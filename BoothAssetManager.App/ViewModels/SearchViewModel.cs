using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 結果一覧の1行。仮想化の単位。
///
/// 中身は足し引きできる一覧にしてある。列数が変わるたびに行を全部作り直していた頃は、
/// ナビや絞り込みを畳むと見えている行のカードの見た目が全部作り直され、画面が 146〜380ms 固まった（U28）。
/// 並びの合っているカードには触らず、ずれた所だけを抜き差しする（<c>SearchViewModel.RebuildRows</c>）。
/// </summary>
public sealed class CardRow
{
    public ObservableCollection<ItemCardViewModel> Cards { get; } = [];
}

/// <summary>
/// 検索画面。アプリの生存期間中1つだけ持ち回るので、条件やスクロール位置がそのまま残る。
///
/// 絞り込み（離散値）と文字列検索を分けているのは、
/// 「なぜこの結果になったか」が分かるようにするため。
/// </summary>
public sealed partial class SearchViewModel : ViewModelBase, IItemCardHost, ISelectionScreen
{
    /// <summary>カード1枚が占める幅（カードの幅 + 右の間。設定の「サムネイルの大きさ」で変わる）。列数の計算に使う。</summary>
    private static double CardSlotWidth => global::BoothAssetManager.App.Services.CardMetrics.SlotWidth;

    /// <summary>最後に知らされた一覧の幅。カードの大きさが変わったときに、幅の知らせを待たずに割り直すため。</summary>
    private double _viewportWidth;

    /// <summary>結果一覧の左右の余白（ScrollViewerのPadding分）。</summary>
    private const double ResultsPadding = 36;

    private readonly AppServiceContainer _services;
    private readonly ThumbnailLoader _thumbnails;
    private readonly Dictionary<string, ItemCardViewModel> _cards = new(StringComparer.Ordinal);

    /// <summary>
    /// 商品ごとの検索対象文字列。正規化が高くつくので読み込み時に1度だけ作る。
    /// 入力1文字ごとに作り直すと、全商品ぶんの説明文を毎回畳むことになる。
    /// </summary>
    private Dictionary<string, Core.Services.SearchHaystack> _haystacks = new(StringComparer.Ordinal);
    private List<ItemRecord> _allItems = [];
    private List<ItemCardViewModel> _matches = [];
    private string _queryText = string.Empty;
    private Core.Services.SearchNode _queryNode = new Core.Services.SearchNode.All();
    private bool _searchAlternates;
    private Core.Services.AvatarCompatibilityIndex? _compatibility;
    private bool _isLoading;
    private bool _isFilterPanelCollapsed;
    private int _columns = 1;
    private SortOption _sort = DefaultSort;

    /// <summary>並べ替えに使う項目（M5）。BuildFacets で作り直す。</summary>
    private SortField _sortField = DefaultSortField;

    /// <summary>「最近」の足跡。絞り込み1回ぶんの間だけ持つ写し</summary>
    private RecentTimes? _recentTimes;

    /// <summary>改変から引いた「どのアバターにどの商品を使ったか」。null は「まだ読んでいない」</summary>
    private ModificationUsage? _modificationUsage;
    private List<string> _attributeNames = [];

    /// <summary>ライブラリにあるBOOTHタグの全種類。候補の元。</summary>
    private List<string> _boothTagNames = [];

    private MainViewModel? _main;

    /// <summary>「取り込み中に n 件増えました」の1行を出すために見る。</summary>
    public MainViewModel? Main => _main;

    public SearchViewModel(AppServiceContainer services, ThumbnailLoader thumbnails)
    {
        _services = services;
        _thumbnails = thumbnails;
        global::BoothAssetManager.App.Services.CardMetrics.Changed += RelayoutForCardSize;
        ClearFiltersCommand = new RelayCommand(() => ClearFiltersKeepingHistoryAsync().Forget());
        SelectAllCommand = new RelayCommand(SelectAllMatches);
        ClearSelectionCommand = new RelayCommand(ClearSelection);
        SendSelectionToEditCommand = new RelayCommand(SendSelectionToEdit, () => SelectedCount > 0);
        AddSelectionToFavoritesCommand = new RelayCommand(() => AddSelectionToFavoritesAsync().Forget(), () => SelectedCount > 0);
        AddSelectionToModificationCommand = new RelayCommand(() => AddSelectionToModificationAsync().Forget(), () => SelectedCount > 0);
        SendSelectionToUnityCommand = new RelayCommand(() => SendSelectionToUnityAsync().Forget(), () => SelectedCount > 0 && !IsSendingToUnity);
        // 行き先が無いものは押せなくする（ユーザ指示 2026-09-20・R1）。押しても黙って何も起きなかった。
        // 理由はカードのツールチップ（ItemCardViewModel の OpenBoothTip など）で出す
        OpenBoothCommand = new RelayCommand(
            parameter => OpenBooth(AsCard(parameter)),
            parameter => AsCard(parameter) is { HasBoothPage: true });
        OpenShopCommand = new RelayCommand(
            parameter => OpenShop(AsCard(parameter)),
            parameter => AsCard(parameter) is { HasShop: true });
        CopyLinkCommand = new RelayCommand(
            parameter => CopyLink(AsCard(parameter)),
            parameter => AsCard(parameter) is { HasBoothPage: true });
        EditItemCommand = new RelayCommand(parameter => EditItemAsync(AsCard(parameter)).Forget());
        RevealCommand = new RelayCommand(parameter => Reveal(AsCard(parameter)));
        CardUnpackCommand = new RelayCommand(parameter =>
        {
            if (AsCard(parameter) is { } card)
            {
                ItemFileActions.UnpackAsync(_services, card.Item).Forget();
            }
        });
        CardSendToUnityCommand = new RelayCommand(parameter => CardUnityAsync(
            AsCard(parameter), "Unityへ送る", "これを送る",
            package => ItemUnityActions.SendAsync(_services, AsCard(parameter)!.Item, package)).Forget());
        CardSendToUnityWithRecordCommand = new RelayCommand(parameter => CardUnityAsync(
            AsCard(parameter), "改変に足して送る", "これを送る",
            package => ItemUnityActions.SendWithRecordAsync(_services, AsCard(parameter)!.Item, package,
                (text, failed) => Tell("改変に足して送る", text, failed))).Forget());
        CardSelectInUnityCommand = new RelayCommand(parameter => CardUnityAsync(
            AsCard(parameter), "Unityで選択", "これを示す",
            package => ItemUnityActions.SelectAsync(AsCard(parameter)!.Item, package,
                (text, failed) => Tell("Unityで選択", text, failed))).Forget());
        HideItemCommand = new RelayCommand(parameter => HideItemAsync(AsCard(parameter)).Forget());
        ToggleFilterPanelCommand = new RelayCommand(ToggleFilterPanel);
        _isFilterPanelCollapsed = services.UiState.FilterPanelCollapsed;
        _isListMode = ItemListMode.IsList(services, "search");

        // 前回の条件を値まで戻す（ユーザ判断 2026-09-16 Q10。前は種類だけ戻していた）
        InitializeModules(services.UiState.SearchModules);

        ReloadAsync().Forget();
    }

    /// <summary>
    /// 絞り込みパネルを畳んでいるか。
    ///
    /// 畳んでも条件は外さない。**畳むのは見えなくすることで、外すことではない**ので、
    /// 畳んだ姿に効いている条件の数を出して、結果が絞られていることが分かるようにする。
    /// </summary>
    public bool IsFilterPanelCollapsed
    {
        get => _isFilterPanelCollapsed;
        private set
        {
            if (SetField(ref _isFilterPanelCollapsed, value))
            {
                FilterPane.IsCollapsed = value;
            }
        }
    }

    private PaneColumn? _filterPane;

    /// <summary>
    /// 絞り込み欄の列。開いたときの幅はドラッグで変えられる（ユーザ判断 2026-09-14）。
    /// 畳んだときの幅（34）は、開くボタンと縦書きの見出しが通る分だけで変えない。
    /// </summary>
    public PaneColumn FilterPane => _filterPane ??= new PaneColumn(_services.PaneWidths, "search.filter", collapsedWidth: 34, followsResetAll: true)
    {
        IsCollapsed = IsFilterPanelCollapsed,
    };

    public RelayCommand ToggleFilterPanelCommand { get; }

    private void ToggleFilterPanel()
    {
        IsFilterPanelCollapsed = !IsFilterPanelCollapsed;
        var collapsed = IsFilterPanelCollapsed;
        _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeUiState(
            state => state with { FilterPanelCollapsed = collapsed })).Forget();
    }

    /// <summary>画面遷移のために親を後から渡す（生成順の都合でコンストラクタでは受け取れない）。</summary>
    public void AttachMain(MainViewModel main) => _main = main;

    /// <summary>
    /// 商品を開く。**ここで検索の履歴を1件積む**（ユーザ指示）。
    ///
    /// 絞り込みは打つたびに変わるので、変わるたびに残すとゴミになる。
    /// 「探して見つけた」が一区切りで、実りのあった検索だけが残る。
    /// </summary>
    /// <summary>
    /// 裏の取得がこの商品の画像を置いた（UIスレッドで呼ばれる）。カードを描き直させる。
    /// 一覧ごと組み直さないのは、絞り込みやスクロール位置を崩さないため。
    /// </summary>
    public void NoteItemImagesSaved(string itemId)
    {
        if (_cards.TryGetValue(itemId, out var card))
        {
            card.RefreshImages();
        }
    }

    public void OpenItem(ItemCardViewModel card)
    {
        RecordHistoryAsync().Forget();
        _main?.ShowItem(card.Item);
    }

    // ---- 検索の履歴 ----

    public int TotalCount => _allItems.Count;

    /// <summary>読んである全商品から1件を引く。持っているアバターの絵に、その商品の1枚目を使うため（U18）。</summary>
    public ItemRecord? FindItem(string itemId) => _allItems.FirstOrDefault(item => item.Id == itemId);

    /// <summary>その商品のカード（右クリックを借りる画面のため・M2）。手元に無ければ null。</summary>
    public ItemCardViewModel? CardFor(string itemId) => FindItem(itemId) is { } item ? ToCard(item) : null;

    /// <summary>
    /// そのファイルを持っている（外していない）別の商品。商品ページの灰色の行で、
    /// 「この商品に戻す」を押す前に戻せないことを見せるため。読んである写しから引くので、
    /// 最後に読み直してからの紐付けは見えない（押したときに保存側で改めて確かめる）。
    /// </summary>
    /// <summary>
    /// 読んである全商品の写し（取り出した時点のもの）。ショップ一覧・ショップ画面が、開くたびに
    /// 全商品のJSONを読み直さずに数えるため（ユーザ指示 2026-09-12）。
    /// **画面のスレッドで呼ぶ。**お気に入りの切り替えがこの一覧をその場で書き換えるので、
    /// 裏で数える側には取り出した写しを渡す。新しくなる時機は検索画面と同じ（取り込み後・編集を終えた後など）
    /// </summary>
    public IReadOnlyList<ItemRecord> SnapshotItems() => _allItems.ToList();

    public ItemRecord? FindFileOwner(string hash, string exceptItemId) => _allItems.FirstOrDefault(item =>
        item.Id != exceptItemId
        && item.Local.OwnedFiles.Any(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// 所持している商品のID（所持＝ファイルかフォルダを1つ以上持つ）。
    /// 商品ページの対応アバターの札を「所持」の色にするのに使う（U25）。
    /// 検索画面は起動時に全商品を読んでいるので、札のために200件以上を読み直さない
    /// </summary>
    public IReadOnlySet<string> OwnedItemIds() => _allItems
        .Where(item => item.Local.OwnedFiles.Count > 0 || item.Local.LocalFolders.Count > 0)
        .Select(item => item.Id)
        .ToHashSet(StringComparer.Ordinal);

    public int ShopCount => _allItems
        .Select(item => item.ShopSubdomain)
        .Where(subdomain => subdomain is not null)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    /// <summary>
    /// 編集を待っている件数（ナビのバッジ）。取り込みの③がまだの商品は数えない——
    /// 数えると、押して開いた編集画面にその商品が出てこない（U8・U10）
    /// </summary>
    public int NeedsEditCount => _allItems.Count(item =>
        item.Local.UserTags.Count == 0 && _main?.IsAwaitingDetection(item.Id) != true);

    /// <summary>
    /// 一覧を下へ読み進めているか。取り込みで増えた商品を黙って入れるか、
    /// 「押すと反映」の1行にするかの分かれ目（U10）。画面の側が知らせる
    /// </summary>
    public bool IsScrolledDown { get; set; }

    /// <summary>
    /// 速く流しているかを知らせる（U12・U27）。流している間はカードの絵を小さく読み、
    /// 止まったら小さく読んだカードだけ正規の大きさで読み直させる。
    /// </summary>
    public void SetFastScrolling(bool fast)
    {
        if (_thumbnails.IsFastScrolling == fast)
        {
            return;
        }

        _thumbnails.IsFastScrolling = fast;
        if (!fast)
        {
            foreach (var card in _cards.Values)
            {
                card.NoteScrollSettled();
            }
        }
    }

    /// <summary>件数。設定「非表示にしている件数を検索結果に出す」が入っていれば、隠している数も添える。</summary>
    public string ResultSummary => _hiddenCount > 0 && _services.Settings.ShowHiddenCountInSearch
        ? $"{_matches.Count} 件（ほかに非表示 {_hiddenCount} 件）"
        : $"{_matches.Count} 件";

    public bool IsEmpty => !IsLoading && _matches.Count == 0;

    /// <summary>
    /// 結果一覧の表示幅が変わったときに呼ぶ。列数が変わったときだけ行を組み直す。
    /// </summary>
    public void SetViewportWidth(double width)
    {
        _viewportWidth = width;
        var columns = Math.Max(1, (int)((width - ResultsPadding) / CardSlotWidth));
        if (columns == _columns)
        {
            return;
        }

        _columns = columns;
        RebuildRows();
    }

    /// <summary>
    /// カードの大きさが変わった。検索画面は使い回すので、設定から戻っても一覧の幅は変わらず知らせが来ない。
    /// 覚えている幅で割り直す
    /// </summary>
    private void RelayoutForCardSize()
    {
        if (_viewportWidth > 0)
        {
            _columns = 0;
            SetViewportWidth(_viewportWidth);
        }
    }

    public async Task ReloadAsync()
    {
        IsLoading = true;
        try
        {
            var loaded = await _services.Store.Items.LoadAllAsync();

            // 並べ替えと検索用の文字列作りは、はっきり画面のスレッドの外で行う（夜の調査 2026-09-13）。
            // await の続きは画面のスレッドに戻るので、ここにそのまま書くと画面のスレッドで走り、
            // 2000件で約0.5秒、読み込むたびに画面が止まっていた（起動・取り込みや編集の後の読み直し）。
            // 作り終えてから画面のスレッドで差し替えるので、作っている途中の表を画面が読むことは無い
            var (sorted, built) = await Task.Run(() =>
            {
                // 外付けのドライブ文字が変わっていないかを読み直す（通し番号を読むので、ここで）。
                // 表は書かない：控えるのは取り込みとフォルダビューを開いた時（ユーザ判断 2026-09-14）
                try
                {
                    _services.Volumes.RefreshRemap();
                }
                catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException
                                                      or System.Text.Json.JsonException)
                {
                    Core.Diagnostics.AppLog.Error("検索：ドライブ文字の読み替えを確かめる", exception);
                }

                var sortedItems = loaded.Items
                    .OrderByDescending(item => item.Local.AcquiredAt ?? DateOnly.MinValue)
                    .ThenBy(item => item.DisplayName, StringComparer.CurrentCulture)
                    .ToList();

                // 検索対象の文字列はここで作る。正規化は全商品の説明文を畳むので、
                // UIスレッドに乗せると読み込みのたびに画面が固まる
                var haystacks = sortedItems.ToDictionary(
                    item => item.Id,
                    item => Core.Services.SearchText.Build(item, _services.KanjiReadings),
                    StringComparer.Ordinal);

                return (sortedItems, haystacks);
            });

            _allItems = sorted;
            _haystacks = built;
            _favoriteShops = Core.Services.ShopNotes.FavoriteKeys(_services.Store.ShopNotes.Load());

            RunOnUiThread(() =>
            {
                // カードは絞り込みのたびには作り直さず、itemごとに1つを使い回す。
                // 作り直すと、件数に比例した生成コストがキー入力のたびに掛かる。
                _cards.Clear();
                foreach (var item in _allItems)
                {
                    var card = ToCard(item);
                    card.SelectionChanged += OnCardSelectionChanged;
                    _cards[item.Id] = card;
                }

                OnCardSelectionChanged();

                // カテゴリ（自分で入れた分類を含む）・タグ・アバターなどの候補は、全商品から組み直す
                BuildFacets();
                ApplyFilters();
                OnPropertyChanged(nameof(TotalCount));
                OnPropertyChanged(nameof(ShopCount));
                OnPropertyChanged(nameof(NeedsEditCount));

                // 全件の読み込みと検索対象の文字列作りが出したゴミを、ここでOSへ返させる（#71）
                BoothAssetManager.App.Services.MemoryTrim.Request();
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

}
