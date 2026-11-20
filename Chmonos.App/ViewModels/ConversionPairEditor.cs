using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// アバターの管理の「変換」の一覧の上の「追加した組」の節（<c>docs/spec/conversion.md</c>「人が足す組」・ユーザ判断 2026-11-20）。
/// 組（<c>conversion-pairs.json</c>）をプロファイルの商品と同じ形の行で並べ（元 → 先と短いメモ）、行を選ぶと右に組の中身（元・先・メモ・外す）を出す。
/// 「組を追加」の行で元と先の両方にアバターか共通素体を選び、「追加」を押した時だけ足す（ユーザ判断 2026-11-20：候補を決めた瞬間に足すのをやめた）。
/// 「逆も」は置かない（逆はもう1つの組として足す）。書き込みは <see cref="UiCommand.ChangeConversionPairs"/> 1本。
/// </summary>
public sealed class ConversionPairEditor : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private readonly Func<string, ItemRecord?>? _findItem;
    private readonly Services.ThumbnailLoader? _thumbnails;

    /// <summary>組を足した・外した後に、変換経由を使う所（検索の絞り込み・アバターの詳細の札）へ知らせる。メモだけの変更では呼ばない（計算は変わらない）。</summary>
    private readonly Action? _written;

    /// <summary>メモを書いた後（アバターの詳細の札の吹き出しだけが変わる）。</summary>
    private readonly Action? _memoWritten;

    /// <summary>選んだ行が替わった（プロファイルの商品の選びと、右に出す物を入れ替える）。</summary>
    private readonly Action<ConversionPairRow?>? _selectionChanged;

    /// <summary>行のメモの自動保存。書く先（行）と文は打った時点で決める（<see cref="MemoAutoSave{TOwner}"/>）。</summary>
    private readonly MemoAutoSave<ConversionPairRow> _memo;

    /// <summary>候補に出した共通素体の行から素体の名前を引く表（アバターと同じ名前の素体を取り違えない）。</summary>
    private Dictionary<string, string> _basesBySuggestion = new(StringComparer.CurrentCultureIgnoreCase);

    private Dictionary<string, string> _avatarIdsBySuggestion = new(StringComparer.CurrentCultureIgnoreCase);

    private Dictionary<string, Controls.SuggestInfo> _info = new(StringComparer.CurrentCultureIgnoreCase);

    private string _fromText = string.Empty;
    private string _toText = string.Empty;
    private bool _isExpanded = true;
    private ConversionPairRow? _selectedRow;
    private ConversionPairSort _sort;

    /// <summary>元ごとのまとまり（今の並び）。作り直しでも同じ元のまとまりは同じ物を使う（見出しに止まったフォーカスを飛ばさない）。</summary>
    private List<ConversionPairGroup> _groups = [];

    /// <summary>
    /// 一覧の行を出し入れしている間。ListBox は消えた行を選んでいると選びに null を書き戻すので、その間の書き戻しは受けない
    /// （受けると、組を選んだまま畳む・作り直すだけで決めていない商品へ右が移る）
    /// </summary>
    private bool _syncingEntries;

    public ConversionPairEditor(
        AppServiceContainer services,
        Services.ThumbnailLoader? thumbnails = null,
        Func<string, ItemRecord?>? findItem = null,
        Action? written = null,
        Action? memoWritten = null,
        Action<ConversionPairRow?>? selectionChanged = null,
        Func<ConversionEnd, bool>? canOpen = null,
        Action<ConversionEnd>? open = null)
    {
        _services = services;
        _thumbnails = thumbnails;
        _findItem = findItem;
        _written = written;
        _memoWritten = memoWritten;
        _selectionChanged = selectionChanged;
        CanOpen = canOpen ?? (_ => false);
        Open = open ?? (_ => { });
        _memo = new MemoAutoSave<ConversionPairRow>(SaveMemoAsync);
        AddCommand = new RelayCommand(() => AddAsync().Forget());
        _sort = ConversionPairSortChoice.FromKey(services.UiState.ConversionPairSort);
    }

    /// <summary>並びの選択肢。</summary>
    public IReadOnlyList<ConversionPairSortChoice> SortChoices => ConversionPairSortChoice.All;

    /// <summary>選んでいる並び。替えたら覚え（ui-state）、行を並べ直す（行は作り直さないので、選びとメモの待ちはそのまま）。</summary>
    public ConversionPairSortChoice SelectedSortChoice
    {
        get => SortChoices.First(choice => choice.Sort == _sort);
        set
        {
            if (value is null || value.Sort == _sort)
            {
                return;
            }

            _sort = value.Sort;
            OnPropertyChanged();
            var key = value.Key;
            _services.Commands.ExecuteAsync(new UiCommand.ChangeUiState(state => state with { ConversionPairSort = key })).Forget();
            Arrange();
        }
    }

    /// <summary>今の並び。</summary>
    public ConversionPairSort Sort => _sort;

    /// <summary>元ごとのまとまり（今の並び）。</summary>
    public IReadOnlyList<ConversionPairGroup> Groups => _groups;

    /// <summary>
    /// 一覧に出す物（まとまりの見出しと、開いたまとまりの組の行）。行の出し入れは差分で当てる
    /// （丸ごと差し替えると見出しの部品も作り直され、キーで畳んだ直後にフォーカスが飛ぶ）
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<object> Entries { get; } = [];

    /// <summary>
    /// 一覧（ListBox）の選び。組の行なら組を選び、見出しは選ばない（見出しは畳むだけ）。行を出し入れしている間の null は受けない
    /// </summary>
    public object? SelectedEntry
    {
        get => _selectedRow is { } row && Entries.Contains(row) ? row : null;
        set
        {
            if (_syncingEntries || value is ConversionPairGroup)
            {
                return;
            }

            SelectedRow = value as ConversionPairRow;
        }
    }

    /// <summary>端（アバター・共通素体）へ移れるか。登録簿に無いアバター・消えた素体は移れない（右の元・先を押せなくする）。</summary>
    internal Func<ConversionEnd, bool> CanOpen { get; }

    /// <summary>端のアバター・共通素体へ移る（アバターの管理の中で見方を替えて選ぶ）。</summary>
    internal Action<ConversionEnd> Open { get; }

    /// <summary>組の行（今の並びで、まとまりを順につないだ物。畳んだまとまりの組も含む。外した後の「次の組」もこの順で引く）。</summary>
    public IReadOnlyList<ConversionPairRow> Rows { get; private set; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>節の見出し（数を添える。畳んでいても何組あるか分かるように）。</summary>
    public string HeaderText => IsFiltering ? $"追加した組（{VisibleRows.Count}/{Rows.Count}）" : $"追加した組（{Rows.Count}）";

    /// <summary>節を開いているか。見方を替えて戻っても保つ（View に持たせると作り直しで戻る）。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    /// <summary>
    /// 選んでいる組（右に中身を出す）。プロファイルの商品の選びとはどちらか1つ（<see cref="AvatarsViewModel"/> が入れ替える）。
    /// 選び直す前に待っているメモを書き切る：自動保存の待ちは1つしか持てず、別の組で打ち始めると前の組に打った文が消える
    /// </summary>
    public ConversionPairRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(_selectedRow, value))
            {
                return;
            }

            _memo.Flush();
            _selectedRow = value;
            RevealSelected(clearFilterIfHidden: true);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedEntry));
            OnPropertyChanged(nameof(HasSelection));
            _selectionChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// 選んだ組のまとまりが畳んであれば開いて覚える（足した・既にある組へ移った・外した後の次の組・戻るで選び直した組を、畳みの中に隠さない）。
    /// 覚えないと、「変換」に入るたびの読み直しでまた畳まれ、選んだ組が見えなくなる。
    /// 絞っている間は、絞りの間だけ開く（畳んだ事の記録は書き換えない）。選んだ組が絞りに当たらない時は、
    /// <paramref name="clearFilterIfHidden"/> なら絞りを外して見せる（足した組・既にある組・外した後の次の組・戻るで選び直した組。
    /// 「アバター」の見方で、絞りで外れたアバターへ移る時と同じ）。外さない時（読み直し・語を打った）は、右に組を出したまま一覧には出さない
    /// </summary>
    private void RevealSelected(bool clearFilterIfHidden)
    {
        if (_selectedRow is not { } row || _groups.FirstOrDefault(group => group.Rows.Contains(row)) is not { } group)
        {
            return;
        }

        if (IsFiltering)
        {
            if (group.ShownRows.Contains(row))
            {
                if (!group.IsExpanded)
                {
                    group.SetShownWhileFiltering(true);
                    SyncEntries();
                }

                return;
            }

            if (!clearFilterIfHidden)
            {
                return;
            }

            // 絞りを外すと、下で覚えた開閉に戻ってから開いて覚える（絞りが無い時と同じ）
            Query = string.Empty;
        }

        if (group.IsExpanded)
        {
            return;
        }

        group.SetExpandedQuietly(true);
        RememberCollapsed(group.From, collapsed: false);
        SyncEntries();
    }

    /// <summary>見出しの三角で開閉した（<see cref="ConversionPairGroup.IsExpanded"/> から呼ぶ）。覚えて、行を出し入れする。</summary>
    internal void GroupToggled(ConversionPairGroup group)
    {
        RememberCollapsed(group.From, collapsed: !group.IsExpanded);
        SyncEntries();
    }

    /// <summary>絞っている間に見出しで開閉した。覚えずに行を出し入れするだけ。</summary>
    internal void GroupToggledWhileFiltering() => SyncEntries();

    // ---- 組を探す欄（2026-11-20・open.md「変換：組を探す欄・右の欄の絵・畳んだ左の欄の名前」） ----

    private string _query = string.Empty;
    private ConversionPairSearchScope _scope = ConversionPairSearchScope.Both;

    /// <summary>
    /// 探す語。「アバター」の見方の探す欄と同じ照らし方（<see cref="ConversionPairSearch"/>）で組を絞る。
    /// 当たった組のまとまりは開いて見せるが、畳んだ事の記録（ui-state）は書き換えない。組の選び（右の欄）は、当たらなくても外さない
    /// </summary>
    public string Query
    {
        get => _query;
        set
        {
            var wasFiltering = IsFiltering;
            if (!SetField(ref _query, value ?? string.Empty))
            {
                return;
            }

            if (IsFiltering)
            {
                // 語を替えるたびに、当たったまとまりを開いた姿から見せ直す。節（追加した組）を畳んでいても開く（打った結果が見えないと絞れたか分からない）
                foreach (var group in _groups)
                {
                    group.SetShownWhileFiltering(true);
                }

                IsExpanded = true;
            }
            else if (wasFiltering)
            {
                // 三角を覚えた姿に戻す
                foreach (var group in _groups)
                {
                    group.SetShownWhileFiltering(true);
                }
            }

            Arrange();
            if (wasFiltering && !IsFiltering)
            {
                // 絞りの間に選んだ組が、覚えた姿で畳んだまとまりにあれば開いて覚える（選んだ組のまとまりは開いて覚える決まりと同じ）
                RevealSelected(clearFilterIfHidden: false);
            }
        }
    }

    /// <summary>絞っているか（空白だけの語では絞らない）。</summary>
    public bool IsFiltering => _query.Trim().Length > 0;

    /// <summary>語を当てる端（元・先・両方。既定は両方。覚えない）。</summary>
    public ConversionPairSearchScope Scope
    {
        get => _scope;
        set
        {
            if (SetField(ref _scope, value))
            {
                OnPropertyChanged(nameof(IsScopeBoth));
                OnPropertyChanged(nameof(IsScopeFrom));
                OnPropertyChanged(nameof(IsScopeTo));
                if (IsFiltering)
                {
                    foreach (var group in _groups)
                    {
                        group.SetShownWhileFiltering(true);
                    }

                    Arrange();
                }
            }
        }
    }

    public bool IsScopeBoth => _scope == ConversionPairSearchScope.Both;

    public bool IsScopeFrom => _scope == ConversionPairSearchScope.From;

    public bool IsScopeTo => _scope == ConversionPairSearchScope.To;

    /// <summary>つながったボタンの「両方」「元」「先」（引数は both・from・to）。</summary>
    public RelayCommand SetScopeCommand => _setScopeCommand ??= new RelayCommand(key => Scope = (key as string) switch
    {
        "from" => ConversionPairSearchScope.From,
        "to" => ConversionPairSearchScope.To,
        _ => ConversionPairSearchScope.Both,
    });

    private RelayCommand? _setScopeCommand;

    /// <summary>探す欄の語を消す（当てはまる組が無い時の「絞り込みを消す」）。</summary>
    public RelayCommand ClearQueryCommand => _clearQueryCommand ??= new RelayCommand(() => Query = string.Empty);

    private RelayCommand? _clearQueryCommand;

    /// <summary>絞りに当たる組（今の並び）。絞っていなければ <see cref="Rows"/> と同じ。外した後の「次の組」はこの順で引く。</summary>
    public IReadOnlyList<ConversionPairRow> VisibleRows { get; private set; } = [];

    /// <summary>組はあるのに、語に当てはまる組が無い（一覧の代わりに1行で言う）。</summary>
    public bool ShowsNoMatches => HasRows && IsFiltering && VisibleRows.Count == 0;

    public bool HasVisibleRows => VisibleRows.Count > 0;

    /// <summary>畳んだまとまりの元を ui-state に書く。錠の中の今の値に当てる（ほかの所で替えた畳み方を消さない）。</summary>
    private void RememberCollapsed(ConversionEnd from, bool collapsed)
        => _services.Commands.ExecuteAsync(new UiCommand.ChangeUiState(state =>
        {
            var current = state.ConversionPairCollapsed ?? [];
            return current.Any(end => end.Same(from)) == collapsed
                ? state
                : state with { ConversionPairCollapsed = collapsed ? [.. current, from] : [.. current.Where(end => !end.Same(from))] };
        })).Forget();

    private bool IsCollapsed(ConversionEnd from) => (_services.UiState.ConversionPairCollapsed ?? []).Any(end => end.Same(from));

    /// <summary>
    /// まとまりと行を今の並びで並べ直し、一覧に当てる（行は作り直さない）。
    /// </summary>
    private void Arrange()
    {
        var arranged = ConversionPairOrder.Arrange(
            _groups.Select(group => (group, group.Rows)),
            group => group.Name,
            row => row.ToName,
            row => row.AddedIndex,
            _sort);
        var query = _query.Trim();
        foreach (var (group, rows) in arranged)
        {
            group.SetRows(rows, query.Length == 0 ? rows : [.. rows.Where(row => ConversionPairSearch.Matches(row, query, _scope))]);
        }

        _groups = [.. arranged.Select(item => item.Group)];
        Rows = [.. _groups.SelectMany(group => group.Rows)];
        VisibleRows = [.. _groups.SelectMany(group => group.ShownRows)];
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(VisibleRows));
        OnPropertyChanged(nameof(Groups));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(IsFiltering));
        OnPropertyChanged(nameof(ShowsNoMatches));
        OnPropertyChanged(nameof(HasVisibleRows));
        SyncEntries();
    }

    /// <summary>
    /// 一覧に出す物を、まとまりの見出しと開いたまとまりの行に合わせる。差分で当てる（同じ物は残す）。
    /// 出し入れの間の ListBox の書き戻し（消えた行の選びを null に）は受けず、終わってから今の選びを知らせ直す
    /// </summary>
    private void SyncEntries()
    {
        // 絞っている間は、当たる組の無いまとまりを見出しごと出さない
        var wanted = new List<object>();
        foreach (var group in _groups.Where(group => group.ShownRows.Count > 0))
        {
            wanted.Add(group);
            if (group.IsExpanded)
            {
                wanted.AddRange(group.ShownRows);
            }
        }

        _syncingEntries = true;
        try
        {
            var keep = new HashSet<object>(wanted, ReferenceEqualityComparer.Instance);
            for (var index = Entries.Count - 1; index >= 0; index--)
            {
                if (!keep.Contains(Entries[index]))
                {
                    Entries.RemoveAt(index);
                }
            }

            for (var index = 0; index < wanted.Count; index++)
            {
                if (index < Entries.Count && ReferenceEquals(Entries[index], wanted[index]))
                {
                    continue;
                }

                var at = IndexOfReference(wanted[index], index + 1);
                if (at >= 0)
                {
                    Entries.Move(at, index);
                }
                else
                {
                    Entries.Insert(index, wanted[index]);
                }
            }
        }
        finally
        {
            _syncingEntries = false;
        }

        OnPropertyChanged(nameof(SelectedEntry));
    }

    /// <summary>
    /// 一覧のキーの移り先（↑↓ は ±1、Home・End は端）。行と見出しを渡り、端では止まる（回り込まない・ui-keyboard.md）。
    /// 移り先が組の行なら選ぶ（見出しは止まるだけ）。動かない時は null
    /// </summary>
    internal object? MoveEntry(object? current, int step, bool toEdge = false)
    {
        if (Entries.Count == 0)
        {
            return null;
        }

        var index = current is null ? -1 : IndexOfReference(current, 0);
        var target = toEdge ? (step < 0 ? 0 : Entries.Count - 1)
            : index < 0 ? (step < 0 ? Entries.Count - 1 : 0)
            : Math.Clamp(index + step, 0, Entries.Count - 1);
        if (target == index)
        {
            return null;
        }

        var entry = Entries[target];
        if (entry is ConversionPairRow row)
        {
            SelectedRow = row;
        }

        return entry;
    }

    private int IndexOfReference(object item, int from)
    {
        for (var index = from; index < Entries.Count; index++)
        {
            if (ReferenceEquals(Entries[index], item))
            {
                return index;
            }
        }

        return -1;
    }

    public bool HasSelection => SelectedRow is not null;

    /// <summary>戻るで「変換」へ戻した時に選び直す組（初めて読んだ時に1回だけ使う）。</summary>
    internal ConversionPair? SelectOnLoad { get; set; }

    /// <summary>元と先を選ぶ欄の候補（アバターと、「（共通素体）」を添えた共通素体）。</summary>
    public IReadOnlyList<string> Suggestions { get; private set; } = [];

    public Func<string, Controls.SuggestInfo?> InfoSelector => text => _info.GetValueOrDefault(text);

    public IReadOnlyList<string> GroupHeadings => AvatarSuggestionText.Headings;

    /// <summary>候補の頭の絵（変換扱いのつなぎ先の欄と同じ）。素体かは確定の時と同じ表で先に見る。</summary>
    public Func<string, System.Windows.Media.ImageSource?> IconSelector => text =>
        _thumbnails is not { } thumbnails ? null
        : _basesBySuggestion.TryGetValue(text, out var baseName) ? AvatarBaseIcons.Load(_services, baseName, FindItem, thumbnails.LoadForTile)
        : _avatarIdsBySuggestion.TryGetValue(text, out var id) && AvatarImageSync.IconPath(_services.Paths, id, FindItem(id), _services.Settings.ShowRemovedBoothImages) is { } path
            ? thumbnails.LoadForTile(path)
        : null;

    /// <summary>元の欄の文字。候補を選ぶと候補の表記が入る（KeepsCommittedText）。打った綴りが候補と同じなら選んだのと同じに扱う（ui-input.md）。</summary>
    public string FromText
    {
        get => _fromText;
        set
        {
            if (SetField(ref _fromText, value ?? string.Empty))
            {
                Notice.Clear();
            }
        }
    }

    public string ToText
    {
        get => _toText;
        set
        {
            if (SetField(ref _toText, value ?? string.Empty))
            {
                Notice.Clear();
            }
        }
    }

    /// <summary>
    /// 「追加」（欄の Enter も同じ）。元と先の欄の文字から組を作って足す。候補を決めただけでは足さない
    /// （ユーザ判断 2026-11-20：先の候補を選んだ瞬間に足されるのは、選び間違いを直す間が無かった）
    /// </summary>
    public RelayCommand AddCommand { get; }

    /// <summary>足せなかった・書けなかったときだけ出る1行。</summary>
    public AreaNotice Notice { get; } = new();

    /// <summary>メモの書くのを待っている物があるか（画面を離れる前に書き切る）。</summary>
    public bool IsMemoPending => _memo.IsPending;

    private ItemRecord? FindItem(string itemId) => _findItem?.Invoke(itemId);

    /// <summary>待っているメモを今書く（画面を離れる・閉じる前）。</summary>
    public Task FlushPendingWritesAsync() => _memo.FlushAsync();

    /// <summary>
    /// ファイルと登録簿から読み直す（「変換」に入った時・登録簿を読み直した時）。組は素体の改名・削除と商品 ID の変更でも書き換わるので、
    /// 読み直さないと古い行の「この組を外す」が何も外さない
    /// </summary>
    public void Reload() => ReloadAsync().Forget();

    /// <summary>
    /// 待っているメモを書き切ってからファイルを読む。書き切る前に読むと、打ったばかりのメモが古い文の行で作り直されて画面から消える
    /// （ファイルには後から書かれるので、次に開くまで食い違う）。待つ物が無ければ同期のまま作り直す
    /// </summary>
    internal async Task ReloadAsync()
    {
        await _memo.FlushAsync();
        Rebuild(_services.Store.ConversionPairs.Load());
    }

    /// <summary>
    /// 行を作り直す。選んでいた組は端で引き直して選び直す（行は新しい物に替わる）。消えた組（外した・読み替えで落ちた）なら選びを外す
    /// </summary>
    private void Rebuild(ConversionPairFile file, ConversionPair? select = null)
    {
        var registry = _services.CachedAvatars.Load();
        var names = AvatarNames.Map(registry.Entries);

        // 探す欄の照らしに使う（名前のほか正式名・呼び方）。同じ ID が2件あっても投げないよう、先の1件を使う
        var entries = registry.Entries
            .GroupBy(entry => entry.ItemId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        AvatarRegistryEntry? EntryOf(ConversionEnd end) => end.AvatarItemId is { } id ? entries.GetValueOrDefault(id) : null;

        // 足した・外した後の次の組・戻るで選び直す組は、絞りに当たらなければ絞りを外して見せる。ただの読み直しでは打った語を消さない
        var explicitSelect = select ?? (_selectedRow is null ? SelectOnLoad : null);
        var wanted = select ?? _selectedRow?.Pair ?? SelectOnLoad;
        SelectOnLoad = null;

        // 一覧を差し替えると ListBox が選びに null を書き戻す。それを「選びが外れた」と受けると、組を選び直すまでの間に
        // 決めていない商品を一度選んで右を入れ替える（読み込みも走る）。先に黙って外し、書き戻しは行の出し入れの間は受けない（SyncEntries）
        var previous = _selectedRow;
        _selectedRow = null;

        // 位置は追加した順（配列の位置。足すと末尾に付き、外す・メモ・読み替えでは順を保つ）。空の組を飛ばしても位置は元の配列のまま
        var rows = file.Pairs
            .Select((pair, index) => (pair, index))
            .Where(item => !item.pair.IsEmpty)
            .Select(item => new ConversionPairRow(
                this, item.pair, ConversionPairText.NameOf(item.pair.From, names), ConversionPairText.NameOf(item.pair.To, names), item.index, IconOf)
            {
                FromEntry = EntryOf(item.pair.From),
                ToEntry = EntryOf(item.pair.To),
            })
            .ToList();

        // 元ごとにまとめる。前からあるまとまりは同じ物を使い（開閉と見出しのフォーカスを保つ）、新しいまとまりは覚えた畳み方で始める
        var groups = new List<ConversionPairGroup>();
        var groupRows = new List<List<ConversionPairRow>>();
        foreach (var row in rows)
        {
            var at = groups.FindIndex(group => group.From.Same(row.Pair.From));
            if (at < 0)
            {
                groups.Add(_groups.FirstOrDefault(group => group.From.Same(row.Pair.From) && group.Name == row.FromName)
                    ?? new ConversionPairGroup(this, row.Pair.From, row.FromName, !IsCollapsed(row.Pair.From)));
                groupRows.Add([]);
                at = groups.Count - 1;
            }

            groupRows[at].Add(row);
        }

        for (var index = 0; index < groups.Count; index++)
        {
            // 絵は作り直すたびに引き直す（アバターの見方で画像を替えて戻った時に、古い絵を残さない）
            groups[index].SetIcon(IconOf(groups[index].From, groups[index].Name));
            groups[index].SetRows(groupRows[index], groupRows[index]);
        }

        _groups = groups;

        // 候補の群（所持アバター／共通素体／未所持アバター）と呼び方は、変換扱いのつなぎ先の欄と同じ作り
        _basesBySuggestion = registry.BaseGroups
            .Where(group => !group.Rejected && group.Name.Trim().Length > 0)
            .Select(group => group.Name)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(ConversionPairText.BaseText, name => name, StringComparer.CurrentCultureIgnoreCase);
        _avatarIdsBySuggestion = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        _info = new Dictionary<string, Controls.SuggestInfo>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var entry in registry.Entries.Where(AvatarService.IsAvatar))
        {
            var name = names[entry.ItemId];
            if (_avatarIdsBySuggestion.TryAdd(name, entry.ItemId))
            {
                var group = entry.IsOwnedManually || FindItem(entry.ItemId)?.IsOwned == true
                    ? AvatarSuggestionText.OwnedGroup
                    : AvatarSuggestionText.OtherGroup;
                _info[name] = AvatarSuggestionText.InfoOf(entry, name, group);
            }
        }

        foreach (var baseText in _basesBySuggestion.Keys)
        {
            _info[baseText] = new Controls.SuggestInfo(AvatarSuggestionText.BaseGroup, []);
        }

        Suggestions = [.. _avatarIdsBySuggestion.Keys
            .Concat(_basesBySuggestion.Keys)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCulture)];
        Arrange();
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(Suggestions));

        // 行は新しい物に替わったので、同じ組でも選び直したと必ず知らせる。待っているメモは呼ぶ側が先に書き切っている
        var next = wanted is null ? null : Rows.FirstOrDefault(row => row.Pair.SameEnds(wanted));
        if (previous is null && next is null)
        {
            return;
        }

        _selectedRow = next;
        RevealSelected(clearFilterIfHidden: explicitSelect is not null);
        OnPropertyChanged(nameof(SelectedRow));
        OnPropertyChanged(nameof(SelectedEntry));
        OnPropertyChanged(nameof(HasSelection));
        _selectionChanged?.Invoke(next);
    }

    /// <summary>端の頭の絵（アバターは「アバター」の見方の行と同じ読み方、共通素体は素体の行と同じ）。</summary>
    private ConversionEndIcon IconOf(ConversionEnd end, string name)
    {
        if (end.BaseName is { } baseName)
        {
            return new ConversionEndIcon(baseName, () => AvatarBaseIcons.PathOf(_services, baseName, FindItem), _thumbnails);
        }

        var id = end.AvatarItemId ?? string.Empty;
        return new ConversionEndIcon(
            name, () => AvatarImageSync.IconPath(_services.Paths, id, FindItem(id), _services.Settings.ShowRemovedBoothImages), _thumbnails);
    }

    /// <summary>候補の文字から端を引く。素体の表を先に見る（同じ名前のアバターと取り違えない）。</summary>
    internal ConversionEnd? EndOf(string text)
        => _basesBySuggestion.TryGetValue(text.Trim(), out var baseName) ? ConversionEnd.Base(baseName)
        : _avatarIdsBySuggestion.TryGetValue(text.Trim(), out var id) ? ConversionEnd.Avatar(id)
        : null;

    /// <summary>
    /// 元と先の欄から組を足す。足さない理由（候補に無い・同じ物同士・既にある）はここで言う。
    /// Core の <see cref="ConversionPairs.Add"/> も同じ物を黙って足さない（錠の中の今の値で判定する）が、理由は返さないので画面で先に見る。
    /// 足した組は選んで右に出す（同じ画面の共通素体の「追加」と同じ。既にある組なら、その組を選ぶ）
    /// </summary>
    internal async Task AddAsync()
    {
        if (EndOf(FromText) is not { } from || EndOf(ToText) is not { } to)
        {
            Notice.Warn("候補からアバターか共通素体を選んでください。");
            return;
        }

        var pair = new ConversionPair { From = from, To = to };
        if (pair.PointsToItself)
        {
            Notice.Warn("元と先に同じ物は選べません。");
            return;
        }

        if (Rows.FirstOrDefault(row => row.Pair.SameEnds(pair)) is { } known)
        {
            Notice.Warn($"「{known.Text}」は既にあります。");
            SelectedRow = known;
            return;
        }

        if (await WriteThenRebuildAsync(file => ConversionPairs.Add(file, pair), select: pair))
        {
            // 同じ元から続けて足せるように、元は残して先だけ空にする
            ToText = string.Empty;
        }
    }

    /// <summary>
    /// 外す。外した組を選んでいたなら、同じ位置の次の組（最後なら前の組）を選ぶ。組が無くなったら選びを外す
    /// （右はプロファイルの商品へ戻る・<see cref="AvatarsViewModel"/>）
    /// </summary>
    internal async Task RemoveAsync(ConversionPairRow row)
    {
        // 絞っている間は、見えている組の中で次を引く（見えていない組へ飛ぶと、絞りを外すことになる）
        var visible = VisibleRows.Contains(row) ? VisibleRows : Rows;
        var index = visible.ToList().IndexOf(row);
        var wasSelected = ReferenceEquals(row, _selectedRow);
        ConversionPair? next = null;
        if (wasSelected && index >= 0)
        {
            next = visible.Skip(index + 1).FirstOrDefault()?.Pair ?? (index > 0 ? visible[index - 1].Pair : null);
        }

        await WriteThenRebuildAsync(file => ConversionPairs.Remove(file, row.Pair), select: next);
    }

    /// <summary>
    /// 足す・外す。先に待っているメモを書き切る（書き切らずに書いた後の値で作り直すと、打ったばかりのメモが古い文の行に戻る）
    /// </summary>
    private async Task<bool> WriteThenRebuildAsync(Func<ConversionPairFile, ConversionPairFile> change, ConversionPair? select)
    {
        await _memo.FlushAsync();
        if (await RunAsync(change) is not { } written)
        {
            return false;
        }

        Rebuild(written, select);
        _written?.Invoke();
        return true;
    }

    /// <summary>右の欄で打った（行から呼ぶ）。</summary>
    internal void MemoEdited(ConversionPairRow row, string text) => _memo.Edited(row, text);

    /// <summary>
    /// 打った時点の行の組へメモを書く。行は作り直さずにその場で組を差し替える（作り直すと選びとメモの欄のフォーカスが飛ぶ）
    /// </summary>
    private async Task SaveMemoAsync(ConversionPairRow row, string text)
    {
        if (await RunAsync(file => ConversionPairs.SetMemo(file, row.Pair, text)) is { } written
            && written.Pairs.FirstOrDefault(pair => pair.SameEnds(row.Pair)) is { } saved)
        {
            row.Pair = saved;
            _memoWritten?.Invoke();
        }
    }

    /// <summary>書く。書けたら書いた後の値、書けなければ null（理由は欄の下の1行）。</summary>
    private async Task<ConversionPairFile?> RunAsync(Func<ConversionPairFile, ConversionPairFile> change)
    {
        Notice.Clear();
        try
        {
            var result = await _services.Commands.ExecuteAsync(new UiCommand.ChangeConversionPairs(change));
            switch (result)
            {
                case CommandResult.ConversionPairsChanged changed:
                    return changed.Pairs;
                case CommandResult.Failed failed:
                    Notice.Warn(failed.Message);
                    break;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Core.Diagnostics.AppLog.Error("変換の組の保存", exception);
            Notice.Warn($"保存できませんでした。{FailureText.Cause(exception)}");
        }

        return null;
    }
}

/// <summary>
/// 「追加した組」の1行（左はプロファイルの商品と同じ形で「元 → 先」と短いメモ）。選ぶと右に、元・先（押して移る）・メモ・「この組を外す」を出す。
/// </summary>
public sealed class ConversionPairRow : ViewModelBase
{
    private readonly ConversionPairEditor _owner;
    private string _memoText;

    public ConversionPairRow(
        ConversionPairEditor owner, ConversionPair pair, string fromName, string toName, int addedIndex,
        Func<ConversionEnd, string, ConversionEndIcon> iconOf)
    {
        _owner = owner;
        Pair = pair;
        FromName = fromName;
        ToName = toName;
        AddedIndex = addedIndex;
        FromIcon = iconOf(pair.From, fromName);
        ToIcon = iconOf(pair.To, toName);
        _memoText = pair.Memo ?? string.Empty;
        RemoveCommand = new RelayCommand(() => owner.RemoveAsync(this).Forget());
        OpenFromCommand = new RelayCommand(() => owner.Open(Pair.From), () => owner.CanOpen(Pair.From));
        OpenToCommand = new RelayCommand(() => owner.Open(Pair.To), () => owner.CanOpen(Pair.To));
    }

    /// <summary>今の組（メモを書いた後はその場で差し替える）。外す・メモを書く時は端で当てるので、メモが古くても同じ組を指す。</summary>
    public ConversionPair Pair { get; set; }

    public string FromName { get; }

    public string ToName { get; }

    /// <summary>元・先のアバターの登録簿の行（探す欄で正式名・呼び方にも当てるため）。共通素体と、登録簿から消えたアバターは null。</summary>
    public AvatarRegistryEntry? FromEntry { get; init; }

    public AvatarRegistryEntry? ToEntry { get; init; }

    /// <summary>一覧の行の型の出し分け（まとまりの見出しと同じ名前の印）。</summary>
    public bool IsGroupHeader => false;

    /// <summary>追加した順の位置（<c>conversion-pairs.json</c> の配列の位置。並びの「追加が新しい順・古い順」に使う）。</summary>
    public int AddedIndex { get; }

    /// <summary>元と先の頭の絵（ユーザ判断 2026-11-20・①）。</summary>
    public ConversionEndIcon FromIcon { get; }

    public ConversionEndIcon ToIcon { get; }

    public string Text => ConversionPairText.Format(FromName, ToName);

    /// <summary>使うツールの名前（ファイルを直して書いた物だけ。画面では書かない）。</summary>
    public string? Tool => Pair.Tool;

    public bool HasTool => !string.IsNullOrWhiteSpace(Tool);

    /// <summary>右の欄のメモ（押さずに残る・I9）。</summary>
    public string MemoText
    {
        get => _memoText;
        set
        {
            if (SetField(ref _memoText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(MemoPreview));
                OnPropertyChanged(nameof(HasMemo));
                _owner.MemoEdited(this, _memoText);
            }
        }
    }

    /// <summary>左の行に添えるメモ（最初の行だけ。長さは行の幅で切る）。</summary>
    public string MemoPreview => _memoText
        .Split('\n')
        .Select(line => line.Trim())
        .FirstOrDefault(line => line.Length > 0) ?? string.Empty;

    public bool HasMemo => MemoPreview.Length > 0;

    /// <summary>右の「この組を外す」。</summary>
    public RelayCommand RemoveCommand { get; }

    /// <summary>右の元の名前。そのアバター・共通素体へ移る（登録簿に無い物は押せない）。</summary>
    public RelayCommand OpenFromCommand { get; }

    public RelayCommand OpenToCommand { get; }
}

/// <summary>組の書き方（「変換」の節とアバターの詳細の札で同じにする）。</summary>
public static class ConversionPairText
{
    public static string BaseText(string baseName) => $"{baseName}（共通素体）";

    public static string Format(string fromName, string toName) => $"{fromName} → {toName}";

    /// <summary>端の名前。登録簿から消えたアバターは ID のまま出す（何を指していたか分かるように）。</summary>
    public static string NameOf(ConversionEnd end, IReadOnlyDictionary<string, string> names)
        => end.BaseName is { } baseName ? BaseText(baseName)
        : end.AvatarItemId is { } id ? names.TryGetValue(id, out var name) ? name : id
        : string.Empty;
}

/// <summary>
/// アバターの詳細の「変換の組」（読むだけ・ユーザ判断 2026-11-20）。そのアバターが元か先にある組を札で並べ、
/// 「変換で編集」で「変換」へ移る。所属する共通素体が端の組は出さない（このアバターを直に指す組だけ）
/// </summary>
public sealed class ConversionPairSummary : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private string? _avatarItemId;

    public ConversionPairSummary(AppServiceContainer services, Action goToConversion)
    {
        _services = services;
        EditCommand = new RelayCommand(goToConversion);
    }

    public IReadOnlyList<ConversionPairChip> Chips { get; private set; } = [];

    public bool HasChips => Chips.Count > 0;

    /// <summary>「変換で編集」。アバターの管理の「変換」へ移る。</summary>
    public RelayCommand EditCommand { get; }

    /// <summary>見せるアバターを替える（一覧で選び直したとき）。null なら空にする。</summary>
    public void ShowFor(string? avatarItemId)
    {
        _avatarItemId = avatarItemId;
        Refresh();
    }

    /// <summary>組を書いた後・登録簿を読み直した後に読み直す。</summary>
    public void Refresh()
    {
        var self = _avatarItemId is { } id ? ConversionEnd.Avatar(id) : null;
        if (self is null)
        {
            Chips = [];
        }
        else
        {
            var names = AvatarNames.Map(_services.CachedAvatars.Load().Entries);
            Chips = [.. _services.Store.ConversionPairs.Load().Pairs
                .Where(pair => !pair.IsEmpty && (pair.From.Same(self) || pair.To.Same(self)))
                .Select(pair => new ConversionPairChip(
                    ConversionPairText.Format(ConversionPairText.NameOf(pair.From, names), ConversionPairText.NameOf(pair.To, names)),
                    pair.Memo))];
        }

        OnPropertyChanged(nameof(Chips));
        OnPropertyChanged(nameof(HasChips));
    }
}

/// <summary>アバターの詳細の組の札1枚（元 → 先。メモは吹き出し）。</summary>
public sealed record ConversionPairChip(string Text, string? Memo)
{
    public bool HasMemo => !string.IsNullOrWhiteSpace(Memo);
}
