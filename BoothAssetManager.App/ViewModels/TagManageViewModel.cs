using System.Collections.ObjectModel;
using System.Windows;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 並べ替え中に、どこへ落ちるかを示す線。行の上か下かだけを持つ。
/// Adornerを使わないのは、行のテンプレートに1本足すだけで済むため。
/// </summary>
/// <summary>タグの管理の並べ方（ユーザ指示 2026-09-18）。</summary>
public enum TagSortMode
{
    /// <summary>名前順（既定）。</summary>
    Name,

    /// <summary>付いている商品の多い順。</summary>
    Count,

    /// <summary>手で並べた順（ドラッグで置いた並び＝ファイルの並びそのもの）。</summary>
    Manual,
}

public abstract class ReorderableRow : ViewModelBase
{
    private bool _dropBefore;
    private bool _dropAfter;

    public bool DropBefore
    {
        get => _dropBefore;
        set => SetField(ref _dropBefore, value);
    }

    public bool DropAfter
    {
        get => _dropAfter;
        set => SetField(ref _dropAfter, value);
    }

    public void ClearDropIndicator()
    {
        DropBefore = false;
        DropAfter = false;
    }

    /// <summary>
    /// 一緒に並べ替えられる相手。既定は自分と同じ型（タグのトップとサブは別の並びなので、混ざらない）。
    /// 型が違っても1つの並びに混ざるもの（検索の絞り込みの条件）は、これを揃える。
    /// </summary>
    public virtual object ReorderGroup => GetType();
}

/// <summary>トップレベル1件。件数を出すのは、消す前に影響が見えるようにするため。</summary>
public sealed class TagTopRow : ReorderableRow
{
    private bool _isSelected;

    public required string Name { get; init; }

    /// <summary>保存したら書き換える。読み込み時の値のままだと、選び直したときに古いメモが出る。</summary>
    public string? Memo { get; set; }

    public required int SubCount { get; init; }

    public required int ItemCount { get; init; }

    // 画面の言葉は「大分類／小分類」に揃える（ユーザ指示 2026-09-19：「サブ」「トップ」と混ざっていた）
    public string SubCountText => SubCount == 0 ? "小分類なし" : $"小分類 {SubCount} 件";

    public string ItemCountText => ItemCount == 0 ? "未使用" : $"{ItemCount}";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}

/// <summary>サブレベル1件。</summary>
public sealed class TagSubRow : ReorderableRow
{
    public required string Name { get; init; }

    public required string Top { get; init; }

    /// <summary>保存したら書き換える（次に保存するとき、変わった行だけを書くため）。</summary>
    public string? Memo { get; set; }

    public required int ItemCount { get; init; }

    private string _memoDraft = string.Empty;
    private bool _isExpanded;

    /// <summary>
    /// メモの入力欄（ユーザ指示 2026-09-18）。以前は「メモなし」と書いてあるだけで、
    /// **書く場所がどこにも無かった**。何を書くかは、空のときに欄の中で言う
    /// </summary>
    public string MemoDraft
    {
        get => _memoDraft;
        set
        {
            if (SetField(ref _memoDraft, value ?? string.Empty))
            {
                MemoEdited?.Invoke(this);
            }
        }
    }

    public event Action<TagSubRow>? MemoEdited;

    /// <summary>
    /// 中の商品を見られるように畳む（ユーザ指示 2026-09-18：改変とitemの関係と同じ形）。
    /// 件数だけでは、何が入っているのか分からない
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value) && value)
            {
                ExpandRequested?.Invoke(this);
            }
        }
    }

    public event Action<TagSubRow>? ExpandRequested;

    /// <summary>中に入っている商品。開いたときに詰める（全部の小分類で先に読むと重い）。</summary>
    public ObservableCollection<TagItemRow> Items { get; } = [];

    public string ItemCountText => ItemCount == 0 ? "未使用" : $"{ItemCount}";

    public bool IsUsed => ItemCount > 0;

    // 押せないときは理由を言う（押しても何も起きないように見える物を作らない）
    public string ExpandToolTip => IsUsed
        ? "開くと、この小分類が付いている商品が並びます。"
        : "この小分類はまだどの商品にも付いていないので、開いても中身がありません。";

    public string ShowItemsToolTip => IsUsed
        ? "この小分類が付いている商品を、検索で開きます。"
        : "この小分類はまだどの商品にも付いていません。編集画面で付けると開けます。";

    private bool _isHidden;

    /// <summary>商品名で絞っているとき、当たりが無い小分類は出さない（ユーザ指示 2026-09-18）。</summary>
    public bool IsHidden
    {
        get => _isHidden;
        set => SetField(ref _isHidden, value);
    }

    /// <summary>寄せ先の候補。自分自身は外す（自分に改名しても何も起きない）。</summary>
    public IReadOnlyList<string> OtherNames { get; set; } = [];

    /// <summary>移動先のトップ候補。自分が属するトップは外す。</summary>
    public IReadOnlyList<string> MoveTargets { get; set; } = [];

    public RelayCommand? ShowItemsCommand { get; set; }

    public RelayCommand? RenameCommand { get; set; }

    public RelayCommand? MoveCommand { get; set; }

    public RelayCommand? DeleteCommand { get; set; }
}

/// <summary>
/// 小分類の中に入っている商品1件（ユーザ指示 2026-09-18）。
/// 改変の一覧と同じ形（絵・名前）で出し、押すと商品ページへ。
/// </summary>
public sealed class TagItemRow : ViewModelBase
{
    public required string ItemId { get; init; }

    public required string Name { get; init; }

    public required string ShopName { get; init; }

    /// <summary>名前の右に出す札（属性の管理の「85 %」）。タグの管理では出さない。</summary>
    public string? ValueText { get; init; }

    public bool HasValue => ValueText is not null;

    public string? ThumbnailPath { get; init; }

    public BoothAssetManager.App.Services.ThumbnailLoader? Thumbnails { get; init; }

    /// <summary>裏で読み、届いたら描き直す（改変の一覧・アバターの管理と同じ扱い）。</summary>
    public System.Windows.Media.Imaging.BitmapSource? Thumbnail => ThumbnailPath is { } path
        ? Thumbnails?.PeekForTile(path, () => OnPropertyChanged(nameof(Thumbnail)))
        : null;

    /// <summary>ホバーで出す大きめの絵（ユーザ指示 2026-09-18。ほかの一覧と同じ）。窓が開いたときに初めて読む。</summary>
    public System.Windows.Media.Imaging.BitmapSource? HoverImage => ThumbnailPath is { } path
        ? Thumbnails?.PeekForCard(path, () => OnPropertyChanged(nameof(HoverImage)))
        : null;

    public bool HasHoverImage => ThumbnailPath is not null;

    public string Initial => AvatarText.InitialOf(Name);

    public RelayCommand? OpenCommand { get; set; }

    /// <summary>カード表示（ユーザ指示 2026-09-20・M4）で使う、検索と同じカード。作るのに要る物をまとめて受け取る。</summary>
    public Func<ItemCardViewModel?>? CardFactory { get; init; }

    private ItemCardViewModel? _card;

    /// <summary>**カード表示に切り替えたときに初めて作る**（1つの分類に数百件入ることがある）。</summary>
    public ItemCardViewModel? Card => _card ??= CardFactory?.Invoke();
}

/// <summary>マスタに無いのにitemが参照している名前。要確認は知らせるだけで、直せるのはここ。</summary>
public sealed class OrphanTagRow : ViewModelBase
{
    public required string Top { get; init; }

    /// <summary>null ならトップレベル、入っていればその配下のサブ。</summary>
    public string? Sub { get; init; }

    public required int ItemCount { get; init; }

    public bool IsSub => Sub is not null;

    public string Name => Sub ?? Top;

    /// <summary>サブは、どのトップの配下なのかが分からないと直しようがない。</summary>
    public string DisplayName => IsSub ? $"{Top}／{Sub}" : Top;

    public string KindText => IsSub ? "小分類" : "大分類";

    public string ItemCountText => $"{ItemCount} 件の商品が参照";

    public string MergePlaceholder => IsSub
        ? $"「{Top}」の既にある小分類へ統合する"
        : "既にある大分類へ統合する";

    /// <summary>寄せ先の候補。トップならトップ一覧、サブなら同じトップの既存サブ。</summary>
    public IReadOnlyList<string> MergeCandidates { get; set; } = [];

    public RelayCommand? AddToMasterCommand { get; set; }

    public RelayCommand? MergeCommand { get; set; }

    public RelayCommand? RemoveCommand { get; set; }
}

/// <summary>
/// タグの管理画面。userTagマスタ（トップ／サブの2階層）を編集する。
///
/// item側は名前で参照しているので、改名も削除も全itemの書き換えを伴う。
/// 戻せない操作なので、実行前に「何件が書き換わるか」を必ず数えて見せる。
/// マスタに無い名前をitemが参照したままの状態もここに出す。
/// 要確認はそれを知らせるだけで、直せる場所はこの画面しかないため。
/// </summary>
public sealed class TagManageViewModel : ViewModelBase, IPendingWrites
{
    private readonly AppServiceContainer _services;

    private ManageItemView? _itemView;

    /// <summary>商品をカードで出すかリストで出すか（M4）。ほかの画面と同じ切り替え。</summary>
    private ManageItemView ItemView => _itemView ??= new ManageItemView(_services, "tag", () =>
    {
        OnPropertyChanged(nameof(IsCardMode));
        OnPropertyChanged(nameof(IsListMode));
    });

    public bool IsCardMode => ItemView.IsCardMode;

    public bool IsListMode => ItemView.IsListMode;

    private RelayCommand? _showCards;
    private RelayCommand? _showList;

    public RelayCommand ShowCardsCommand => _showCards ??= new RelayCommand(() => ItemView.Set(false));

    public RelayCommand ShowListCommand => _showList ??= new RelayCommand(() => ItemView.Set(true));
    private PaneColumn? _listPane;

    /// <summary>左の一覧の列。ドラッグで幅を変えられる（ユーザ判断 2026-09-14）。</summary>
    public PaneColumn ListPane => _listPane ??= new PaneColumn(_services.PaneWidths, "tags.list");
    private readonly MainViewModel _main;

    /// <summary>
    /// 選んでいた大分類と、開いていた小分類を覚える（ユーザ指示 2026-09-18：商品ページから戻ると
    /// 先頭に戻ってしまい、どこを見ていたか分からなくなる）。画面は開くたびに作り直すので、型の側で持つ。
    /// アプリを閉じるまでの記憶でよい（次の起動で開き直すほどの話ではない）
    /// </summary>
    private static string? _lastSelectedTop;

    private static readonly HashSet<string> ExpandedSubs = new(StringComparer.Ordinal);

    /// <summary>大分類と小分類の組を1つの鍵にする。名前には入らない改行で区切る。</summary>
    private static string SubKey(TagSubRow row) => row.Top + "\n" + row.Name;

    private List<TagTopRow> _allTops = [];
    private TagTopRow? _selected;
    private string _filterText = string.Empty;
    private string _memoDraft = string.Empty;
    private string _statusText = string.Empty;
    private bool _isBusy;
    private bool _swappingMemo;
    private bool _rebuildingList;

    public TagManageViewModel(AppServiceContainer services, MainViewModel main)
    {
        _services = services;
        _main = main;

        AddTopCommand = new RelayCommand(parameter => AddTopAsync(parameter as string).Forget());
        AddSubCommand = new RelayCommand(parameter => AddSubAsync(parameter as string).Forget(), _ => Selected is not null);
        RenameTopCommand = new RelayCommand(() => AskRenameTopAsync().Forget(), () => Selected is not null);
        DeleteTopCommand = new RelayCommand(() => DeleteTopAsync().Forget(), () => Selected is not null);
        RefreshCommand = new RelayCommand(() => ReloadAsync().Forget());
        ToggleAllCommand = new RelayCommand(ToggleAll);
        ShowItemsCommand = new RelayCommand(
            () => _main.ShowItemsWithTag(Selected!.Name),
            () => Selected is { ItemCount: > 0 });

        // メモは押さずに残す（ユーザ指示 2026-09-18。ショップ・アバターと同じ）
        _saveMemo = new Debounced(TimeSpan.FromMilliseconds(800), SaveMemoAsync);
        _saveSubMemo = new Debounced(TimeSpan.FromMilliseconds(800), SaveSubMemosAsync);

        _sort = services.UiState.TagSort switch
        {
            "count" => TagSortMode.Count,
            "manual" => TagSortMode.Manual,
            _ => TagSortMode.Name,
        };

        ReloadAsync().Forget();
    }

    private readonly Debounced _saveMemo;
    private readonly Debounced _saveSubMemo;

    /// <summary>待っているメモを今書く（画面を離れる前・閉じる前）。</summary>
    public Task FlushPendingWritesAsync() => Task.WhenAll(_saveMemo.RunNowAsync(), _saveSubMemo.RunNowAsync());

    /// <summary>
    /// 並べ方（ユーザ指示 2026-09-18）。既定は名前順。
    /// 選ぶと `userTags.json` の並びも同じ順に書き換える——検索の候補もこの並びをそのまま使うので、
    /// 画面だけ並べ替えると「画面と候補で順番が違う」状態になる
    /// </summary>
    private TagSortMode _sort;

    public TagSortMode Sort
    {
        get => _sort;
        set
        {
            if (!SetField(ref _sort, value))
            {
                return;
            }

            var saved = value switch
            {
                TagSortMode.Count => "count",
                TagSortMode.Manual => "manual",
                _ => "name",
            };

            _main.SaveUiStateAsync(state => state with { TagSort = saved }).Forget();
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
    /// 今の並べ方で `userTags.json` を並べ替える。手で並べた順のときは何もしない
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

        var tops = SortNames(_allTops.Select(row => (row.Name, row.ItemCount)));
        await _services.Commands.ExecuteAsync(new UiCommand.ReorderUserTags(tops));

        // 小分類も同じ並べ方に揃える。大分類だけ並べても、開いた先がばらばらでは読めない
        var master = _services.Store.UserTags.Load();
        foreach (var top in master.Tops)
        {
            if (top.Subs.Count < 2)
            {
                continue;
            }

            _subCounts.TryGetValue(top.Name, out var usage);
            var subs = SortNames(top.Subs.Select(sub =>
                (sub.Name, usage?.SubCounts.GetValueOrDefault(sub.Name) ?? 0)));

            await _services.Commands.ExecuteAsync(new UiCommand.ReorderUserTags(subs, top.Name));
        }

        await ReloadAsync();
        _main.RefreshMasters();
    }

    private IReadOnlyList<string> SortNames(IEnumerable<(string Name, int Count)> entries) => Sort switch
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

    public ObservableCollection<TagTopRow> Tops { get; } = [];

    public ObservableCollection<TagSubRow> Subs { get; } = [];

    public ObservableCollection<OrphanTagRow> Orphans { get; } = [];

    /// <summary>改名の寄せ先候補。既存を選べば統合、無い語を入れれば単なる改名になる。</summary>
    public ObservableCollection<string> OtherTopNames { get; } = [];

    /// <summary>
    /// マスタにある全トップレベル。マスタに無い分類の寄せ先はこちらを候補にする。
    /// 右側で何を選んでいるかとは無関係なので、<see cref="OtherTopNames"/> は使えない。
    /// </summary>
    public ObservableCollection<string> AllTopNames { get; } = [];

    /// <summary>サブレベルの改名の寄せ先候補。同じトップの中だけを候補にする。</summary>
    public ObservableCollection<string> SubNames { get; } = [];

    /// <summary>
    /// 戻る（ユーザ判断 2026-09-20・V2・V3）。**ナビから入っても直前の画面へ戻れる。**
    /// 出し方・見た目はどの画面でも同じにする（履歴が無いときだけ出さない）。
    /// </summary>
    public MainViewModel Main => _main;

    public RelayCommand AddTopCommand { get; }

    public RelayCommand AddSubCommand { get; }

    public RelayCommand RenameTopCommand { get; }

    public RelayCommand DeleteTopCommand { get; }

    public RelayCommand RefreshCommand { get; }

    /// <summary>この分類が付いているitemを検索で見せる。消す・統合するの判断は中身を見ないとできない。</summary>
    public RelayCommand ShowItemsCommand { get; }

    public bool SelectedIsUsed => Selected is { ItemCount: > 0 };

    public TagTopRow? Selected
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

            _selected = value;

            if (_selected is not null)
            {
                _selected.IsSelected = true;
                _lastSelectedTop = _selected.Name;
            }

            // 選び直しで欄を入れ替えるときは、自動保存を走らせない
            _swappingMemo = true;
            MemoDraft = _selected?.Memo ?? string.Empty;
            _swappingMemo = false;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedName));
            OnPropertyChanged(nameof(SelectedUsageText));
            OnPropertyChanged(nameof(SelectedIsUsed));
            OnPropertyChanged(nameof(ShowItemsToolTip));
            RebuildSubs();
            RebuildOtherNames();
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelection => Selected is not null;

    public string SelectedName => Selected?.Name ?? string.Empty;

    public string SelectedUsageText => Selected is null
        ? string.Empty
        : Selected.ItemCount == 0
            ? "まだどの商品にも付いていません。編集画面で付けると、ここに件数が出ます。"
            : $"{Selected.ItemCount} 件の商品に付いています";

    public string ShowItemsToolTip => SelectedIsUsed
        ? "この大分類が付いている商品を、検索で開きます。"
        : "この大分類はまだどの商品にも付いていません。編集画面で付けると開けます。";

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value))
            {
                RebuildTops();
            }
        }
    }

    public string MemoDraft
    {
        get => _memoDraft;
        set
        {
            if (SetField(ref _memoDraft, value))
            {
                OnPropertyChanged(nameof(MemoChanged));

                // 押さずに残す（ユーザ指示 2026-09-18）。選び直しで欄を入れ替えたときは書かない
                if (!_swappingMemo)
                {
                    _memoPending = true;
                    _saveMemo.Request();
                }
            }
        }
    }

    public bool MemoChanged => Selected is not null && MemoDraft != (Selected.Memo ?? string.Empty);

    public bool HasOrphans => Orphans.Count > 0;

    /// <summary>
    /// 左の一覧が空のとき、真っ白にせず次にやることを書く（`ui-rules.md`・E1）。
    /// **探して0件と、そもそも1つも無いのを言い分ける**（検索・フォルダ・改変と同じ書き分け）。
    /// </summary>
    public bool IsEmpty => Tops.Count == 0;

    public string EmptyText => HasFilter
        ? "探している言葉に当てはまる大分類がありません。言葉を変えるか、絞り込みを消してください。"
        : "大分類はまだありません。上の「大分類を追加」に名前を入れるか、商品の編集画面でユーザータグを付けると、ここに並びます。";

    public int TopCount => _allTops.Count;

    public string HeaderText => $"大分類 {TopCount} 件";

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetField(ref _statusText, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => StatusText.Length > 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task ReloadAsync()
    {
        await FlushMemosAsync();

        IsBusy = true;
        try
        {
            var master = _services.Store.UserTags.Load();
            var usage = await _services.UserTags.LoadUsageAsync();
            var orphans = await _services.UserTags.LoadOrphansAsync();

            RunOnUiThread(() =>
            {
                var counts = usage.ToDictionary(entry => entry.Top, StringComparer.CurrentCultureIgnoreCase);
                var keep = Selected?.Name;

                _allTops = master.Tops.Select(top => new TagTopRow
                {
                    Name = top.Name,
                    Memo = top.Memo,
                    SubCount = top.Subs.Count,
                    ItemCount = counts.TryGetValue(top.Name, out var entry) ? entry.ItemCount : 0,
                }).ToList();

                _subCounts = counts;

                AllTopNames.Clear();
                foreach (var top in _allTops)
                {
                    AllTopNames.Add(top.Name);
                }

                Orphans.Clear();
                foreach (var orphan in orphans)
                {
                    Orphans.Add(CreateOrphanRow(orphan));
                }

                RebuildTops();
                OnPropertyChanged(nameof(TopCount));
                OnPropertyChanged(nameof(HeaderText));
                OnPropertyChanged(nameof(HasOrphans));

                // 選び直す。改名した直後は名前が変わっているので、無ければ先頭に落とす。
                // 開き直したとき（keep が無いとき）は、前に見ていた大分類に戻る
                Selected = _allTops.FirstOrDefault(row => row.Name == (keep ?? _lastSelectedTop))
                    ?? Tops.FirstOrDefault();
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private IReadOnlyDictionary<string, UserTagUsage> _subCounts =
        new Dictionary<string, UserTagUsage>(StringComparer.CurrentCultureIgnoreCase);

    /// <summary>
    /// 探すのは大分類の名前だけでなく、**小分類の名前と商品名**も（ユーザ指示 2026-09-18）。
    /// どこに入れたか忘れた分類は、中の商品名からしか辿れないことがある
    /// </summary>
    private void RebuildTops()
    {
        // 書き方は検索画面と同じ（ユーザ指示 2026-09-19）。大分類・小分類の名前にも同じ式を当てる
        var filter = ItemTextFilter.Create(_filterText);
        var master = filter is null ? null : _services.Store.UserTags.Load();
        var items = filter is null ? [] : _main.Search.SnapshotItems();

        // 作り直す間は、一覧が書き戻す「選択なし」を受けない。受けると、探すたびに右が空になっていた。
        // 当たりから外れても右は今見ている物のまま残す（見ている物が勝手に消えると、何を探していたか分からなくなる）
        _rebuildingList = true;
        try
        {
            Tops.Clear();
            foreach (var row in _allTops.Where(row => filter is null || MatchesFilter(row, filter, master!, items)))
            {
                Tops.Add(row);
            }
        }
        finally
        {
            _rebuildingList = false;
        }

        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasFilter));
        OnPropertyChanged(nameof(FilterResultText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    private static bool MatchesFilter(
        TagTopRow row, ItemTextFilter filter, UserTagMaster master, IReadOnlyList<ItemRecord> items)
    {
        if (filter.MatchesName(row.Name))
        {
            return true;
        }

        var top = master.Tops.FirstOrDefault(entry =>
            string.Equals(entry.Name, row.Name, StringComparison.CurrentCultureIgnoreCase));

        if (top is not null && top.Subs.Any(sub => filter.MatchesName(sub.Name)))
        {
            return true;
        }

        // 商品でも引く。探している物がどの分類に入っているか分からないときの逃げ道
        return items.Any(item =>
            item.Local.UserTags.Any(tag => string.Equals(tag.Top, row.Name, StringComparison.CurrentCultureIgnoreCase))
            && filter.Matches(item));
    }

    private bool _isAddingSub;
    private string _itemFilter = string.Empty;

    /// <summary>小分類を足す欄を出しているか（普段は隠す。ユーザ指示 2026-09-18：入力欄が並ぶと読みづらい）。</summary>
    public bool IsAddingSub
    {
        get => _isAddingSub;
        set => SetField(ref _isAddingSub, value);
    }

    /// <summary>
    /// この大分類の中を商品名で探す（ユーザ指示 2026-09-18）。
    /// 当たった商品を持つ小分類だけを開いて出す——どの小分類に入れたか分からない物を辿るため
    /// </summary>
    public string ItemFilter
    {
        get => _itemFilter;
        set
        {
            if (SetField(ref _itemFilter, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasItemFilter));
                RebuildSubs();
                OnPropertyChanged(nameof(ItemFilterResultText));
            }
        }
    }

    public bool HasItemFilter => _itemFilter.Trim().Length > 0;

    private int _itemFilterHits;
    private int _subFilterHits;

    public string ItemFilterResultText => HasItemFilter
        ? $"「{_itemFilter.Trim()}」に当たる小分類 {_subFilterHits} 件・商品 {_itemFilterHits} 件（当たった所だけを出しています）"
        : string.Empty;

    /// <summary>絞り込んでいるか。絞っている間は、右の小分類と中の商品も同じ語で絞る。</summary>
    public bool HasFilter => _filterText.Trim().Length > 0;

    public string FilterResultText => HasFilter
        ? $"「{_filterText.Trim()}」に当たる大分類 {Tops.Count} 件（小分類名・商品も探しています）"
        : string.Empty;

    public bool HasSubs => Subs.Count > 0;

    /// <summary>
    /// 小分類をまとめて開く・畳む（ユーザ指示 2026-09-19）。1つずつ三角を押して回るのは、小分類の多い大分類で手間。
    /// 開けるのは中身のある小分類だけ（中身の無い物は三角も押せない）。探している間は、出ている物だけが相手
    /// </summary>
    public RelayCommand ToggleAllCommand { get; }

    private IEnumerable<TagSubRow> OpenableSubs => Subs.Where(row => row.IsUsed && !row.IsHidden);

    /// <summary>開ける物が全部開いていれば「すべて畳む」、1つでも畳んでいれば「すべて開く」。</summary>
    public bool AllSubsExpanded => OpenableSubs.Any() && OpenableSubs.All(row => row.IsExpanded);

    public string ToggleAllText => AllSubsExpanded ? "すべて畳む" : "すべて開く";

    private void ToggleAll()
    {
        var expand = !AllSubsExpanded;
        foreach (var row in OpenableSubs.ToList())
        {
            row.IsExpanded = expand;
        }

        OnPropertyChanged(nameof(ToggleAllText));
    }

    private void RebuildSubs()
    {
        Subs.Clear();
        SubNames.Clear();
        if (Selected is null)
        {
            OnPropertyChanged(nameof(HasSubs));
            return;
        }

        var master = _services.Store.UserTags.Load();
        var top = master.Tops.FirstOrDefault(entry =>
            string.Equals(entry.Name, Selected.Name, StringComparison.CurrentCultureIgnoreCase));

        if (top is null)
        {
            OnPropertyChanged(nameof(HasSubs));
            return;
        }

        _subCounts.TryGetValue(Selected.Name, out var usage);

        foreach (var sub in top.Subs)
        {
            var row = new TagSubRow
            {
                Name = sub.Name,
                Top = top.Name,
                Memo = sub.Memo,
                ItemCount = usage?.SubCounts.GetValueOrDefault(sub.Name) ?? 0,
                MemoDraft = sub.Memo ?? string.Empty,
            };

            row.MemoEdited += _ =>
            {
                _subMemoPending = true;
                _saveSubMemo.Request();
            };
            row.ExpandRequested += entry => FillSubItemsAsync(entry).Forget();

            // 開いていた小分類は、商品ページから戻ってきたときも開いたままにする（ユーザ指示 2026-09-18）
            var key = SubKey(row);
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(TagSubRow.IsExpanded))
                {
                    return;
                }

                if (row.IsExpanded)
                {
                    ExpandedSubs.Add(key);
                }
                else
                {
                    ExpandedSubs.Remove(key);
                }

                // 1つずつ開け閉めしても、「すべて開く／すべて畳む」の言い方を合わせる
                OnPropertyChanged(nameof(ToggleAllText));
            };

            if (row.IsUsed && ExpandedSubs.Contains(key))
            {
                row.IsExpanded = true;
            }

            // 名前の変更は窓で聞く（ユーザ指示 2026-09-18：行に入力欄を常設せず、ボタンから）
            row.RenameCommand = new RelayCommand(() => AskRenameSubAsync(row).Forget());
            row.DeleteCommand = new RelayCommand(() => DeleteSubAsync(row).Forget());
            row.ShowItemsCommand = new RelayCommand(
                () => _main.ShowItemsWithTag(row.Top, row.Name),
                () => row.IsUsed);
            row.MoveCommand = new RelayCommand(() => MoveSubToTopAsync(row).Forget(), () => _allTops.Count > 1);
            row.MoveTargets = _allTops
                .Where(entry => !string.Equals(entry.Name, top.Name, StringComparison.CurrentCultureIgnoreCase))
                .Select(entry => entry.Name)
                .ToList();
            Subs.Add(row);
            SubNames.Add(row.Name);
        }

        // 自分以外を寄せ先の候補にする。自分に改名しても何も起きないので出さない
        foreach (var row in Subs)
        {
            row.OtherNames = SubNames.Where(name => name != row.Name).ToList();
        }

        ApplyItemFilter();
        OnPropertyChanged(nameof(HasSubs));
        OnPropertyChanged(nameof(ToggleAllText));
    }

    /// <summary>
    /// 商品名で絞る（ユーザ指示 2026-09-18）。当たった商品を持つ小分類だけを開いて出し、
    /// 中身も当たった商品だけにする。空にしたら元の（覚えている）開き方に戻す
    /// </summary>
    private void ApplyItemFilter()
    {
        // 書き方は検索画面と同じ（ユーザ指示 2026-09-19）
        var filter = ItemTextFilter.Create(_itemFilter);
        _itemFilterHits = 0;

        if (filter is null)
        {
            foreach (var row in Subs)
            {
                row.IsHidden = false;
                row.Items.Clear();
                row.IsExpanded = row.IsUsed && ExpandedSubs.Contains(SubKey(row));

                if (row.IsExpanded)
                {
                    FillSubItemsAsync(row).Forget();
                }
            }

            return;
        }

        var all = _main.Search.SnapshotItems();
        _subFilterHits = 0;

        foreach (var row in Subs)
        {
            // 小分類の名前で当たったら、中の商品は全部出す（ユーザ指示 2026-09-19：
            // 名前で探した人は、その小分類の中身を見たい。商品名でさらに削ると探した物が隠れる）
            var nameHit = filter.MatchesName(row.Name);

            var matched = all
                .Where(item => item.Local.UserTags.Any(tag =>
                    string.Equals(tag.Top, row.Top, StringComparison.CurrentCultureIgnoreCase)
                    && tag.Subs.Any(sub => string.Equals(sub, row.Name, StringComparison.CurrentCultureIgnoreCase))))
                .Where(item => nameHit || filter.Matches(item))
                .OrderBy(item => item.DisplayName, StringComparer.CurrentCulture)
                .ToList();

            row.Items.Clear();
            foreach (var item in matched)
            {
                row.Items.Add(CreateItemRow(item));
            }

            if (nameHit)
            {
                _subFilterHits++;
            }
            else
            {
                _itemFilterHits += matched.Count;
            }

            // 名前で当たった小分類は、中身が無くても出す（使っていない小分類を名前で探すこともある）
            row.IsHidden = !nameHit && matched.Count == 0;
            row.IsExpanded = matched.Count > 0;
        }
    }

    /// <summary>マスタに載っているサブレベル名。寄せ先の候補に使う。</summary>
    private IReadOnlyList<string> SubNamesOf(string top)
        => _services.Store.UserTags.Load().Tops
            .FirstOrDefault(entry => string.Equals(entry.Name, top, StringComparison.CurrentCultureIgnoreCase))
            ?.Subs.Select(sub => sub.Name).ToList()
            ?? [];

    private void RebuildOtherNames()
    {
        OtherTopNames.Clear();
        foreach (var name in _allTops
            .Where(row => Selected is null || row.Name != Selected.Name)
            .Select(row => row.Name))
        {
            OtherTopNames.Add(name);
        }
    }

    private OrphanTagRow CreateOrphanRow(OrphanUserTag orphan)
    {
        var row = new OrphanTagRow
        {
            Top = orphan.Top,
            Sub = orphan.Sub,
            ItemCount = orphan.ItemCount,

            // サブの寄せ先は同じトップの中だけ。別のトップのサブへは寄せられない
            MergeCandidates = orphan.Sub is null
                ? _allTops.Select(top => top.Name).ToList()
                : SubNamesOf(orphan.Top),
        };

        row.AddToMasterCommand = new RelayCommand(() => AddOrphanToMasterAsync(row).Forget());
        row.MergeCommand = new RelayCommand(parameter => MergeOrphanAsync(row, parameter as string).Forget());
        row.RemoveCommand = new RelayCommand(() => RemoveOrphanAsync(row).Forget());

        return row;
    }

    private async Task AddTopAsync(string? name)
    {
        // 改行やタブは空白に寄せて1行にする（I13）
        var trimmed = NameText.Normalize(name);

        // 空のまま押したときに黙って終わらない（I1）。押した人は「やった」と思っている
        if (trimmed.Length == 0)
        {
            StatusText = "大分類の名前を入れてから押してください。";
            return;
        }

        // 長すぎるものは**切らずに断る**（切ると打った名前と食い違う・I13）
        if (NameText.IsTooLong(trimmed))
        {
            StatusText = NameText.TooLongMessage("大分類の名前");
            return;
        }

        // 既にある名前は足されない。**足していないのに「追加しました」と言わない**（I2）
        if (_allTops.Any(row => string.Equals(row.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)))
        {
            StatusText = $"「{trimmed}」は既にあります。";
            Selected = _allTops.FirstOrDefault(row =>
                string.Equals(row.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)) ?? Selected;
            return;
        }

        await _services.Commands.ExecuteAsync(new UiCommand.AddUserTag(trimmed));
        StatusText = $"「{trimmed}」を追加しました。";
        await ReloadAsync();
        _main.RefreshMasters();
        Selected = _allTops.FirstOrDefault(row =>
            string.Equals(row.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)) ?? Selected;
    }

    private async Task AddSubAsync(string? name)
    {
        if (Selected is null)
        {
            return;
        }

        var trimmed = NameText.Normalize(name);
        if (trimmed.Length == 0)
        {
            StatusText = "小分類の名前を入れてから押してください。";
            return;
        }

        if (NameText.IsTooLong(trimmed))
        {
            StatusText = NameText.TooLongMessage("小分類の名前");
            return;
        }

        if (Subs.Any(row => string.Equals(row.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)))
        {
            StatusText = $"「{Selected.Name}」には「{trimmed}」が既にあります。";
            return;
        }

        await _services.Commands.ExecuteAsync(new UiCommand.AddUserTag(Selected.Name, trimmed));
        StatusText = $"「{Selected.Name}」に「{trimmed}」を追加しました。";
        await ReloadAsync();
        _main.RefreshMasters();
    }

    /// <summary>
    /// 改名。既にある名前を指すと統合になる。どちらも戻せないので、
    /// 何件のitemが書き換わるかを出してから確認を取る。
    /// </summary>
    private async Task RenameTopAsync(string? newName)
    {
        var target = newName?.Trim();
        if (Selected is null || string.IsNullOrEmpty(target)
            || string.Equals(target, Selected.Name, StringComparison.CurrentCultureIgnoreCase))
        {
            return;
        }

        var merging = _allTops.Any(row =>
            string.Equals(row.Name, target, StringComparison.CurrentCultureIgnoreCase));

        // 統合だけもう一度聞く（D5。理由は小分類の方に書いた）
        if (merging && !Confirm(
                $"「{Selected.Name}」を「{target}」に統合します。\n\n"
                + $"{Selected.ItemCount} 件の商品を書き換えます。小分類は「{target}」側へまとめます。\n"
                + MemoNotice(Selected.Memo, target)
                + "この操作は元に戻せません。",
                "大分類を統合する"))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new UiCommand.RenameUserTag(Selected.Name, null, target));
        if (result is CommandResult.UserTagsRewritten rewritten)
        {
            StatusText = rewritten.Result.WasMerged
                ? $"「{target}」に統合しました（{rewritten.Result.ItemsUpdated} 件の商品を書き換え）。"
                : $"「{target}」に変更しました（{rewritten.Result.ItemsUpdated} 件の商品を書き換え）。";
        }

        var keep = target;
        await ReloadAsync();
        Selected = _allTops.FirstOrDefault(row =>
            string.Equals(row.Name, keep, StringComparison.CurrentCultureIgnoreCase)) ?? Selected;

        await _main.ReloadLibraryAsync();
    }

    /// <summary>
    /// 削除。マスタから消すだけだとitem側に参照が残るので、付けていたitemからも外す。
    /// userTagが空になるitemは編集の対象に戻るので、その件数も先に出す。
    /// </summary>
    private async Task DeleteTopAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var message = Selected.ItemCount == 0
            ? $"「{Selected.Name}」を削除します。\n\nどの商品にも付いていないので、影響はありません。"
            : $"「{Selected.Name}」を削除します。\n\n"
                + $"{Selected.ItemCount} 件の商品からこの大分類が外れます（小分類も一緒に外れます）。\n"
                + "\nこの操作は元に戻せません。同じ名前で作り直しても、商品への割り当ては戻りません。";

        if (!Confirm(message, "大分類を削除する"))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new UiCommand.DeleteUserTag(Selected.Name));
        if (result is CommandResult.UserTagsRewritten rewritten)
        {
            StatusText = rewritten.Result.ItemsLeftUntagged > 0
                ? $"削除しました（{rewritten.Result.ItemsUpdated} 件の商品から外し、"
                    + $"うち {rewritten.Result.ItemsLeftUntagged} 件はユーザータグが空になったので編集の対象に戻ります）。"
                : $"削除しました（{rewritten.Result.ItemsUpdated} 件の商品から外しました）。";
        }

        Selected = null;
        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>名前を変える窓を出してから実行する。既にある名前を選ぶと統合になる。</summary>
    private async Task AskRenameSubAsync(TagSubRow row)
    {
        var model = new RenameTagDialogViewModel("小分類", row.Name, row.ItemCount, row.OtherNames);
        if (new Views.RenameTagDialog(model).ShowDialog() != true)
        {
            return;
        }

        await RenameSubAsync(row, model.Target);
    }

    /// <summary>大分類の名前を変える窓。</summary>
    private async Task AskRenameTopAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var others = _allTops.Where(row => row.Name != Selected.Name).Select(row => row.Name).ToList();
        var model = new RenameTagDialogViewModel("大分類", Selected.Name, Selected.ItemCount, others);
        if (new Views.RenameTagDialog(model).ShowDialog() != true)
        {
            return;
        }

        await RenameTopAsync(model.Target);
    }

    private async Task RenameSubAsync(TagSubRow row, string? newName)
    {
        var target = newName?.Trim();
        if (string.IsNullOrEmpty(target)
            || string.Equals(target, row.Name, StringComparison.CurrentCultureIgnoreCase))
        {
            return;
        }

        // **統合だけもう一度聞く**（ユーザ判断 2026-09-20・D5）。統合は元に戻せないが、
        // 名前の変更は同じ手順で戻せる。戻せるものまで2回聞くと、聞かれること自体が読まれなくなる
        var merging = Subs.Any(entry => string.Equals(entry.Name, target, StringComparison.CurrentCultureIgnoreCase));
        if (merging && !Confirm(
                $"「{row.Top}」の「{row.Name}」を「{target}」に統合します。\n\n"
                + $"{row.ItemCount} 件の商品を書き換えます。\n"
                + MemoNotice(row.Memo, target)
                + "\nこの操作は元に戻せません。",
                "小分類を統合する"))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new UiCommand.RenameUserTag(row.Top, row.Name, target));
        if (result is CommandResult.UserTagsRewritten rewritten)
        {
            StatusText = $"「{target}」に変更しました（{rewritten.Result.ItemsUpdated} 件の商品を書き換え）。";
        }

        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    private async Task DeleteSubAsync(TagSubRow row)
    {
        var message = row.ItemCount == 0
            ? $"「{row.Top}」から「{row.Name}」を削除します。\n\nどの商品にも付いていないので、影響はありません。"
            : $"「{row.Top}」から「{row.Name}」を削除します。\n\n"
                + $"{row.ItemCount} 件の商品からこの小分類が外れます。「{row.Top}」自体は付いたままです。\n"
                + "\nこの操作は元に戻せません。同じ名前で作り直しても、商品への割り当ては戻りません。";

        if (!Confirm(message, "小分類を削除する"))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new UiCommand.DeleteUserTag(row.Top, row.Name));
        if (result is CommandResult.UserTagsRewritten rewritten)
        {
            StatusText = $"「{row.Name}」を削除しました（{rewritten.Result.ItemsUpdated} 件の商品から外しました）。";
        }

        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>
    /// トップレベルを並べ替える。並びは検索の絞り込みにも編集の候補にもそのまま出るので、
    /// 「よく使う順」に置けること自体が機能になる。itemは名前で参照しているので触らない。
    ///
    /// 絞り込み中は見えている分しか動かせないため、隠れている行の位置は保つ。
    /// </summary>
    /// <summary>
    /// ドラッグで置き換える。置いた並びを残したいので、並べ方は「手で並べた順」に切り替える
    /// （名前順のままだと、次の読み直しで元に戻って「動かなかった」ように見える）
    /// </summary>
    public async Task MoveTopAsync(TagTopRow moved, TagTopRow target, bool after)
    {
        SwitchToManual();

        var order = _allTops.Select(row => row.Name).ToList();
        if (!Reorder(order, moved.Name, target.Name, after))
        {
            return;
        }

        await _services.Commands.ExecuteAsync(new UiCommand.ReorderUserTags(order));
        await ReloadAsync();

        // 並びは検索の絞り込みにもそのまま出るので、そちらも作り直す
        _main.RefreshMasters();
    }

    public async Task MoveSubAsync(TagSubRow moved, TagSubRow target, bool after)
    {
        SwitchToManual();

        var order = Subs.Select(row => row.Name).ToList();
        if (!Reorder(order, moved.Name, target.Name, after))
        {
            return;
        }

        await _services.Commands.ExecuteAsync(new UiCommand.ReorderUserTags(order, moved.Top));
        await ReloadAsync();
        _main.RefreshMasters();
    }

    /// <summary>ドラッグしたら「手で並べた順」にする。並べ方の選択も覚え直す。</summary>
    private void SwitchToManual()
    {
        if (_sort == TagSortMode.Manual)
        {
            return;
        }

        _sort = TagSortMode.Manual;
        _main.SaveUiStateAsync(state => state with { TagSort = "manual" }).Forget();

        foreach (var name in new[] { nameof(Sort), nameof(SortsByName), nameof(SortsByCount), nameof(SortsManually) })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>抜いてから差し込む。落とす先の index は抜いた後で数え直す。</summary>
    private static bool Reorder(List<string> order, string moved, string target, bool after)
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

    /// <summary>
    /// サブレベルを別のトップへ移す。削除して付け直すとitemの割当てが失われるので、
    /// 専用の操作にしてある。
    ///
    /// 滅多に使わない操作なので入力欄は常設せず、ここでダイアログを開いて
    /// 移動先と「サブが無くなった元のトップをどうするか」をまとめて決めてもらう。
    /// </summary>
    private async Task MoveSubToTopAsync(TagSubRow row)
    {
        var dialog = new Views.MoveSubDialog(
            new MoveSubDialogViewModel(_services.UserTags, row.Top, row.Name, row.MoveTargets));

        if (dialog.ShowDialog() != true
            || dialog.DataContext is not MoveSubDialogViewModel { Target: { } to })
        {
            return;
        }

        var drop = ((MoveSubDialogViewModel)dialog.DataContext).DropEmptySourceTop;

        var result = await _services.Commands.ExecuteAsync(
            new UiCommand.MoveUserTagSub(row.Top, row.Name, to, drop));

        if (result is CommandResult.UserTagsRewritten rewritten)
        {
            var parts = new List<string> { $"{rewritten.Result.ItemsUpdated} 件の商品を書き換え" };

            if (rewritten.Result.ItemsGainedTop > 0)
            {
                parts.Add($"うち {rewritten.Result.ItemsGainedTop} 件に「{to}」が新しく付きました");
            }

            if (rewritten.Result.ItemsSourceTopRemoved > 0)
            {
                parts.Add($"{rewritten.Result.ItemsSourceTopRemoved} 件から「{row.Top}」を外しました");
            }

            StatusText = $"「{to}」の下へ移しました（{string.Join("、", parts)}）。";
        }

        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    private bool _memoPending;
    private bool _subMemoPending;

    /// <summary>
    /// 待っているメモを今書く。選び直す・読み直す前に呼ぶ——800ms の待ちの間に移ると、
    /// 待ちが明けたときには欄も小分類の行も移った先の物になっていて、打った文が消えていた
    /// </summary>
    private void FlushMemos() => FlushMemosAsync().Forget();

    /// <summary>
    /// 書く値は最初の await より前に控えるので、待たずに選び直しても控えた方が書かれる。
    /// 読み直すときは、古いファイルを読まないよう書き終わるまで待つ
    /// </summary>
    private async Task FlushMemosAsync()
    {
        var top = _memoPending ? SaveMemoAsync() : Task.CompletedTask;
        var subs = _subMemoPending ? SaveSubMemosAsync() : Task.CompletedTask;
        _saveMemo.Cancel();
        _saveSubMemo.Cancel();
        await Task.WhenAll(top, subs);
    }

    private async Task SaveMemoAsync()
    {
        _memoPending = false;
        if (Selected is not { } row)
        {
            return;
        }

        // 読み直すと打っている途中の欄が戻るので、ここでは読み直さない（ショップ・アバターと同じ）。
        // 代わりに行の値を書き換えて、選び直したときに古いメモが出ないようにする
        var memo = MemoDraft;
        row.Memo = memo;
        await _services.Commands.ExecuteAsync(new UiCommand.SetUserTagMemo(row.Name, null, memo));
        StatusText = memo.Trim().Length == 0 ? "メモを消しました。" : "メモを保存しました。";
    }

    /// <summary>小分類のメモを保存する。打つたびではなく、止まってから変わったものだけ書く。</summary>
    private async Task SaveSubMemosAsync()
    {
        _subMemoPending = false;

        // 変わった行と文を先に控える。書く間に選び直されて行が入れ替わっても、控えた方を書く
        var changed = Subs
            .Where(row => row.MemoDraft != (row.Memo ?? string.Empty))
            .Select(row => (Row: row, Memo: row.MemoDraft))
            .ToList();

        foreach (var (row, memo) in changed)
        {
            row.Memo = memo;
            await _services.Commands.ExecuteAsync(new UiCommand.SetUserTagMemo(row.Top, row.Name, memo));
        }

        if (changed.Count > 0)
        {
            StatusText = "メモを保存しました。";
        }
    }

    /// <summary>
    /// 小分類の中身（商品）を詰める。開いたときだけ読む——全部の小分類で先に読むと、
    /// 商品のJSONを何度も読み直すことになる
    /// </summary>
    private async Task FillSubItemsAsync(TagSubRow row)
    {
        if (row.Items.Count > 0)
        {
            return;
        }

        var items = _main.Search.SnapshotItems()
            .Where(item => item.Local.UserTags.Any(tag =>
                string.Equals(tag.Top, row.Top, StringComparison.CurrentCultureIgnoreCase)
                && tag.Subs.Any(sub => string.Equals(sub, row.Name, StringComparison.CurrentCultureIgnoreCase))))
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCulture)
            .ToList();

        RunOnUiThread(() =>
        {
            foreach (var item in items)
            {
                row.Items.Add(CreateItemRow(item));
            }
        });

        await Task.CompletedTask;
    }

    /// <summary>小分類の中に出す商品1件。絵の引き方は改変の一覧と同じものを使う。</summary>
    private TagItemRow CreateItemRow(ItemRecord item)
    {
        var builder = new ModificationRowBuilder(_services, _main.Thumbnails, new Dictionary<string, ItemRecord>());

        var entry = new TagItemRow
        {
            ItemId = item.Id,
            Name = item.DisplayName,
            ShopName = item.Booth.Shop?.Name ?? string.Empty,
            ThumbnailPath = builder.ItemThumbnailPath(item),
            Thumbnails = _main.Thumbnails,
            CardFactory = () => _main.Search.CardFor(item.Id),
        };

        entry.OpenCommand = new RelayCommand(() => _main.ShowItem(item));
        return entry;
    }

    /// <summary>参照だけ残っている名前を、そのままマスタへ作る。名前が正しかった場合の直し方。</summary>
    private async Task AddOrphanToMasterAsync(OrphanTagRow row)
    {
        await _services.Commands.ExecuteAsync(row.IsSub
            ? new UiCommand.AddUserTag(row.Top, row.Sub)
            : new UiCommand.AddUserTag(row.Top));

        StatusText = $"「{row.DisplayName}」を一覧に追加しました。{row.ItemCount} 件の商品が絞り込みに出るようになります。";
        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>名前が変わっていた場合の直し方。既存の分類へ寄せる。</summary>
    private async Task MergeOrphanAsync(OrphanTagRow row, string? target)
    {
        var name = target?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (!Confirm(
            $"「{row.DisplayName}」を「{name}」に統合します。\n\n{row.ItemCount} 件の商品を書き換えます。",
            "ユーザータグを統合する"))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(row.IsSub
            ? new UiCommand.RenameUserTag(row.Top, row.Sub, name)
            : new UiCommand.RenameUserTag(row.Top, null, name));

        if (result is CommandResult.UserTagsRewritten rewritten)
        {
            StatusText = $"「{name}」に統合しました（{rewritten.Result.ItemsUpdated} 件の商品を書き換え）。";
        }

        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>もう使わない名前だった場合の直し方。itemから外す。</summary>
    private async Task RemoveOrphanAsync(OrphanTagRow row)
    {
        var notice = row.IsSub
            ? $"「{row.Top}」自体は付いたままです。"
            : string.Empty;

        if (!Confirm(
            $"「{row.DisplayName}」を {row.ItemCount} 件の商品から外します。\n\n{notice}この操作は元に戻せません。",
            "参照を外す"))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(row.IsSub
            ? new UiCommand.DeleteUserTag(row.Top, row.Sub)
            : new UiCommand.DeleteUserTag(row.Top));
        if (result is CommandResult.UserTagsRewritten rewritten)
        {
            StatusText = rewritten.Result.ItemsLeftUntagged > 0
                ? $"「{row.DisplayName}」を外しました（{rewritten.Result.ItemsUpdated} 件の商品から外し、"
                    + $"うち {rewritten.Result.ItemsLeftUntagged} 件はユーザータグが空になったので編集の対象に戻ります）。"
                : $"「{row.DisplayName}」を外しました（{rewritten.Result.ItemsUpdated} 件の商品から外しました）。";
        }

        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>
    /// 統合でメモがどうなるかを一行で伝える。黙って寄せ先に書き足すと、
    /// 後から読んだときに出所が分からないメモが増えることになる。
    /// </summary>
    private static string MemoNotice(string? memo, string target)
        => string.IsNullOrWhiteSpace(memo)
            ? string.Empty
            : $"メモは「{target}」側に「「元の名前」から統合：…」として書き足します。\n";

    /// <summary>
    /// 人が押した操作の確認（アイコンは Question で揃える。`ui-rules.md`・D9）。
    /// 既定はキャンセル。Enterを押しただけで消えないようにする。
    /// </summary>
    private static bool Confirm(string message, string caption)
        => Services.Notice.Show(
            message,
            caption,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;
}
