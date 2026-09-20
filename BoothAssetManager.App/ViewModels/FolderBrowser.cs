using System.Collections.ObjectModel;
using System.Globalization;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>右の一覧の1行。子フォルダのカードと商品のカードを混ぜて並べる（行に切るのは検索画面と同じ理由：仮想化）。</summary>
public sealed class FolderBrowserRow
{
    public ObservableCollection<object> Cards { get; } = [];
}

/// <summary>
/// 子フォルダのカード。商品のカードと同じ大きさにする（ユーザ指示 2026-09-14「もしかしたら変わるかもしれないが、まずは商品カードサイズで」）。
/// 押すと木の中でそのフォルダへ移る。
/// </summary>
public sealed class FolderBrowserFolderCard
{
    /// <summary>木の行の鍵（1本道の段を畳んだ先のフォルダで作る。木と同じ決め方）。</summary>
    public required string Key { get; init; }

    public required string Path { get; init; }

    public required string Name { get; init; }

    public required int ItemCount { get; init; }

    public required int UnresolvedCount { get; init; }

    /// <summary>取り外したドライブ。薄く出す。</summary>
    public bool IsDim { get; init; }

    /// <summary>探しているときの、今のフォルダから見た場所（どこにあるフォルダかが分かるように）。</summary>
    public string SubText { get; init; } = string.Empty;

    public bool HasSubText => SubText.Length > 0;

    public string CountText => UnresolvedCount > 0 && FolderViewModel.ShowsUnresolvedNow
        ? $"商品 {ItemCount}・未確定 {UnresolvedCount}"
        : $"商品 {ItemCount}";

    public bool HasUnresolved => UnresolvedCount > 0;

    /// <summary>未確定を出していないときの小さな札。件数は出さず、あることだけ分かるように（ユーザ指示 2026-09-15）。</summary>
    public bool HasUnresolvedMark => UnresolvedCount > 0 && !FolderViewModel.ShowsUnresolvedNow;

    internal FolderViewNode? Node { get; init; }

    // ---- リストの行で使う（商品の行と同じ列に並べる・ユーザ指示 2026-09-14） ----

    /// <summary>フォルダの行。絵の代わりにフォルダの印、ショップの列に場所、札の列に数を出す。</summary>
    public bool IsFolder => true;
}

/// <summary>
/// 右に出すフォルダ（ユーザ指示 2026-09-14）。検索画面と同じカードで商品を並べ、先頭に子フォルダのカードを置く。
/// 文字で探すことはできるが、絞り込みは付けない。
///
/// 並べるのは**そのフォルダの直下**（エクスプローラと同じ。深い所へは子フォルダのカードから降りる）。
/// 文字で探しているときは、**この下の全部**から探す（どこにあったか覚えていないときに探せるように）。
/// 非表示・R-18 を出さない設定は、検索画面・ショップの画面と同じ決め事で外す。
/// </summary>
public sealed class FolderViewDetail : ViewModelBase, IItemCardHost
{
    /// <summary>カード1枚ぶんの幅（カード228＋間14）。検索画面と同じ。</summary>
    private static double CardSlotWidth => CardMetrics.SlotWidth;

    /// <summary>一覧の左右の余白（18×2）と縦のスクロールバーのぶん。</summary>
    private const double ListChrome = 36 + 18;

    private readonly MainViewModel _main;
    private readonly AppServiceContainer _services;
    private readonly IReadOnlyList<FolderBrowserFolderCard> _childFolders;
    private readonly IReadOnlyList<FolderBrowserFolderCard> _allFolders;
    private readonly List<ItemRecord> _directItems;
    private readonly List<ItemRecord> _allItems;
    private readonly Dictionary<string, ItemCardViewModel> _cards = new(StringComparer.Ordinal);
    private string _query = string.Empty;
    private int _columns = 1;
    private int _excluded;

    internal FolderViewDetail(
        FolderViewModel owner,
        MainViewModel main,
        AppServiceContainer services,
        IReadOnlyList<FolderBrowserFolderCard> childFolders,
        IReadOnlyList<FolderBrowserFolderCard> allFolders,
        IEnumerable<ItemRecord> directItems,
        IEnumerable<ItemRecord> allItems)
    {
        Owner = owner;
        _main = main;
        _services = services;
        _childFolders = childFolders;
        _allFolders = allFolders;
        _directItems = directItems.DistinctBy(item => item.Id).OrderBy(item => item.DisplayName, NaturalComparer.Instance).ToList();
        _allItems = allItems.DistinctBy(item => item.Id).OrderBy(item => item.DisplayName, NaturalComparer.Instance).ToList();

        _isListMode = ItemListMode.IsList(services, "folder");
        OpenFolderCommand = new RelayCommand(parameter => Owner.OpenFolder(parameter as FolderBrowserFolderCard));
        HideItemCommand = new RelayCommand(parameter =>
        {
            if (parameter is ItemCardViewModel card)
            {
                // 書くのは検索画面と同じ命令。この一覧からもその場で外す（外したのに残って見えないように）
                _main.Search.HideItemCommand.Execute(card);
                _directItems.RemoveAll(item => item.Id == card.Item.Id);
                _allItems.RemoveAll(item => item.Id == card.Item.Id);
                Rebuild();
            }
        });
    }

    /// <summary>フォルダビュー。上の操作（検索で絞る・エクスプローラ・取り込み元・監視・未確定）はそちらが持つ。</summary>
    public FolderViewModel Owner { get; }

    public required string Path { get; init; }

    public required string Title { get; init; }

    /// <summary>この下の商品の数（木の行の「商品 n」と同じ数え方）。</summary>
    public required int ItemCount { get; init; }

    public required IReadOnlyList<UnresolvedFile> Unresolved { get; init; }

    public bool IsOffline { get; init; }

    private bool _isWatched;

    /// <summary>監視対象か。切り替えたらその場で書き換える（右を作り直すと、探していた語や見ていた所が戻る）。</summary>
    public bool IsWatched
    {
        get => _isWatched;
        set
        {
            if (SetField(ref _isWatched, value))
            {
                OnPropertyChanged(nameof(WatchStateText));
                OnPropertyChanged(nameof(WatchButtonText));
            }
        }
    }

    public string WatchStateText => IsWatched ? "監視中" : "監視していません";

    public string WatchButtonText => IsWatched ? "監視をやめる" : "監視対象にする";

    public string CountText => Unresolved.Count > 0 && FolderViewModel.ShowsUnresolvedNow
        ? $"この下に 商品 {ItemCount} 件・未確定 {Unresolved.Count} 件"
        : $"この下に 商品 {ItemCount} 件";

    /// <summary>未確定の案内と「管理対象から除外する」を出すか。左の「未確定」を切っているときは出さない（ユーザ指示 2026-09-15）。</summary>
    public bool HasUnresolved => Unresolved.Count > 0 && FolderViewModel.ShowsUnresolvedNow;

    /// <summary>未確定を出していないときの小さな札（あることだけ分かるように）。</summary>
    public bool HasUnresolvedMark => Unresolved.Count > 0 && !FolderViewModel.ShowsUnresolvedNow;

    /// <summary>左の「未確定」を切り替えたとき。件数の出し方・案内・子フォルダのカードの件数を出し直す。</summary>
    internal void RefreshUnresolvedShown()
    {
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasUnresolved));
        OnPropertyChanged(nameof(HasUnresolvedMark));

        // 行を作り直すと、子フォルダのカードの結び付けも読み直される
        Rebuild();
    }

    public string UnresolvedText => $"この下に未確定のファイルが {Unresolved.Count} 件あります。確定・フォルダの登録は「未確定として開く」から、"
        + "要らない物は「管理対象から除外する」で片付けられます（ファイル自体は消しません）。";

    public string ExcludeText => $"この下の未確定 {Unresolved.Count} 件を管理対象から除外する";

    // ---- 文字で探す（絞り込みは付けない・ユーザ指示） ----

    public string Query
    {
        get => _query;
        set
        {
            if (SetField(ref _query, value))
            {
                OnPropertyChanged(nameof(HasQuery));
                Rebuild();
            }
        }
    }

    public bool HasQuery => Query.Length > 0;

    public ObservableCollection<FolderBrowserRow> Rows { get; } = [];

    // ---- カードかリストか（ユーザ指示 2026-09-14。検索画面と同じ作り。どちらで出すかはフォルダビューとして覚える） ----

    private bool _isListMode;
    private ItemListColumns? _listColumns;
    private IReadOnlyList<object> _listItems = [];

    public bool IsListMode
    {
        get => _isListMode;
        set
        {
            if (SetField(ref _isListMode, value))
            {
                OnPropertyChanged(nameof(IsCardMode));
                ItemListMode.Save(_services, "folder", value);
            }
        }
    }

    public bool IsCardMode
    {
        get => !_isListMode;
        set => IsListMode = !value;
    }

    /// <summary>切り替えを押したときだけ変える（点いているかは読むだけ。検索画面と同じ）。</summary>
    public RelayCommand ShowCardsCommand => _showCards ??= new RelayCommand(() => IsListMode = false);

    public RelayCommand ShowListCommand => _showList ??= new RelayCommand(() => IsListMode = true);

    private RelayCommand? _showCards;
    private RelayCommand? _showList;

    public ItemListColumns ListColumns => _listColumns ??= new ItemListColumns(_services.PaneWidths, "folder", hasSelect: true, shopHeader: "ショップ・場所");

    /// <summary>リストに並べる物（先に子フォルダ、続けて商品。カードと同じ並び）。</summary>
    public IReadOnlyList<object> ListItems => _listItems;

    public bool IsEmpty { get; private set; }

    public string EmptyText { get; private set; } = string.Empty;

    /// <summary>外している商品があれば、そう書く（数えた商品が見当たらないと、壊れて見える）。</summary>
    public string ExcludedText => _excluded > 0
        ? $"非表示・R-18 を出さない設定で {_excluded} 件を出していません（設定から変えられます）。"
        : string.Empty;

    public bool HasExcluded => _excluded > 0;

    public RelayCommand OpenFolderCommand { get; }

    /// <summary>一覧の幅から列数を決める（WPFには仮想化するWrapPanelが無いので、行に切って並べる）。</summary>
    public void SetViewportWidth(double width)
    {
        var columns = Math.Max(1, (int)((width - ListChrome) / CardSlotWidth));
        if (columns == _columns && Rows.Count > 0)
        {
            return;
        }

        _columns = columns;
        Rebuild();
    }

    internal void Rebuild()
    {
        var needle = Query.Trim();
        var folders = needle.Length == 0
            ? _childFolders
            : _allFolders.Where(folder => Hits(folder.Name, needle)).ToList();
        var candidates = needle.Length == 0
            ? _directItems
            : _allItems.Where(item => ItemHits(item, needle)).ToList();

        // 検索画面・ショップの画面と同じ決め事で外す（ShopService の見える条件と同じ）
        var visible = candidates.Where(item => !item.Local.IsHidden && (_services.Settings.ShowAdult || !item.Booth.IsAdult)).ToList();
        _excluded = candidates.Count - visible.Count;

        var cards = folders.Cast<object>().Concat(visible.Select(CardFor)).ToList();
        _listItems = cards;
        OnPropertyChanged(nameof(ListItems));
        Rows.Clear();
        for (var start = 0; start < cards.Count; start += _columns)
        {
            var row = new FolderBrowserRow();
            foreach (var card in cards.Skip(start).Take(_columns))
            {
                row.Cards.Add(card);
            }

            Rows.Add(row);
        }

        IsEmpty = cards.Count == 0;
        EmptyText = needle.Length > 0
            ? $"この下に「{needle}」に当てはまる商品・フォルダはありません。"
            : HasUnresolved
                ? "このフォルダの直下には、管理している商品も子フォルダもありません。未確定のファイルは上の「未確定として開く」から片付けられます。"
                : "このフォルダの直下には、管理している商品も子フォルダもありません。";
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(ExcludedText));
        OnPropertyChanged(nameof(HasExcluded));
    }

    /// <summary>カードは探し直しても作り直さない（なぞって選んだ絵や星の状態を残す）。</summary>
    private ItemCardViewModel CardFor(ItemRecord item)
    {
        if (!_cards.TryGetValue(item.Id, out var card))
        {
            card = _main.Search.CreateCard(item);
            card.SelectionChanged += OnCardSelectionChanged;
            card.IsSelectionMode = HasSelection;
            _cards[item.Id] = card;
        }

        return card;
    }

    /// <summary>商品名・ショップ名・この商品のファイル名で探す。大文字小文字・かなの種類・全角半角を区別しない（画面内検索と同じ）。</summary>
    private static bool ItemHits(ItemRecord item, string needle)
        => Hits(item.DisplayName, needle)
            || Hits(item.Booth.Shop?.Name, needle)
            || item.Local.OwnedFiles.SelectMany(file => file.Paths).Any(path => Hits(System.IO.Path.GetFileName(path), needle));

    private static bool Hits(string? text, string needle)
        => text is not null && CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, needle, Views.FindInPage.Options) >= 0;

    // ---- まとめて操作（検索画面と同じ・ユーザ指示 2026-09-14：フォルダも検索と同等の発見手段なので同等にする） ----

    private bool _isSendingToUnity;
    private string _unityQueueText = string.Empty;

    public RelayCommand SelectAllCommand => _selectAll ??= new RelayCommand(() =>
    {
        foreach (var card in _listItems.OfType<ItemCardViewModel>())
        {
            card.IsSelected = true;
        }
    });

    public RelayCommand ClearSelectionCommand => _clearSelection ??= new RelayCommand(ClearSelection);

    public RelayCommand SendSelectionToEditCommand => _sendToEdit ??= new RelayCommand(() =>
    {
        var ids = SelectedCards().Select(card => card.Item.Id).ToList();
        if (ids.Count > 0)
        {
            ClearSelection();
            _main.ShowEditAsync(ids).Forget();
        }
    });

    public RelayCommand AddSelectionToFavoritesCommand => _addToFavorites ??= new RelayCommand(() => AddSelectionToFavoritesAsync().Forget());

    public RelayCommand AddSelectionToModificationCommand => _addToModification ??= new RelayCommand(
        () => ItemSelectionActions.AddToModificationAsync(_services, SelectedCards()).Forget());

    public RelayCommand SendSelectionToUnityCommand => _sendToUnity ??= new RelayCommand(
        () => ItemSelectionActions.SendToUnityAsync(
            _services, SelectedCards(), sending => IsSendingToUnity = sending, text => UnityQueueText = text).Forget(),
        () => !IsSendingToUnity);

    private RelayCommand? _selectAll;
    private RelayCommand? _clearSelection;
    private RelayCommand? _sendToEdit;
    private RelayCommand? _addToFavorites;
    private RelayCommand? _addToModification;
    private RelayCommand? _sendToUnity;

    public bool IsSendingToUnity
    {
        get => _isSendingToUnity;
        private set
        {
            if (SetField(ref _isSendingToUnity, value))
            {
                OnPropertyChanged(nameof(ShowsSelectionBar));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>帯を出すか。選んでいる間と、Unity へ送っている間（E10：1件でも進み具合を出す）。</summary>
    public bool ShowsSelectionBar => HasSelection || IsSendingToUnity;

    /// <summary>送るのをやめる（E7）。</summary>
    public RelayCommand StopUnityCommand => _stopUnity ??= new RelayCommand(Services.UnityImportQueue.Stop);

    private RelayCommand? _stopUnity;

    public string UnityQueueText
    {
        get => _unityQueueText;
        private set
        {
            if (SetField(ref _unityQueueText, value))
            {
                OnPropertyChanged(nameof(HasUnityQueueText));
            }
        }
    }

    public bool HasUnityQueueText => UnityQueueText.Length > 0;

    public int SelectedCount => _cards.Values.Count(card => card.IsSelected);

    public bool HasSelection => SelectedCount > 0;

    public string SelectionText => $"{SelectedCount} 件を選択中";

    /// <summary>選んだカード。表示中の並びを先に、探し直して見えなくなった物を後に（検索画面と同じ）。</summary>
    private List<ItemCardViewModel> SelectedCards()
    {
        var cards = _listItems.OfType<ItemCardViewModel>().Where(card => card.IsSelected).ToList();
        cards.AddRange(_cards.Values.Where(card => card.IsSelected && !cards.Contains(card)));
        return cards;
    }

    public void ClearSelection()
    {
        foreach (var card in _cards.Values.Where(card => card.IsSelected))
        {
            card.IsSelected = false;
        }
    }

    /// <summary>選んだ物に星を付ける。付いている物はそのまま（外す操作ではない）。</summary>
    private async Task AddSelectionToFavoritesAsync()
    {
        foreach (var card in SelectedCards().Where(card => !card.IsFavorite))
        {
            await ToggleFavoriteAsync(card);
        }
    }

    /// <summary>1件でも選ぶと、カード全体が選択の的になる（検索画面と同じ。中を見るのは専用のボタンへ）。</summary>
    private void OnCardSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowsSelectionBar));
        OnPropertyChanged(nameof(SelectionText));

        var selecting = HasSelection;
        foreach (var card in _cards.Values)
        {
            card.IsSelectionMode = selecting;
        }

        RelayCommand.RaiseCanExecuteChanged();
    }

    // ---- カードの操作（検索画面と同じ・IItemCardHost） ----

    public void OpenItem(ItemCardViewModel card) => _main.ShowItem(card.Item);

    public void OpenBooth(ItemCardViewModel? card) => _main.Search.OpenBooth(card);

    public Task ToggleFavoriteAsync(ItemCardViewModel card) => _main.Search.ToggleFavoriteAsync(card);

    // 右クリックのメニューはカードの Tag（＝この画面）から同じ名前で引く。中身は検索画面の物をそのまま使う
    public RelayCommand OpenBoothCommand => _main.Search.OpenBoothCommand;

    public RelayCommand OpenShopCommand => _main.Search.OpenShopCommand;

    public RelayCommand CopyLinkCommand => _main.Search.CopyLinkCommand;

    public RelayCommand EditItemCommand => _main.Search.EditItemCommand;

    public RelayCommand RevealCommand => _main.Search.RevealCommand;


    // 右クリックの「開く」「Unity」は検索画面と同じ命令を借りる（ユーザ指示 2026-09-19）

    public RelayCommand CardUnpackCommand => _main.Search.CardUnpackCommand;


    public RelayCommand CardSendToUnityCommand => _main.Search.CardSendToUnityCommand;


    public RelayCommand CardSendToUnityWithRecordCommand => _main.Search.CardSendToUnityWithRecordCommand;


    public RelayCommand CardSelectInUnityCommand => _main.Search.CardSelectInUnityCommand;

    public RelayCommand HideItemCommand { get; }
}
