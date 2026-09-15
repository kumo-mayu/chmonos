using System.IO;
using System.Net.Http;
using System.Collections.ObjectModel;
using System.Diagnostics;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// ショップ1件の画面。所持している商品と、持っていないが情報だけある商品を並べる。
///
/// BOOTHのショップにある全商品ではない。取りに行っていないものは存在自体を知らないので、
/// その旨は画面に書いておく（件数を全商品数と誤解されると数字の意味が変わる）。
/// </summary>
public sealed class ShopViewModel : ViewModelBase, IItemCardHost
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;
    private readonly ThumbnailLoader _thumbnails;

    private bool _ownedOnly;
    private List<ItemCardViewModel> _all = [];

    public ShopViewModel(
        ShopSummary shop,
        AppServiceContainer services,
        MainViewModel main,
        ThumbnailLoader thumbnails)
    {
        Shop = shop;
        _services = services;
        _main = main;
        _thumbnails = thumbnails;

        // 戻るは画面の履歴を遡る（U23）
        BackCommand = new RelayCommand(main.GoBack);
        OpenShopPageCommand = new RelayCommand(OpenBooth, () => !string.IsNullOrEmpty(Shop.Url));

        // カードかリストか（ユーザ指示 2026-09-15：検索画面と同じ見方に）。どちらで出すかはショップ画面として覚える
        _isListMode = ItemListMode.IsList(services, "shop");
        HideItemCommand = new RelayCommand(parameter =>
        {
            if (parameter is ItemCardViewModel card)
            {
                // 書くのは検索画面と同じ命令。この一覧からもその場で外す（外したのに残って見えないように）
                _main.Search.HideItemCommand.Execute(card);
                _all.Remove(card);
                Rebuild();
            }
        });

        // ショップ画面に絞り込みを作り直さず、検索の絞り込みをそのまま使う（#55・ユーザ判断）
        ShowInSearchCommand = new RelayCommand(() => main.ShowItemsOfShop(Shop.Subdomain, Shop.Name));
        RefreshImagesCommand = new RelayCommand(() => RefreshImagesAsync().Forget(), () => !IsRefreshingImages);

        // 有無が分からない店だけ、開いた瞬間から場所を空けて待つ。
        // 確かめ直す時期が来た店も「分からない」に含まれる（結果が変わり得るため）
        _reservedBannerArea = shop.BannerState == ShopBannerState.Unknown;
        _isBannerPending = _reservedBannerArea;

        _icon = shop.IconPath is null ? null : thumbnails.Load(shop.IconPath);

        ReloadAsync().Forget();
    }

    public ShopSummary Shop { get; }

    /// <summary>
    /// 行に切った一覧。見えている行のカードだけが作られ、絵を裏で読む（検索画面と同じ作り）。
    /// 以前は WrapPanel に全商品のカードを並べていた。
    /// </summary>
    public ObservableCollection<CardRow> Rows { get; } = [];

    /// <summary>絞り込んだ後の商品（「所持しているものだけ」）。</summary>
    private List<ItemCardViewModel> _matches = [];

    private int _columns = 1;

    /// <summary>カード1枚ぶんの幅（カード228＋間14）。ShopView.xaml のカードの Width と Margin に合わせる。</summary>
    private const double CardStride = 242;

    /// <summary>一覧の左右の余白（24×2）と縦のスクロールバーのぶん。</summary>
    private const double ListChrome = 48 + 18;

    /// <summary>一覧の幅から列数を決める（WPFには仮想化するWrapPanelが無いので、行に切って並べる）。</summary>
    public void SetViewportWidth(double width)
    {
        var columns = Math.Max(1, (int)((width - ListChrome) / CardStride));
        if (columns == _columns)
        {
            return;
        }

        _columns = columns;
        FillRows();
    }

    /// <summary>行に切り直す。1店の商品は多くても数百なので、丸ごと作り直す。</summary>
    private void FillRows()
    {
        Rows.Clear();
        for (var start = 0; start < _matches.Count; start += _columns)
        {
            var row = new CardRow();
            foreach (var card in _matches.Skip(start).Take(_columns))
            {
                row.Cards.Add(card);
            }

            Rows.Add(row);
        }
    }

    public RelayCommand BackCommand { get; }

    /// <summary>どこから来たかで戻り先を変える。来た道と違う場所へ戻されると迷子になる。</summary>
    /// <summary>戻るの文言。行き先は画面の履歴の直前の画面（U23）。</summary>
    public string BackText => _main.BackButtonText;

    /// <summary>
    /// BOOTH のショップページを開く。カードの右クリックの「BOOTHで開く」（商品ページ）は同じ画面から
    /// OpenBoothCommand の名前で引くので、名前を分けてある
    /// </summary>
    public RelayCommand OpenShopPageCommand { get; }

    /// <summary>このショップの商品で絞った検索画面へ移る。</summary>
    public RelayCommand ShowInSearchCommand { get; }

    /// <summary>
    /// このショップの画像を今すぐ取り直す。
    /// 外部で更新を知ったときに、次の確認時期を待たずに済むように。
    /// </summary>
    public RelayCommand RefreshImagesCommand { get; }

    private bool _isRefreshingImages;
    private string _refreshStatus = string.Empty;

    public bool IsRefreshingImages
    {
        get => _isRefreshingImages;
        private set
        {
            if (SetField(ref _isRefreshingImages, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string RefreshStatus
    {
        get => _refreshStatus;
        private set
        {
            if (SetField(ref _refreshStatus, value))
            {
                OnPropertyChanged(nameof(HasRefreshStatus));
            }
        }
    }

    public bool HasRefreshStatus => RefreshStatus.Length > 0;

    private async Task RefreshImagesAsync()
    {
        IsRefreshingImages = true;
        RefreshStatus = "ショップの画像を取り直しています…";

        try
        {
            var refreshed = await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.RefreshShopImages(Shop.Subdomain));
            var result = (refreshed as Core.Commands.CommandResult.ShopImagesRefreshed)?.Result ?? new ShopImageRefresh();

            RunOnUiThread(() =>
            {
                if (result.BannerPath is not null)
                {
                    // バナーの置き場は固定なので、覚えている絵を捨てないと古いまま出る
                    _thumbnails.Forget(result.BannerPath);
                    Banner = _thumbnails.Load(result.BannerPath);
                }

                if (result.IconPath is not null)
                {
                    _thumbnails.Forget(result.IconPath);
                    Icon = _thumbnails.Load(result.IconPath);
                }

                IsBannerPending = false;
                RefreshStatus = Describe(result);
            });
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            RunOnUiThread(() => RefreshStatus = "取得できませんでした。時間をおいて試してください。");
        }
        finally
        {
            IsRefreshingImages = false;
        }
    }

    /// <summary>何が変わったかをそのまま書く。「更新しました」とだけ出すと確かめようがない。</summary>
    private static string Describe(ShopImageRefresh result)
    {
        if (result.Failed)
        {
            return "ショップページを読めませんでした。時間をおいて試してください。";
        }

        var changed = new List<string>();
        if (result.IconUpdated)
        {
            changed.Add("アイコン");
        }

        if (result.BannerUpdated)
        {
            changed.Add("バナー");
        }

        if (changed.Count > 0)
        {
            return $"{string.Join("と", changed)}を取り直しました。";
        }

        return result.BannerAbsent
            ? "変わっていませんでした（このショップはバナーを設定していません）。"
            : "変わっていませんでした。";
    }

    public string Name => Shop.Name;

    /// <summary>手元だけのショップには .booth.pm を付けない（実在しないURLになる）。</summary>
    public string DomainText => Core.Models.LocalShopKey.IsLocal(Shop.Subdomain)
        ? "BOOTHのショップに結び付いていません"
        : $"{Shop.Subdomain}.booth.pm";

    public string Initial => Shop.Name.Length == 0 ? "?" : Shop.Name[..1];

    private System.Windows.Media.Imaging.BitmapSource? _icon;

    /// <summary>落としてあるアイコン。まだ無ければ頭文字のタイルで代える。</summary>
    public System.Windows.Media.Imaging.BitmapSource? Icon
    {
        get => _icon;
        private set
        {
            if (SetField(ref _icon, value))
            {
                OnPropertyChanged(nameof(HasIcon));
                OnPropertyChanged(nameof(ShowInitial));
            }
        }
    }

    public bool HasIcon => Icon is not null;

    public bool ShowInitial => Icon is null;

    private System.Windows.Media.Imaging.BitmapSource? _banner;
    private bool _isBannerPending;

    /// <summary>この画面を開いた時点で場所を空けたか。開いた後は変えない（ずれるので）。</summary>
    private readonly bool _reservedBannerArea;

    /// <summary>
    /// ショップのバナー。URLがHTMLにしか無いので、この画面を開いたときに取りに行く。
    /// 置いていないショップもあるので、無ければ帯ごと出さない。
    /// </summary>
    public System.Windows.Media.Imaging.BitmapSource? Banner
    {
        get => _banner;
        private set
        {
            if (SetField(ref _banner, value))
            {
                OnPropertyChanged(nameof(HasBanner));
                OnPropertyChanged(nameof(ShowBannerArea));
            }
        }
    }

    public bool HasBanner => Banner is not null;

    /// <summary>
    /// バナーの有無がまだ分からず、取りに行っている最中か。
    /// 出す文言を切り替えるためだけに使う（場所を空けるかどうかは <see cref="ShowBannerArea"/>）。
    /// </summary>
    public bool IsBannerPending
    {
        get => _isBannerPending;
        private set
        {
            if (SetField(ref _isBannerPending, value))
            {
                OnPropertyChanged(nameof(BannerPlaceholderText));
            }
        }
    }

    /// <summary>
    /// バナーの場所を空けるか。
    ///
    /// 開いた時点で有無が分からなければ空け、その後の結果では畳まない。
    /// 「無かった」と分かった時点で畳むと、結局そこで下へずれてしまう。
    /// 次に開くときは「無し」と分かっているので、最初から空けずに開く。
    /// </summary>
    public bool ShowBannerArea => HasBanner || _reservedBannerArea;

    /// <summary>場所を空けている間に出す文言。何を待っているのか、何が無かったのかを書く。</summary>
    public string BannerPlaceholderText => IsBannerPending
        ? "バナーを確認しています…"
        : "このショップはバナーを設定していません";

    public string OwnedText => $"{Shop.OwnedCount}";

    public string SpentText => $"¥{Shop.SpentYen:N0}";

    public string LastAcquiredText => Shop.LastAcquiredAt?.ToString("yyyy-MM-dd") ?? "—";

    public string SizeText => Core.Models.DisplayText.Size(_totalBytes);

    /// <summary>所持しているものだけに絞る。既定は全部（情報だけのものも見せる）。</summary>
    public bool OwnedOnly
    {
        get => _ownedOnly;
        set
        {
            if (SetField(ref _ownedOnly, value))
            {
                Rebuild();
            }
        }
    }

    /// <summary>
    /// カードを開く。検索画面と同じく、ビューからカードを渡してもらう。
    /// 戻り先はこのショップにする（検索へ戻されると、見ていた場所を失う）。
    /// </summary>
    public void OpenItem(ItemCardViewModel card)
        => _main.ShowItem(card.Item);

    public void OpenBooth(ItemCardViewModel? card) => _main.Search.OpenBooth(card);

    public Task ToggleFavoriteAsync(ItemCardViewModel card) => _main.Search.ToggleFavoriteAsync(card);

    // 右クリックのメニューはカードの Tag（＝この画面）から同じ名前で引く。中身は検索画面の物をそのまま使う
    public RelayCommand OpenBoothCommand => _main.Search.OpenBoothCommand;

    public RelayCommand OpenShopCommand => _main.Search.OpenShopCommand;

    public RelayCommand CopyLinkCommand => _main.Search.CopyLinkCommand;

    public RelayCommand EditItemCommand => _main.Search.EditItemCommand;

    public RelayCommand RevealCommand => _main.Search.RevealCommand;

    public RelayCommand HideItemCommand { get; }

    // ---- カードかリストか（検索画面・フォルダビューと同じ作り） ----

    private bool _isListMode;
    private ItemListColumns? _listColumns;
    private IReadOnlyList<object> _listItems = [];
    private RelayCommand? _showCards;
    private RelayCommand? _showList;

    public bool IsListMode
    {
        get => _isListMode;
        set
        {
            if (SetField(ref _isListMode, value))
            {
                OnPropertyChanged(nameof(IsCardMode));
                ItemListMode.Save(_services, "shop", value);
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

    /// <summary>ショップの列には入手日を出す（カードの2行目と同じ。店名は全部同じで意味が無い）。</summary>
    public ItemListColumns ListColumns => _listColumns ??= new ItemListColumns(_services.PaneWidths, "shop", hasSelect: false, shopHeader: "入手日");

    public IReadOnlyList<object> ListItems => _listItems;

    public string CountText => _all.Count == _matches.Count
        ? $"{_matches.Count} 件"
        : $"{_matches.Count} 件 / 全 {_all.Count} 件";

    public bool IsEmpty => _matches.Count == 0;

    /// <summary>件数から外している数（非表示・R-18を出さない設定）。</summary>
    private (int Hidden, int Adult) _excluded;

    /// <summary>
    /// 空のときに、なぜ無いのかと戻し方を言う（動線の点検 D8）。以前は「表示する商品がありません」だけで、
    /// 全部非表示にしたのか、R-18を出さない設定なのか、絞っているのかが分からなかった
    /// </summary>
    public string EmptyReasonText
    {
        get
        {
            var lines = new List<string>();
            if (_ownedOnly && _all.Count > 0)
            {
                lines.Add($"「持っているものだけ」を外すと {_all.Count} 件出ます。");
            }

            if (_excluded.Hidden > 0)
            {
                lines.Add($"非表示にしている商品が {_excluded.Hidden} 件あります。設定の「非表示にした商品」から戻せます。");
            }

            if (_excluded.Adult > 0)
            {
                lines.Add($"R-18 の商品が {_excluded.Adult} 件あります。設定の「R-18 の商品を表示する」をオンにすると出ます。");
            }

            return lines.Count > 0 ? string.Join("\n", lines) : "このショップの商品は、まだ取り込まれていません。";
        }
    }

    public bool HasExcluded => _excluded.Hidden > 0 || _excluded.Adult > 0;

    public RelayCommand ShowSettingsCommand => _main.ShowSettingsCommand;

    private long _totalBytes;

    public async Task ReloadAsync()
    {
        // 全商品のJSONは読み直さず、検索画面が起動時に読んだ写しから引く（ユーザ指示 2026-09-12）。
        // 写しは画面のスレッドで取り出し、引くのは裏で
        var items = _main.Search.SnapshotItems();
        var (entries, excluded) = await Task.Run(() =>
            (_services.Shops.ItemsOf(items, Shop.Subdomain), _services.Shops.ExcludedOf(items, Shop.Subdomain)));

        RunOnUiThread(() =>
        {
            _excluded = excluded;
            _totalBytes = entries.Sum(entry => entry.SizeBytes);

            _all = entries.Select(entry => new ItemCardViewModel(
                entry.Item,
                _thumbnails,
                _services.Paths.ItemImagesDir(entry.Item.Id),
                _services.Settings.ThumbnailRole)
            {
                Name = entry.Item.DisplayName,
                // カードの2行目は入手日にする。ショップ画面では店名が全部同じで意味が無い
                ShopName = AcquiredText(entry),
                SizeText = entry.IsOwned ? Core.Models.DisplayText.Size(entry.SizeBytes) : "未取得",
                IsOwned = entry.IsOwned,
                NeedsEdit = entry.Item.Local.UserTags.Count == 0,
                UserTagText = string.Join(" / ", entry.Item.Local.UserTags.Select(tag => tag.Top)),
            }).ToList();

            Rebuild();
            OnPropertyChanged(nameof(SizeText));

            if (Shop.BannerPath is not null)
            {
                Banner = _thumbnails.Load(Shop.BannerPath);
            }
        });

        await EnsureBannerAsync();
    }

    /// <summary>
    /// バナーを用意する。既に持っていれば何もしない。
    ///
    /// URLはショップページのHTMLにしか無く、ファイル名は乱数なので導けない。
    /// 一覧で全店ぶんを先読みすると通信が重くなるので、開いた店だけ取りに行く。
    /// </summary>
    private async Task EnsureBannerAsync()
    {
        if (Shop.BannerPath is not null)
        {
            return;
        }

        // 「置いていない」と分かっている店は、次に確かめる時期が来るまで場所を空けない。
        // 期限が来て見に行った結果バナーが出てきたときだけ、そこで初めて現れる
        if (Shop.BannerState == ShopBannerState.Absent)
        {
            IsBannerPending = false;
        }

        try
        {
            var path = (await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.EnsureShopBanner(Shop.Subdomain))
                as Core.Commands.CommandResult.ShopBannerEnsured)?.Path;

            RunOnUiThread(() =>
            {
                if (path is not null)
                {
                    Banner = _thumbnails.Load(path);
                }

                // 結果が出たので待ちの表示はやめる。無かった場合も場所は畳まない
                // （ここで畳むと、結局そこで下へずれる）。次に開くときは空けずに開く
                IsBannerPending = false;
            });
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            // バナーは飾りなので、取れなくても画面は成立する
            RunOnUiThread(() => IsBannerPending = false);
        }
    }

    private void Rebuild()
    {
        _matches = _all.Where(card => !_ownedOnly || card.IsOwned).ToList();
        FillRows();
        _listItems = _matches.Cast<object>().ToList();
        OnPropertyChanged(nameof(ListItems));

        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyReasonText));
        OnPropertyChanged(nameof(HasExcluded));
    }

    private void OpenBooth()
    {
        if (string.IsNullOrEmpty(Shop.Url))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(Shop.Url) { UseShellExecute = true });
    }

    /// <summary>
    /// ファイルの日付で代えた入手日は、そうと書く。
    /// 手で入れた日と同じ顔で出すと、記録として信用できなくなる。
    /// </summary>
    private static string AcquiredText(ShopItem entry) => entry.AcquiredAt is not { } date
        ? "入手日なし"
        : entry.AcquiredIsFallback
            ? date.ToString("yyyy-MM-dd") + "（ファイルの日付）"
            : date.ToString("yyyy-MM-dd");

}
