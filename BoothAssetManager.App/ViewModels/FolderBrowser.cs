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

    public string CountText => UnresolvedCount > 0 ? $"商品 {ItemCount}・未確定 {UnresolvedCount}" : $"商品 {ItemCount}";

    public bool HasUnresolved => UnresolvedCount > 0;

    internal FolderViewNode? Node { get; init; }
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
    private const double CardSlotWidth = 242;

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

    public string CountText => Unresolved.Count > 0
        ? $"この下に 商品 {ItemCount} 件・未確定 {Unresolved.Count} 件"
        : $"この下に 商品 {ItemCount} 件";

    public bool HasUnresolved => Unresolved.Count > 0;

    public string UnresolvedText => $"この下に未確定のファイルが {Unresolved.Count} 件あります。確定・フォルダの登録は「未確定として開く」から、"
        + "要らない物は「管理から外す」で片付けられます（ファイル自体は消しません）。";

    public string ExcludeText => $"この下の未確定 {Unresolved.Count} 件を管理から外す";

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
            : Unresolved.Count > 0
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

    public RelayCommand HideItemCommand { get; }
}
