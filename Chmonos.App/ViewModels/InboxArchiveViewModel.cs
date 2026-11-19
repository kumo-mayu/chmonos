using System.Collections.ObjectModel;
using System.IO;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.App.ViewModels;

/// <summary>月を選ぶ欄の1項目。</summary>
public sealed record ArchiveMonthChoice(string Month, string Label)
{
    public override string ToString() => Label;
}

/// <summary>変更の種類を選ぶ欄の1項目（件数はすべての月の合計）。</summary>
public sealed record ArchiveKindChoice(NotificationKind Kind, string Label)
{
    public override string ToString() => Label;
}

/// <summary>保管した通知の一番上をどれで切り替えるか（open 146・ユーザ判断 2026-11-19）。</summary>
public enum ArchiveLevel
{
    /// <summary>年月。月を1つ選び、その月の通知を全部の商品まとめて新しい順に。</summary>
    Month,

    /// <summary>商品。商品を1つ選び、すべての月からその商品の通知を新しい順に。</summary>
    Item,

    /// <summary>変更の種類。種類を1つ選び、すべての月からその種類の通知を新しい順に。</summary>
    Kind,
}

/// <summary>保管した通知の一覧の先頭の行（見出しと件数。行の枠の上側）。</summary>
public sealed class ArchiveHeadLine(InboxArchiveViewModel archive) : InboxLine
{
    public InboxArchiveViewModel Archive { get; } = archive;
}

/// <summary>
/// 保管した通知を読む見方（ユーザ案 2026-11-10「その形で良いです」）。通知の画面の中で切り替え、読むだけ（既読・行のボタンは出さない）。
///
/// **月を丸ごと読まない**：50MB の月を丸ごと読むと作業セットが 359MB になった。目録（位置・題・日時・種類・商品だけ）を作り、
/// 新しい順に <see cref="PageSize"/> 件ずつ中身を読み、流して終わりに近づいたら続きを読む（<see cref="NotificationArchive.IndexAsync"/>）。
/// 商品名で絞るのは目録の題で照らすので、打ち替えても月を流し直さない。
///
/// 一番上は「年月・商品・変更の種類」で切り替える（open 146）。商品と種類は月をまたぐので、初めてどちらかへ切り替えた時に、
/// 全部の月の目録を裏で一度だけ作る（<see cref="NotificationArchive.CatalogAsync"/>。変わっていない月は控えを使う）。
/// </summary>
public sealed class InboxArchiveViewModel : ViewModelBase
{
    /// <summary>
    /// 1回に読む件数（ユーザ案 2026-11-10）。続きを読むたびに月をもう一度流す（50MB の月で約0.5秒）ので、
    /// 少なすぎると流すたびに待ち、多すぎると最初に出るまで待つ。行は1件 100px ほどなので、100件で画面の数十枚ぶん。
    /// </summary>
    public const int PageSize = 100;

    private readonly AppServiceContainer _services;
    private readonly Func<NotificationRecord, NotificationRow> _createRow;
    private readonly ArchiveHeadLine _head;

    /// <summary>今の見方で読む目録（年月なら選んだ月だけ、商品・種類なら全部の月）。</summary>
    private NotificationArchiveCatalog? _source;

    /// <summary>全部の月の目録。商品・種類へ初めて切り替えた時に作る（読み終えるまでは読みかけの物）。</summary>
    private Task<NotificationArchiveCatalog>? _allMonths;

    private IReadOnlyDictionary<string, string> _itemChoices = new Dictionary<string, string>();
    private IReadOnlyList<NotificationArchiveCatalogEntry> _matching = [];

    /// <summary>今の見方で絞る前の数（件数の「全体」）。</summary>
    private int _total;

    /// <summary>目録の中で、読みに出した件数（読み終えた物と読んでいる最中の物）。</summary>
    private int _requested;

    /// <summary>読みの番号。見方・月・絞りを替えたら上がり、前の読みの結果は当てない（最後に頼んだ物だけを出す）。</summary>
    private int _turn;

    private ArchiveLevel _level = ArchiveLevel.Month;
    private ArchiveMonthChoice? _selectedMonth;
    private ArchiveKindChoice? _selectedKind;
    private string? _selectedItem;
    private string _nameFilter;
    private ArchiveState _state = ArchiveState.Loading;
    private bool _loadingMore;

    private enum ArchiveState
    {
        Loading,
        Ready,
        NoMonths,
        NeedItem,
        Failed,
    }

    /// <param name="createRow">行は今の通知と同じ形で作る（通知の画面が持つ作り方を借りる）。</param>
    /// <param name="month">開く月（戻るで開き直すとき）。無ければいちばん新しい月。</param>
    public InboxArchiveViewModel(AppServiceContainer services, Func<NotificationRecord, NotificationRow> createRow, string? month = null, string? nameFilter = null)
    {
        _services = services;
        _createRow = createRow;
        _nameFilter = nameFilter ?? string.Empty;
        _head = new ArchiveHeadLine(this);
        PickItemCommand = new RelayCommand(parameter => PickItem(parameter as string));
        Pending = StartAsync(month);
    }

    /// <summary>試験が待つ：最後に始めた読み。</summary>
    internal Task Pending { get; private set; }

    /// <summary>行から商品を開けなかったときの知らせ。一覧の見出しの下の1行（今の通知の ListNotice と同じ置き場）。</summary>
    public AreaNotice Notice { get; } = new();

    public ObservableCollection<ArchiveMonthChoice> Months { get; } = [];

    /// <summary>種類の欄の項目。全部の月の目録ができてから並ぶ。</summary>
    public ObservableCollection<ArchiveKindChoice> Kinds { get; } = [];

    /// <summary>商品で選ぶ欄の候補（商品名）。全部の月の目録ができてから並ぶ。</summary>
    public IReadOnlyList<string> ItemCandidates => [.. _itemChoices.Keys];

    /// <summary>商品で選ぶ欄で確定した（引数は選んだ候補の文字）。</summary>
    public RelayCommand PickItemCommand { get; }

    /// <summary>見方の切り替え・月・「商品名で絞る」の欄を出すか（保管した月が無ければ、選ぶ物も絞る物も無い）。</summary>
    public bool HasMonths => Months.Count > 0;

    /// <summary>先頭は見出しの行、続いて知らせの行（<see cref="InboxRowLine"/>）。</summary>
    public ObservableCollection<object> Lines { get; } = [];

    public ArchiveLevel Level
    {
        get => _level;
        set
        {
            if (!SetField(ref _level, value))
            {
                return;
            }

            foreach (var name in new[]
            {
                nameof(IsMonthLevel), nameof(IsItemLevel), nameof(IsKindLevel), nameof(ShowsNameFilter), nameof(HeadLabel),
            })
            {
                OnPropertyChanged(name);
            }

            Pending = ReloadAsync();
        }
    }

    /// <summary>切り替えの3つの区切り（改変の画面の HubSegment と同じ見た目の RadioButton）が結ぶ。</summary>
    public bool IsMonthLevel
    {
        get => _level == ArchiveLevel.Month;
        set
        {
            if (value)
            {
                Level = ArchiveLevel.Month;
            }
        }
    }

    public bool IsItemLevel
    {
        get => _level == ArchiveLevel.Item;
        set
        {
            if (value)
            {
                Level = ArchiveLevel.Item;
            }
        }
    }

    public bool IsKindLevel
    {
        get => _level == ArchiveLevel.Kind;
        set
        {
            if (value)
            {
                Level = ArchiveLevel.Kind;
            }
        }
    }

    public ArchiveMonthChoice? SelectedMonth
    {
        get => _selectedMonth;
        set
        {
            if (value is null || !SetField(ref _selectedMonth, value))
            {
                return;
            }

            OnPropertyChanged(nameof(MonthLabel));
            OnPropertyChanged(nameof(HeadLabel));
            if (_level == ArchiveLevel.Month)
            {
                Pending = ReloadAsync();
            }
        }
    }

    public ArchiveKindChoice? SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (value is null || !SetField(ref _selectedKind, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HeadLabel));
            if (_level == ArchiveLevel.Kind)
            {
                Pending = ReloadAsync();
            }
        }
    }

    /// <summary>商品で選ぶ欄で選んだ商品（候補の文字）。</summary>
    public string? SelectedItem => _selectedItem;

    public string MonthLabel => _selectedMonth?.Label ?? string.Empty;

    /// <summary>一覧の先頭の見出し：選んだ月・商品・種類。</summary>
    public string HeadLabel => _level switch
    {
        ArchiveLevel.Item => _selectedItem ?? string.Empty,
        ArchiveLevel.Kind => _selectedKind?.Label ?? string.Empty,
        _ => MonthLabel,
    };

    /// <summary>「商品名で絞る」は商品で見るときは出さない（商品を選ぶ欄と重なる）。</summary>
    public bool ShowsNameFilter => _level != ArchiveLevel.Item;

    /// <summary>「商品名で絞る」欄。打ち替えたら目録の題で照らし直し、最初の100件から読み直す。</summary>
    public string NameFilter
    {
        get => _nameFilter;
        set
        {
            if (!SetField(ref _nameFilter, value ?? string.Empty))
            {
                return;
            }

            if (_source is not null && _level != ArchiveLevel.Item)
            {
                Pending = ApplyFilterAsync();
            }
        }
    }

    public bool IsFiltering => _level != ArchiveLevel.Item && _nameFilter.Trim().Length > 0;

    /// <summary>見ている物の件数。絞っているときは「全体 中 当てはまる数」（アバター名で絞る欄と同じ形）。</summary>
    public string CountText => _source is null || _state != ArchiveState.Ready
        ? string.Empty
        : IsFiltering
            ? $"{_total:N0} 件中 {_matching.Count:N0} 件"
            : $"{_matching.Count:N0} 件";

    /// <summary>読めなかった所がある月では、その分を出していないことを言う（黙って欠けさせない）。</summary>
    public string DamageText => _source is { HasDamage: true }
        ? _level == ArchiveLevel.Month
            ? "この月の一部を読めなかったため、その分は表示していません。"
            : "一部の月を読めなかったため、その分は表示していません。"
        : string.Empty;

    public bool HasDamage => DamageText.Length > 0;

    public bool HasMore => _requested < _matching.Count;

    /// <summary>続きを読んでいる間（一覧の終わりに「読み込んでいます…」）。</summary>
    public bool IsLoadingMore
    {
        get => _loadingMore;
        private set => SetField(ref _loadingMore, value);
    }

    public bool IsEmpty => _state != ArchiveState.Ready || _matching.Count == 0;

    /// <summary>
    /// 空の表示（ui-empty-and-errors.md の3通り）：読み込み中・絞って0件（解除の仕方）・そもそも無い（並ぶようにする方法）。
    /// 保管が 0（保管しない）で月が無いときは、容量を 0 より大きくすると並ぶことを言う。
    /// </summary>
    public string EmptyTitle => _state switch
    {
        ArchiveState.Loading => "読み込んでいます…",
        ArchiveState.Failed => "保管した通知を読めませんでした",
        ArchiveState.NoMonths => "保管した通知はありません",
        ArchiveState.NeedItem => "商品を選んでください",
        _ when IsFiltering => "当てはまる通知はありません",
        _ => "この月の通知を読めませんでした",
    };

    public string EmptyNote => _state switch
    {
        ArchiveState.Loading => string.Empty,
        ArchiveState.Failed => "ほかのアプリが開いているかもしれません。少し待ってから、もう一度選んでください。",
        ArchiveState.NoMonths when _services.Settings.NotificationArchiveMegabytes == 0 =>
            "設定の「通知の保管に使う容量」を0より大きくすると、古い通知をここに保管します。",
        ArchiveState.NoMonths => "通知に残す件数を超えた古い既読の通知が、ここに月ごとに保管されます。",
        ArchiveState.NeedItem => "上の欄に商品名を入れて選ぶと、その商品の通知をすべての月から並べます。",
        _ when IsFiltering && _level == ArchiveLevel.Kind => "商品名の言葉を変えるか、別の種類を選ぶか、絞り込みを消してください。",
        _ when IsFiltering => "商品名の言葉を変えるか、別の月を選ぶか、絞り込みを消してください。",
        _ => "この月のファイルが壊れているかもしれません。",
    };

    public bool HasEmptyNote => EmptyNote.Length > 0;

    private async Task StartAsync(string? month)
    {
        IReadOnlyList<string> months;
        try
        {
            months = await Task.Run(() => _services.Store.NotificationArchive.Months());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetState(ArchiveState.Failed);
            return;
        }

        foreach (var name in months.Reverse())
        {
            Months.Add(new ArchiveMonthChoice(name, LabelOf(name)));
        }

        OnPropertyChanged(nameof(HasMonths));
        if (Months.Count == 0)
        {
            SetState(ArchiveState.NoMonths);
            return;
        }

        // 選ぶと読み始める（SelectedMonth の set）。読みの終わりを待てるよう、その読みを返す
        SelectedMonth = Months.FirstOrDefault(choice => choice.Month == month) ?? Months[0];
        await Pending;
    }

    /// <summary>「2026-10」→「2026年10月」。</summary>
    internal static string LabelOf(string month)
        => month.Length == 7 && int.TryParse(month[..4], out var year) && int.TryParse(month[5..], out var number)
            ? $"{year}年{number}月"
            : month;

    private void PickItem(string? name)
    {
        if (name is null || !_itemChoices.ContainsKey(name) || name == _selectedItem)
        {
            return;
        }

        _selectedItem = name;
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(HeadLabel));
        if (_level == ArchiveLevel.Item)
        {
            Pending = ReloadAsync();
        }
    }

    /// <summary>今の見方で目録を作り直し、最初の100件から読み直す。</summary>
    private async Task ReloadAsync()
    {
        var turn = ++_turn;
        IsLoadingMore = false;
        _source = null;
        _matching = [];
        _requested = 0;
        Lines.Clear();
        if (Months.Count == 0)
        {
            SetState(_state == ArchiveState.Failed ? ArchiveState.Failed : ArchiveState.NoMonths);
            return;
        }

        SetState(ArchiveState.Loading);

        NotificationArchiveCatalog source;
        try
        {
            if (_level == ArchiveLevel.Month)
            {
                if (_selectedMonth is not { } month)
                {
                    return;
                }

                var index = await Task.Run(() => _services.Store.NotificationArchive.IndexAsync(month.Month));
                source = NotificationArchiveCatalog.OfMonth(index);
            }
            else
            {
                source = await AllMonthsAsync();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 次に切り替えた時に作り直す（読めなかった物を控えに残さない）
            _allMonths = null;
            if (turn == _turn)
            {
                SetState(ArchiveState.Failed);
            }

            return;
        }

        if (turn != _turn)
        {
            return;
        }

        _source = source;
        await ApplyFilterAsync();
    }

    /// <summary>
    /// 全部の月の目録。初めて頼まれた時に裏で作り、種類と商品の欄を埋める。
    /// 目録・候補・種類・最初の種類の選択までを1つの Task にし、どの読みもその全部が済んでから絞りの条件を取る
    /// （2026-11-19 の Codex の点検 3回目の 2 まで、目録だけを分け合っていた。商品へ切り替えて作っている間に種類へ切り替えると、
    /// 後の読みが種類を選ぶ前に絞り、欄は最初の種類なのに一覧は全部の種類が混ざった）
    /// </summary>
    private Task<NotificationArchiveCatalog> AllMonthsAsync() => _allMonths ??= BuildAllMonthsAsync();

    private async Task<NotificationArchiveCatalog> BuildAllMonthsAsync()
    {
        var catalog = await Task.Run(() => _services.Store.NotificationArchive.CatalogAsync());
        if (CatalogBuiltForTests is { } built)
        {
            await built();
        }

        // 候補と種類の数えは全部の知らせを回るので、数千件でも画面を止めないよう裏で数える
        var (choices, kinds) = await Task.Run(() => (catalog.ItemChoices(), catalog.Kinds()));
        _itemChoices = choices;
        OnPropertyChanged(nameof(ItemCandidates));
        foreach (var (kind, count) in kinds)
        {
            Kinds.Add(new ArchiveKindChoice(kind, $"{InboxViewModel.KindLabel(kind)}（{count:N0}）"));
        }

        // 選んだだけで読み直さない（読み直すのは待っている側。ここで読み直すと、読みが2本になり先の方が待たれない）
        if (_selectedKind is null && Kinds.Count > 0)
        {
            _selectedKind = Kinds[0];
            OnPropertyChanged(nameof(SelectedKind));
            OnPropertyChanged(nameof(HeadLabel));
        }

        return catalog;
    }

    /// <summary>試験だけが使う：全部の月の目録ができた後、候補と種類を作る前に待つ（作っている間に見方を変える順を作る）。</summary>
    internal Func<Task>? CatalogBuiltForTests { get; set; }

    private async Task ApplyFilterAsync()
    {
        if (_source is not { } source)
        {
            return;
        }

        var turn = ++_turn;
        IsLoadingMore = false;
        var filter = _nameFilter;
        var level = _level;
        var kind = _selectedKind?.Kind;
        IReadOnlySet<string>? items = _selectedItem is { } picked && _itemChoices.TryGetValue(picked, out var id)
            ? new HashSet<string>(StringComparer.Ordinal) { id }
            : null;

        if (level == ArchiveLevel.Item && items is null)
        {
            _matching = [];
            _requested = 0;
            Lines.Clear();
            SetState(ArchiveState.NeedItem);
            return;
        }

        var (total, matching) = await Task.Run(() => level switch
        {
            ArchiveLevel.Kind => (source.Matching(kind: kind).Count, source.Matching(kind: kind, name: filter)),
            ArchiveLevel.Item => (0, source.Matching(itemIds: items)),
            _ => (source.Newest.Count, source.Matching(name: filter)),
        });
        if (turn != _turn)
        {
            return;
        }

        _total = total;
        _matching = matching;
        _requested = 0;
        Lines.Clear();
        SetState(ArchiveState.Ready);
        await ReadNextAsync(turn);
    }

    /// <summary>一覧の終わりに近づいたら画面が呼ぶ。読んでいる最中・残りが無いときは何もしない。</summary>
    public Task LoadMoreAsync()
    {
        if (IsLoadingMore || !HasMore || _state != ArchiveState.Ready)
        {
            return Task.CompletedTask;
        }

        Pending = ReadNextAsync(_turn);
        return Pending;
    }

    private async Task ReadNextAsync(int turn)
    {
        if (_source is not { } source || _requested >= _matching.Count)
        {
            return;
        }

        var page = _matching.Skip(_requested).Take(PageSize).ToList();
        _requested += page.Count;
        IsLoadingMore = true;
        try
        {
            IReadOnlyList<NotificationRecord> records;
            try
            {
                records = await Task.Run(() => source.ReadAsync(_services.Store.NotificationArchive, page));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (turn == _turn)
                {
                    _requested -= page.Count;
                    SetState(ArchiveState.Failed);
                }

                return;
            }

            if (turn != _turn)
            {
                return;
            }

            if (Lines.Count == 0 && records.Count > 0)
            {
                Lines.Add(_head);
            }

            if (Lines.LastOrDefault() is InboxLine last)
            {
                last.IsLast = false;
            }

            foreach (var record in records)
            {
                Lines.Add(new InboxRowLine(_createRow(record)));
            }

            if (Lines.LastOrDefault() is InboxLine end)
            {
                end.IsLast = true;
            }
        }
        finally
        {
            if (turn == _turn)
            {
                IsLoadingMore = false;
                Notify();
            }
        }
    }

    /// <summary>読み込んだ知らせの数（試験と、先頭の見出しの行を除いた数）。</summary>
    internal IReadOnlyList<NotificationRow> Rows => Lines.OfType<InboxRowLine>().Select(line => line.Row).ToList();

    private void SetState(ArchiveState state)
    {
        _state = state;
        Notify();
    }

    private void Notify()
    {
        foreach (var name in new[]
        {
            nameof(IsEmpty), nameof(EmptyTitle), nameof(EmptyNote), nameof(HasEmptyNote),
            nameof(CountText), nameof(IsFiltering), nameof(DamageText), nameof(HasDamage), nameof(HasMore),
        })
        {
            OnPropertyChanged(name);
        }
    }
}
