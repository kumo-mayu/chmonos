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

/// <summary>保管した通知の一覧の先頭の行（月の見出しと件数。行の枠の上側）。</summary>
public sealed class ArchiveHeadLine(InboxArchiveViewModel archive) : InboxLine
{
    public InboxArchiveViewModel Archive { get; } = archive;
}

/// <summary>
/// 保管した通知を読む見方（ユーザ案 2026-11-10「その形で良いです」）。通知の画面の中で切り替え、月ごとに読むだけ（既読・行のボタンは出さない）。
///
/// **月を丸ごと読まない**：50MB の月を丸ごと読むと作業セットが 359MB になった。月の目録（位置・題・日時だけ）を作り、
/// 新しい順に <see cref="PageSize"/> 件ずつ中身を読み、流して終わりに近づいたら続きを読む（<see cref="NotificationArchive.IndexAsync"/>）。
/// 商品名で絞るのは目録の題で照らすので、打ち替えても月を流し直さない。
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

    private NotificationArchiveIndex? _index;
    private IReadOnlyList<NotificationArchiveEntry> _matching = [];

    /// <summary>目録の中で、読みに出した件数（読み終えた物と読んでいる最中の物）。</summary>
    private int _requested;

    /// <summary>読みの番号。月や絞りを替えたら上がり、前の読みの結果は当てない（最後に頼んだ物だけを出す）。</summary>
    private int _turn;

    private ArchiveMonthChoice? _selectedMonth;
    private string _nameFilter;
    private ArchiveState _state = ArchiveState.Loading;
    private bool _loadingMore;

    private enum ArchiveState
    {
        Loading,
        Ready,
        NoMonths,
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
        Pending = StartAsync(month);
    }

    /// <summary>試験が待つ：最後に始めた読み。</summary>
    internal Task Pending { get; private set; }

    /// <summary>行から商品を開けなかったときの知らせ。一覧の見出しの下の1行（今の通知の ListNotice と同じ置き場）。</summary>
    public AreaNotice Notice { get; } = new();

    public ObservableCollection<ArchiveMonthChoice> Months { get; } = [];

    /// <summary>月と「商品名で絞る」の欄を出すか（保管した月が無ければ、選ぶ物も絞る物も無い）。</summary>
    public bool HasMonths => Months.Count > 0;

    /// <summary>先頭は月の見出しの行、続いて知らせの行（<see cref="InboxRowLine"/>）。</summary>
    public ObservableCollection<object> Lines { get; } = [];

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
            Pending = LoadMonthAsync(value.Month);
        }
    }

    public string MonthLabel => _selectedMonth?.Label ?? string.Empty;

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

            if (_index is not null)
            {
                Pending = ApplyFilterAsync();
            }
        }
    }

    public bool IsFiltering => _nameFilter.Trim().Length > 0;

    /// <summary>この月の件数。絞っているときは「全体 中 当てはまる数」（アバター名で絞る欄と同じ形）。</summary>
    public string CountText => _index is null
        ? string.Empty
        : IsFiltering
            ? $"{_index.Newest.Count:N0} 件中 {_matching.Count:N0} 件"
            : $"{_index.Newest.Count:N0} 件";

    /// <summary>読めなかった所がある月では、その分を出していないことを言う（黙って欠けさせない）。</summary>
    public string DamageText => _index is { } index && index.UnreadableLines + index.UnreadableBlocks > 0
        ? "この月の一部を読めなかったため、その分は表示していません。"
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
        _ when IsFiltering => "当てはまる通知はありません",
        _ => "この月の通知を読めませんでした",
    };

    public string EmptyNote => _state switch
    {
        ArchiveState.Loading => string.Empty,
        ArchiveState.Failed => "ほかのアプリが開いているかもしれません。少し待ってから、月を選び直してください。",
        ArchiveState.NoMonths when _services.Settings.NotificationArchiveMegabytes == 0 =>
            "設定の「通知の保管に使う容量」を0より大きくすると、古い通知をここに保管します。",
        ArchiveState.NoMonths => "通知に残す件数を超えた古い既読の通知が、ここに月ごとに保管されます。",
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

    private async Task LoadMonthAsync(string month)
    {
        var turn = ++_turn;
        IsLoadingMore = false;
        _index = null;
        _matching = [];
        _requested = 0;
        Lines.Clear();
        SetState(ArchiveState.Loading);

        NotificationArchiveIndex index;
        try
        {
            index = await Task.Run(() => _services.Store.NotificationArchive.IndexAsync(month));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
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

        _index = index;
        await ApplyFilterAsync();
    }

    private async Task ApplyFilterAsync()
    {
        if (_index is not { } index)
        {
            return;
        }

        var turn = ++_turn;
        IsLoadingMore = false;
        var filter = _nameFilter;
        var matching = await Task.Run(() => index.Matching(filter));
        if (turn != _turn)
        {
            return;
        }

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
        if (_index is not { } index || _requested >= _matching.Count)
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
                records = await Task.Run(() => _services.Store.NotificationArchive.ReadEntriesAsync(index, page));
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
