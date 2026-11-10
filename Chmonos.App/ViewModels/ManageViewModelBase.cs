using System.Collections.ObjectModel;
using System.Windows;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 分類の管理（タグの管理の大分類・属性の管理）の左の一覧の1行。名前・メモ・件数・選んでいるかは2画面で同じ。
/// 件数の言い方（「未使用」「未評価」）と、画面だけが持つ値（小分類の数・平均・既定）は各画面の行に残す
/// </summary>
public abstract class ManageMasterRow : ReorderableRow
{
    private bool _isSelected;

    public required string Name { get; init; }

    /// <summary>保存したら書き換える。読み込み時の値のままだと、選び直したときに古いメモが出る。</summary>
    public string? Memo { get; set; }

    public required int ItemCount { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}

/// <summary>
/// 分類の管理の土台。タグの管理と属性の管理は、左の一覧（検索と追加を1本にした欄・並べ方・ドラッグ）・右の頭（名前・メモ・件数）・
/// 一覧に無い名前の直し・中の商品の段（カード／リスト）を同じ作りで持つ（2画面の操作感は揃える。`docs/spec/tags.md`）。
/// 前は2つの ViewModel に同じ名前の約70個を写していて、片方だけ直すと食い違った（R3 2026-11-10。ユーザ判断「まとめてしまいましょう」）。
///
/// 違う所だけを各画面が持つ：タグは大分類・小分類の2段（小分類の行・開いた小分類の中身・移す・入れる）、
/// 属性は値 0〜100（値の順の並び・最初から並べる・統合で残す値を聞く窓）。
/// 一覧に無い名前の3操作・読み直し・名前の変更と削除の本体は、呼ぶ命令も確認の文も違うので各画面に残す
/// </summary>
/// <typeparam name="TRow">左の一覧の行。</typeparam>
/// <typeparam name="TOrphan">一覧に無いのに商品が参照している名前の行。</typeparam>
public abstract class ManageViewModelBase<TRow, TOrphan> : ViewModelBase, IPendingWrites, IItemCardHost, IItemImagesListener
    where TRow : ManageMasterRow
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;
    private readonly string _itemViewKey;
    private readonly string _listPaneKey;

    private TRow? _selected;
    private string _filterText = string.Empty;
    private string _memoDraft = string.Empty;
    private string _itemFilter = string.Empty;
    private bool _rebuildingList;

    /// <param name="itemViewKey">カードかリストかを覚える鍵（<see cref="ManageItemView"/>）。</param>
    /// <param name="listPaneKey">左の一覧の幅を覚える鍵（<see cref="PaneColumn"/>）。</param>
    /// <param name="sort">開いたときの並べ方（`ui-state.json` から各画面が読む。知らない値のときの既定が画面で違う）。</param>
    /// <remarks>
    /// 読み込み（<see cref="ReloadAsync"/>）と段の組み直しは、ここでは始めない。派生の本体で作る物（タグの小分類のメモの待ち）が
    /// まだ無いうちに、最初の await までが同期で走ってしまう。派生のコンストラクタの最後で始める
    /// </remarks>
    protected ManageViewModelBase(AppServiceContainer services, MainViewModel main, string itemViewKey, string listPaneKey, TagSortMode sort)
    {
        _services = services;
        _main = main;
        _itemViewKey = itemViewKey;
        _listPaneKey = listPaneKey;
        _sort = sort;

        AddCommand = new RelayCommand(() => AddAsync(FilterText).Forget());
        SubmitFilterCommand = new RelayCommand(() => SubmitFilterAsync().Forget());
        AskRenameCommand = new RelayCommand(() => AskRenameAsync().Forget(), () => Selected is not null);
        DeleteCommand = new RelayCommand(() => DeleteSelectedAsync().Forget(), () => Selected is not null);
        RefreshCommand = new RelayCommand(() => ReloadAsync().Forget());
        ShowItemsCommand = new RelayCommand(
            () => ShowItemsOf(Selected!.Name),
            () => Selected is { ItemCount: > 0 });

        // メモは押さずに残す（ユーザ指示 2026-09-18。ショップ・アバターと同じ）
        MemoSaver = new MemoAutoSave<TRow>(SaveMemoAsync);
    }

    // ---- 画面ごとに違う物 ----

    /// <summary>左の一覧の物の呼び名（「大分類」「属性」）。追加の知らせ・名前を変える窓・一覧の見出しに入る。</summary>
    protected abstract string KindName { get; }

    /// <summary>
    /// 最後に選んだ名前。画面は開くたびに作り直すので、各画面の型の静的な値に置く（商品ページから戻ると先頭に戻ってしまう）。
    /// 2画面で混ざらないよう、土台には置かない
    /// </summary>
    protected abstract string? LastSelectedName { get; set; }

    /// <summary>右の平らな一覧を組み直す。タグはまとめて後で（小分類の数だけ知らせが続く）、属性はその場で。</summary>
    protected abstract void RequestLines();

    public abstract Task ReloadAsync();

    public abstract string SelectedUsageText { get; }

    public abstract string ShowItemsToolTip { get; }

    public abstract string EmptyText { get; }

    public abstract string ItemFilterResultText { get; }

    /// <summary>選ぶ物が替わった後の、画面ごとの作り直し（右の中身・押せるか）。</summary>
    protected abstract void OnSelectionChanged();

    /// <summary>別の物へ移ったときに消す、画面ごとの知らせ（名前の近くの知らせは土台が消す）。</summary>
    protected abstract void ClearNoticesOfPrevious();

    /// <summary>右の中を探す欄が変わった。</summary>
    protected abstract void OnItemFilterChanged();

    /// <summary>検索画面の事実が変わり、右の中を探す欄の式がそれを見ている。</summary>
    protected abstract void RefilterItemsForSearchFacts();

    /// <summary>
    /// 左の欄の語で、行が当たるか。当たらなければ null、名前で当たれば空、名前以外なら何で当たったか（名前の下に出す）。
    /// 何を見るかが画面で違う（タグは小分類の名前とメモ、属性は付けた商品の名前。`docs/spec/tags.md`）
    /// </summary>
    protected abstract Func<TRow, string?> CreateMatcher(ItemTextFilter filter);

    /// <summary>左の一覧を絞り直した直後（「選択なし」を受けない間）に、画面ごとに印を付け直す。</summary>
    protected virtual void OnListFiltered()
    {
    }

    /// <summary>名前順・件数順を選んだときに、保存先の並びを書き換える（手で並べた順のときは呼ばない）。</summary>
    protected abstract Task ReorderBySortAsync();

    /// <summary>並べ方を `ui-state.json` に覚える（タグと属性で別の値）。</summary>
    protected abstract void SaveSort(string saved);

    protected abstract UiCommand AddCommandFor(string name);

    protected abstract UiCommand MemoCommandFor(TRow row, string memo);

    protected abstract UiCommand ReorderCommandFor(IReadOnlyList<string> order);

    /// <summary>名前の変更・削除が投げたときにログに書く見出し。</summary>
    protected abstract string RewriteLogLabel { get; }

    /// <summary>名前を変える。既にある名前を指すと統合になる（確認の仕方が画面で違う）。</summary>
    internal abstract Task RenameAsync(string? newName);

    protected abstract Task DeleteSelectedAsync();

    /// <summary>この名前が付いている商品を検索で開く。</summary>
    protected abstract void ShowItemsOf(string name);

    // ---- カードとリスト・左の一覧の幅 ----

    private ManageItemView? _itemView;

    /// <summary>商品をカードで出すかリストで出すか（M4）。ほかの画面と同じ切り替え。</summary>
    private ManageItemView ItemView => _itemView ??= new ManageItemView(_services, _itemViewKey, () =>
    {
        OnPropertyChanged(nameof(IsCardMode));
        OnPropertyChanged(nameof(IsListMode));
        RequestLines();
    });

    public bool IsCardMode => ItemView.IsCardMode;

    public bool IsListMode => ItemView.IsListMode;

    private RelayCommand? _showCards;
    private RelayCommand? _showList;

    public RelayCommand ShowCardsCommand => _showCards ??= new RelayCommand(() => ItemView.Set(false));

    public RelayCommand ShowListCommand => _showList ??= new RelayCommand(() => ItemView.Set(true));

    private PaneColumn? _listPane;

    /// <summary>左の一覧の列。ドラッグで幅を変えられる（ユーザ判断 2026-09-14）。</summary>
    public PaneColumn ListPane => _listPane ??= new PaneColumn(_services.PaneWidths, _listPaneKey);

    /// <summary>戻る（ユーザ判断 2026-09-20・V2・V3）。**ナビから入っても直前の画面へ戻れる。**</summary>
    public MainViewModel Main => _main;

    // ---- 右の平らな一覧 ----

    /// <summary>
    /// 右側に並べる平らな一覧（仮想化の単位）。先頭はこの画面そのもの（頭）、続いて中の商品の段、最後に下の枠（<see cref="ManageFoot"/>）。
    /// 何を間に挟むかは各画面が組む（タグは小分類、属性は商品の段だけ）
    /// </summary>
    public ObservableCollection<object> Lines { get; } = [];

    private ManageFoot? _foot;

    protected ManageFoot Foot => _foot ??= new ManageFoot(this);

    /// <summary>中の商品を並べられる幅。0 はまだ測っていない（段に切れないので、段を並べない）。</summary>
    protected double ItemsWidth { get; private set; }

    /// <summary>中の商品を並べられる幅を受け取る（View が一覧の幅から枠の分を引いて渡す）。段の数が変わるときだけ切り直す。</summary>
    public void SetItemsWidth(double width)
    {
        if (Math.Abs(width - ItemsWidth) < 0.5)
        {
            return;
        }

        var before = (Cards: ManageItemLayout.ColumnsFor(ItemsWidth, Services.CardMetrics.SlotWidth), List: ManageItemLayout.ColumnsFor(ItemsWidth, ManageItemLayout.ListSlotWidth));
        var measured = ItemsWidth > 0;
        ItemsWidth = width;
        var after = (Cards: ManageItemLayout.ColumnsFor(width, Services.CardMetrics.SlotWidth), List: ManageItemLayout.ColumnsFor(width, ManageItemLayout.ListSlotWidth));
        if (!measured || before != after)
        {
            RequestLines();
        }
    }

    // ---- 並べ方 ----

    /// <summary>`ui-state.json` の値を並べ方に読む。知らない値は画面ごとの既定（タグは名前順、属性は手で並べた順）。</summary>
    protected static TagSortMode ParseSort(string? saved, TagSortMode fallback) => saved switch
    {
        "name" => TagSortMode.Name,
        "count" => TagSortMode.Count,
        "manual" => TagSortMode.Manual,
        _ => fallback,
    };

    private TagSortMode _sort;

    /// <summary>
    /// 並べ方（ユーザ指示 2026-09-18・2026-09-19：2画面で揃える）。選ぶと保存先の並びも同じ順に書き換える——
    /// 検索と編集の候補もこの並びをそのまま使うので、画面だけ並べ替えると「画面と候補で順番が違う」状態になる
    /// </summary>
    public TagSortMode Sort
    {
        get => _sort;
        set
        {
            if (!SetField(ref _sort, value))
            {
                return;
            }

            SaveSort(value switch
            {
                TagSortMode.Name => "name",
                TagSortMode.Count => "count",
                _ => "manual",
            });
            ApplySortAsync().Forget();
        }
    }

    public bool SortsByName
    {
        get => Sort == TagSortMode.Name;
        set { if (value) { Sort = TagSortMode.Name; } }
    }

    public bool SortsByCount
    {
        get => Sort == TagSortMode.Count;
        set { if (value) { Sort = TagSortMode.Count; } }
    }

    public bool SortsManually
    {
        get => Sort == TagSortMode.Manual;
        set { if (value) { Sort = TagSortMode.Manual; } }
    }

    /// <summary>
    /// 今の並べ方で保存先を並べ替える。手で並べた順のときは何もしない
    /// （人が置いた順がその並びそのものなので、触ると意味が変わる）
    /// </summary>
    private async Task ApplySortAsync()
    {
        foreach (var name in new[] { nameof(SortsByName), nameof(SortsByCount), nameof(SortsManually) })
        {
            OnPropertyChanged(name);
        }

        if (Sort == TagSortMode.Manual)
        {
            return;
        }

        await ReorderBySortAsync();
        await ReloadAsync();
        _main.RefreshMasters();
    }

    /// <summary>今の並べ方（名前順か件数順）での名前の並び。件数が同じ物は名前順。</summary>
    protected IReadOnlyList<string> SortNames(IEnumerable<(string Name, int Count)> entries) => Sort switch
    {
        TagSortMode.Count => entries
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCulture)
            .Select(entry => entry.Name)
            .ToList(),
        _ => entries
            .OrderBy(entry => entry.Name, StringComparer.CurrentCulture)
            .Select(entry => entry.Name)
            .ToList(),
    };

    /// <summary>
    /// ドラッグで置き換える。並びは検索の候補にも編集の候補にもそのまま出るので、「よく使う順」に置けること自体が機能になる。
    /// **「候補の並べ替え」のときだけ**動かす（メモ10-⑤ 2026-10-02）。名前順・件数順のままだと次の読み直しで元に戻り
    /// 「動かなかった」ように見えるが、並べ方を勝手に切り替えるのも望まれなかったので、つかみを出さず・落としても動かさない
    /// </summary>
    public async Task MoveAsync(TRow moved, TRow target, bool after)
    {
        if (!SortsManually)
        {
            return;
        }

        var order = AllRows.Select(row => row.Name).ToList();
        if (!Reorder(order, moved.Name, target.Name, after))
        {
            return;
        }

        await _services.Commands.ExecuteAsync(ReorderCommandFor(order));
        await ReloadAsync();

        // 並びは検索の絞り込みにもそのまま出るので、そちらも作り直す
        _main.RefreshMasters();
    }

    /// <summary>抜いてから差し込む。落とす先の index は抜いた後で数え直す。</summary>
    protected static bool Reorder(List<string> order, string moved, string target, bool after)
    {
        var from = order.IndexOf(moved);
        if (from < 0 || moved == target)
        {
            return false;
        }

        order.RemoveAt(from);

        var at = order.IndexOf(target);
        if (at < 0)
        {
            order.Insert(from, moved);
            return false;
        }

        var to = after ? at + 1 : at;
        if (to == from)
        {
            order.Insert(from, moved);
            return false;
        }

        order.Insert(to, moved);
        return true;
    }

    // ---- 左の一覧 ----

    /// <summary>読み込んだ全部の行（左の欄で絞る前）。</summary>
    protected List<TRow> AllRows { get; private set; } = [];

    /// <summary>左の一覧に出している行（各画面が `Tops`・`Rows` の名前で結ぶ）。</summary>
    protected ObservableCollection<TRow> VisibleRows { get; } = [];

    public ObservableCollection<TOrphan> Orphans { get; } = [];

    /// <summary>改名の寄せ先候補。既存を選べば統合、無い語を入れれば単なる改名になる。</summary>
    public ObservableCollection<string> OtherNames { get; } = [];

    /// <summary>
    /// 欄の右の「追加」。打った名前で足す。
    /// </summary>
    public RelayCommand AddCommand { get; }

    /// <summary>
    /// 欄で Enter。打った名前が無ければ足し、あればそれを選ぶ。
    /// 左の欄は検索と追加の1本（ユーザ指示 2026-10-02）。候補は並べず、足せるときだけ欄の下に「追加」の1行を出す
    /// </summary>
    public RelayCommand SubmitFilterCommand { get; }

    /// <summary>名前を変える窓を出す（2画面で同じ窓。既にある名前を選ぶと統合）。</summary>
    public RelayCommand AskRenameCommand { get; }

    public RelayCommand DeleteCommand { get; }

    public RelayCommand RefreshCommand { get; }

    /// <summary>この分類が付いている商品を検索で見せる。消す・統合するの判断は中身を見ないとできない。</summary>
    public RelayCommand ShowItemsCommand { get; }

    public TRow? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value || (_rebuildingList && value is null))
            {
                return;
            }

            // 待っているメモは、移る前に今の対象へ書き切る。移ってからだと、移った先の欄の文と名前で書いてしまう
            FlushMemos();

            if (_selected is not null)
            {
                _selected.IsSelected = false;
            }

            var previousName = _selected?.Name;
            _selected = value;

            // 読み直しで同じ名前の行に選び直しただけなら、今出している知らせは残す（名前の変更・切り替えの結果が消えてしまう）。
            // 別の物へ移ったときは、前の物の話なので消す
            if (!string.Equals(previousName, value?.Name, StringComparison.CurrentCultureIgnoreCase))
            {
                NameNotice.Clear();
                ClearNoticesOfPrevious();
            }

            if (_selected is not null)
            {
                _selected.IsSelected = true;
                LastSelectedName = _selected.Name;
            }

            // 選び直しで欄を入れ替えるときは、自動保存を走らせない
            MemoSaver.Replace(() => MemoDraft = _selected?.Memo ?? string.Empty);

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedName));
            OnPropertyChanged(nameof(SelectedUsageText));
            OnPropertyChanged(nameof(SelectedIsUsed));
            OnPropertyChanged(nameof(ShowItemsToolTip));
            OnSelectionChanged();
            RebuildOtherNames();
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelection => Selected is not null;

    public bool SelectedIsUsed => Selected is { ItemCount: > 0 };

    public string SelectedName => Selected?.Name ?? string.Empty;

    /// <summary>
    /// 検索画面の事実が変わった（<see cref="MainViewModel.NoteSearchFactsChanged"/>）。検索欄の式が事実を見るときだけ絞り直す——
    /// 見ない式で作り直すと、打っていないのに一覧が動く
    /// </summary>
    /// <remarks>左と右の欄は別々に見る（点検29：左が事実を見ると、右の絞り直しが飛ばされていた）。</remarks>
    internal void RefilterForSearchFacts()
    {
        if (DependsOnFacts(_filterText))
        {
            RebuildList();
        }

        if (DependsOnFacts(_itemFilter))
        {
            RefilterItemsForSearchFacts();
            OnPropertyChanged(nameof(ItemFilterResultText));
        }
    }

    private static bool DependsOnFacts(string text)
        => !string.IsNullOrWhiteSpace(text) && SearchQuery.DependsOnFacts(SearchQuery.Parse(text));

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value ?? string.Empty))
            {
                // 打ち直したら前の「既にあります」は古い（消えるだけで、欄の下の場所は空けたまま）
                AddNoticeText = string.Empty;
                RebuildList();
            }
        }
    }

    /// <summary>
    /// 欄に打った名前の行が既にあれば、その行。空白と大文字小文字は問わない（足すときの「既にあります」と同じ照らし方）
    /// </summary>
    private TRow? TypedExisting
    {
        get
        {
            var typed = NameText.Normalize(_filterText);
            return typed.Length == 0
                ? null
                : AllRows.FirstOrDefault(row => string.Equals(row.Name, typed, StringComparison.CurrentCultureIgnoreCase));
        }
    }

    private string _addNoticeText = string.Empty;
    private bool _addNoticeIsWarning = true;

    /// <summary>
    /// 欄のすぐ下の1行。名前が空・長すぎる・既にある（打ち直しが要る）と、足せた（済んだこと）を言う。
    /// 欄の側が1行分の場所を常に取ってあるので、出ても消えても下の一覧は動かない
    /// </summary>
    public string AddNoticeText
    {
        get => _addNoticeText;
        private set => SetField(ref _addNoticeText, value);
    }

    /// <summary>欄の下の知らせが、打ち直しや別の操作の要る物か（要る物は警告の色、済んだことはふつうの色）。</summary>
    public bool AddNoticeIsWarning
    {
        get => _addNoticeIsWarning;
        private set => SetField(ref _addNoticeIsWarning, value);
    }

    private void SayAdd(string text, bool warning)
    {
        AddNoticeIsWarning = warning;
        AddNoticeText = text;
    }

    // ---- メモ ----

    /// <summary>選んでいる行のメモの自動保存（書く先と文は打った時点で決める）。</summary>
    protected MemoAutoSave<TRow> MemoSaver { get; }

    public string MemoDraft
    {
        get => _memoDraft;
        set
        {
            if (SetField(ref _memoDraft, value))
            {
                OnPropertyChanged(nameof(MemoChanged));

                // 押さずに残す（ユーザ指示 2026-09-18）。選び直しで欄を入れ替えたときは書かない
                MemoSaver.Edited(Selected, value);
            }
        }
    }

    public bool MemoChanged => Selected is not null && MemoDraft != (Selected.Memo ?? string.Empty);

    /// <summary>選び直す前に、待っているメモを今の対象へ書き切る（タグは小分類のメモも）。</summary>
    protected virtual void FlushMemos() => MemoSaver.Flush();

    /// <summary>待っているメモを今書く（画面を離れる前・閉じる前）。</summary>
    public virtual Task FlushPendingWritesAsync() => MemoSaver.FlushAsync();

    /// <summary>打った時点の行へ書く（<see cref="MemoAutoSave{TOwner}"/>）。</summary>
    private async Task SaveMemoAsync(TRow row, string memo)
    {
        // 読み直すと打っている途中の欄が戻るので、ここでは読み直さない（ショップ・アバターと同じ）。
        // 代わりに行の値を書き換えて、選び直したときに古いメモが出ないようにする
        row.Memo = memo;
        await _services.Commands.ExecuteAsync(MemoCommandFor(row, memo));
    }

    public bool HasOrphans => Orphans.Count > 0;

    /// <summary>
    /// 左の一覧が空のとき、真っ白にせず次にやることを書く（`ui-rules.md`・E1）。
    /// **探して0件と、そもそも1つも無いのを言い分ける**（検索・フォルダ・改変と同じ書き分け）。
    /// </summary>
    public bool IsEmpty => VisibleRows.Count == 0;

    public string HeaderText => $"{KindName} {AllRows.Count} 件";

    /// <summary>
    /// 操作の結果の知らせは、押した所の近くに出す（`notice-placement-2026-10-03.md`）。右の欄の先頭の1行にまとめて出していたのをやめた。
    /// 欄の下（足す欄の誤りと「既にあります」）は <see cref="AddNoticeText"/>、名前の近くは <see cref="NameNotice"/>、
    /// 一覧の見出しの近く（行ごと消える操作と、一覧に無い名前の直し）は <see cref="ListNotice"/>。ほかは各画面が持つ。
    /// メモの自動保存の成功は出さない（欄が残るので足りる。設定と同じ）
    /// </summary>
    public AreaNotice NameNotice { get; } = new();

    public AreaNotice ListNotice { get; } = new();

    /// <summary>
    /// 読み込んだ行と一覧に無い名前を出し、選び直す。改名した直後は名前が変わっているので、無ければ先頭に落とす。
    /// 開き直したとき（選んでいる物が無いとき）は、前に見ていた物に戻る
    /// </summary>
    /// <param name="orphanRows">一覧に無い名前の行を作る。行が全部の行を引く（寄せ先の候補）ので、行を入れ替えた後に呼ぶ。</param>
    protected void ShowLoaded(List<TRow> rows, Func<IEnumerable<TOrphan>> orphanRows)
    {
        var keep = Selected?.Name;
        AllRows = rows;

        Orphans.Clear();
        foreach (var orphan in orphanRows())
        {
            Orphans.Add(orphan);
        }

        RebuildList();
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(HasOrphans));

        Selected = AllRows.FirstOrDefault(row => row.Name == (keep ?? LastSelectedName)) ?? VisibleRows.FirstOrDefault();
    }

    /// <summary>
    /// 左の一覧を絞る。名前以外で当たった行には、何で当たったかを名前の下に出す（<see cref="ReorderableRow.MatchReason"/>）。
    /// 書き方は検索画面と同じ（ユーザ指示 2026-09-19）
    /// </summary>
    protected void RebuildList()
    {
        var filter = ItemTextFilter.Create(_filterText, _main.Search.CreateSearchFacts());
        var matches = filter is null ? null : CreateMatcher(filter);

        // 作り直す間は、一覧が書き戻す「選択なし」を受けない。受けると、探すたびに右が空になっていた。
        // 当たりから外れても右は今見ている物のまま残す（見ている物が勝手に消えると、何を探していたか分からなくなる）
        _rebuildingList = true;
        try
        {
            VisibleRows.Clear();
            foreach (var row in AllRows)
            {
                row.MatchReason = string.Empty;
                if (matches is null)
                {
                    VisibleRows.Add(row);
                    continue;
                }

                if (matches(row) is { } reason)
                {
                    row.MatchReason = reason;
                    VisibleRows.Add(row);
                }
            }

            OnListFiltered();
        }
        finally
        {
            _rebuildingList = false;
        }

        var typedExisting = TypedExisting;
        foreach (var row in AllRows)
        {
            row.IsNameMatch = ReferenceEquals(row, typedExisting);
        }

        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasFilter));
        OnPropertyChanged(nameof(FilterResultText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    /// <summary>絞り込んでいるか。</summary>
    public bool HasFilter => _filterText.Trim().Length > 0;

    public string FilterResultText => HasFilter
        ? $"「{_filterText.Trim()}」に当たる{KindName} {VisibleRows.Count} 件"
        : string.Empty;

    private void RebuildOtherNames()
    {
        OtherNames.Clear();
        foreach (var name in AllRows
            .Where(row => Selected is null || row.Name != Selected.Name)
            .Select(row => row.Name))
        {
            OtherNames.Add(name);
        }
    }

    private async Task SubmitFilterAsync()
    {
        var existing = TypedExisting;
        if (existing is not null)
        {
            Selected = existing;
            return;
        }

        await AddAsync(FilterText);
    }

    private async Task AddAsync(string? name)
    {
        // 改行やタブは空白に寄せて1行にする（I13）
        var trimmed = NameText.Normalize(name);

        // 空のまま押したときに黙って終わらない（I1）。押した人は「やった」と思っている
        if (trimmed.Length == 0)
        {
            SayAdd($"{KindName}の名前を入れてから押してください。", warning: true);
            return;
        }

        // 長すぎるものは**切らずに断る**（切ると打った名前と食い違う・I13）
        if (NameText.IsTooLong(trimmed))
        {
            SayAdd(NameText.TooLongMessage($"{KindName}の名前"), warning: true);
            return;
        }

        // 既にある名前は足されない。**足していないのに「追加しました」と言わない**（I2）
        if (AllRows.Any(row => string.Equals(row.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)))
        {
            SayAdd($"「{trimmed}」は既にあります。", warning: true);
            Selected = AllRows.FirstOrDefault(row =>
                string.Equals(row.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)) ?? Selected;
            return;
        }

        await _services.Commands.ExecuteAsync(AddCommandFor(trimmed));
        // 足せたら欄を空ける（続けて足せるように）。空にすると絞り込みも外れ、足した物が一覧に並ぶ
        FilterText = string.Empty;
        await ReloadAsync();
        _main.RefreshMasters();
        Selected = AllRows.FirstOrDefault(row =>
            string.Equals(row.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)) ?? Selected;

        // 欄を空けると打ち直しの扱いで知らせが消えるので、空けた後に出す
        SayAdd($"「{trimmed}」を追加しました。", warning: false);
    }

    /// <summary>名前を変える窓を出してから実行する。既にある名前を選ぶと統合になる。</summary>
    private async Task AskRenameAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var others = AllRows.Where(row => row.Name != Selected.Name).Select(row => row.Name).ToList();
        var model = new RenameTagDialogViewModel(KindName, Selected.Name, Selected.ItemCount, others);
        if (new Views.RenameTagDialog(model).ShowDialog() != true)
        {
            return;
        }

        await RenameAsync(model.Target);
    }

    /// <summary>
    /// 改名・削除の命令を送り、できなかったら呼び手の渡した出し先（<paramref name="failureNotice"/>）に出す。
    /// 前は画面の1行に一律で出していたので、押した所から離れた所に失敗が出ていた。
    ///
    /// 命令は書けなかった例外（ファイルを掴まれた・ドライブが外れた）をそのまま投げ、入口は Forget() でログに残すだけなので、
    /// 前は押しても何も起きなかったように見えた。「できなかった」の結果も受けずに捨てていた。
    /// 例外でも読み直しは続ける——途中まで書き換えた商品があり得るので、今の数を見せる
    /// </summary>
    protected async Task<CommandResult?> RewriteAsync(UiCommand command, string failedText, AreaNotice failureNotice)
    {
        try
        {
            var result = await _services.Commands.ExecuteAsync(command);
            if (result is CommandResult.Failed failed)
            {
                failureNotice.Warn(failed.Message);
            }

            return result;
        }
        catch (Exception exception)
        {
            Core.Diagnostics.AppLog.Error(RewriteLogLabel, exception);
            failureNotice.Warn(failedText + Core.Services.FailureText.Cause(exception));
            return null;
        }
    }

    /// <summary>
    /// 人が押した操作の確認（アイコンは Question で揃える。`ui-rules.md`・D9）。
    /// 既定はキャンセル。Enterを押しただけで消えないようにする。
    /// </summary>
    protected static bool Confirm(string message, string caption)
        => Services.Notice.Show(
            message,
            caption,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;

    // ---- 右の中を探す欄 ----

    /// <summary>右の中の商品を探す。書き方は検索画面と同じ（ユーザ指示 2026-09-19）。何を絞るかは各画面。</summary>
    public string ItemFilter
    {
        get => _itemFilter;
        set
        {
            if (SetField(ref _itemFilter, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasItemFilter));
                OnItemFilterChanged();
            }
        }
    }

    public bool HasItemFilter => _itemFilter.Trim().Length > 0;

    // ---- 中の商品の行 ----

    /// <summary>作った商品の行。画像が届いたときに引き直す先（弱く持つ。探す欄は1文字ごとに行を作り直すので、強く持つと溜まり続ける）。</summary>
    private readonly ItemRowRegistry<TagItemRow> _itemRows = new(row => row.ItemId);

    /// <summary>
    /// 中に出す商品1件。絵の引き方は改変の一覧と同じものを使う（場所は呼び手が引いておく）。
    /// <paramref name="valueText"/> は名前の右の札（属性の「85 %」）。タグでは出さない
    /// </summary>
    protected TagItemRow CreateItemRow(ItemRecord item, string? thumbnailPath, string? valueText = null)
    {
        var entry = new TagItemRow
        {
            ItemId = item.Id,
            Name = item.DisplayName,
            ShopName = item.Booth.Shop?.Name ?? string.Empty,
            ValueText = valueText,
            ThumbnailPath = thumbnailPath,
            Thumbnails = _main.Thumbnails,
            CardFactory = () => _main.Search.CardFor(item.Id),
        };

        _itemRows.Add(entry);
        entry.OpenCommand = new RelayCommand(() => _main.ShowItem(item));
        return entry;
    }

    /// <summary>商品の1枚目の場所を、画面のスレッドで引く。</summary>
    protected string? ThumbnailPathOf(ItemRecord item)
        => new ModificationRowBuilder(_services, _main.Thumbnails, new Dictionary<string, ItemRecord>()).ItemThumbnailPath(item);

    void IItemImagesListener.NoteItemImagesSaved(string itemId)
    {
        var rows = _itemRows.Find(itemId);
        if (rows.Count == 0)
        {
            return;
        }

        var path = _main.Search.FindItem(itemId) is { } item ? ThumbnailPathOf(item) : null;
        OnThumbnailPathChanged(itemId, path);
        foreach (var row in rows)
        {
            row.RefreshImages(path);
        }
    }

    /// <summary>裏の取得が商品の画像を置き、1枚目の場所を引き直した（場所を覚えている画面が書き換える）。</summary>
    protected virtual void OnThumbnailPathChanged(string itemId, string? path)
    {
    }

    // ---- カードの操作（検索画面と同じ・IItemCardHost） ----
    // 中の商品のカードは、枠の Tag にこの画面が入る。受け先が無いと、右クリックのメニューは出るのに押しても何も起きなかった。
    // 中身は検索画面の物をそのまま借りる（ショップ・フォルダビュー・改変と同じ形）

    public void OpenItem(ItemCardViewModel card) => _main.ShowItem(card.Item);

    public void OpenBooth(ItemCardViewModel? card) => _main.Search.OpenBooth(card);

    public Task ToggleFavoriteAsync(ItemCardViewModel card) => _main.Search.ToggleFavoriteAsync(card);

    /// <summary>
    /// 右クリックのメニューの命令。「改変に追加…」は選びがこの画面に無いので押した1件だけ（検索の命令のまま）。
    /// 非表示にするだけ差し替える：書くのは検索画面と同じ命令で、検索の写しを読み直し終えてからこの画面を組み直す
    /// （中の商品は検索の写しから引くので、先に組み直すと古い商品のまま残る）
    /// </summary>
    public CardMenuCommands MenuCommands => _menuCommands ??= CardMenuCommands.BorrowedFrom(_main.Search) with
    {
        HideItemCommand = new RelayCommand(parameter => HideItemAsync(parameter as ItemCardViewModel).Forget()),
    };

    private CardMenuCommands? _menuCommands;

    private async Task HideItemAsync(ItemCardViewModel? card)
    {
        await _main.Search.HideItemAsync(card);
        await ReloadAsync();
    }
}
