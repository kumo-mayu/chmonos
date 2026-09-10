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
public sealed class ShopViewModel : ViewModelBase
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
        ThumbnailLoader thumbnails,
        (string Label, Action Go)? back = null)
    {
        Shop = shop;
        _services = services;
        _main = main;
        _thumbnails = thumbnails;

        BackText = back is { } destination ? $"← {destination.Label}に戻る" : "← ショップ一覧に戻る";
        BackCommand = new RelayCommand(() => (back?.Go ?? main.ShowShops)());
        OpenBoothCommand = new RelayCommand(OpenBooth, () => !string.IsNullOrEmpty(Shop.Url));
        RefreshImagesCommand = new RelayCommand(() => _ = RefreshImagesAsync(), () => !IsRefreshingImages);

        // 有無が分からない店だけ、開いた瞬間から場所を空けて待つ。
        // 確かめ直す時期が来た店も「分からない」に含まれる（結果が変わり得るため）
        _reservedBannerArea = shop.BannerState == ShopBannerState.Unknown;
        _isBannerPending = _reservedBannerArea;

        _icon = shop.IconPath is null ? null : thumbnails.Load(shop.IconPath);

        _ = ReloadAsync();
    }

    public ShopSummary Shop { get; }

    public ObservableCollection<ItemCardViewModel> Items { get; } = [];

    public RelayCommand BackCommand { get; }

    /// <summary>どこから来たかで戻り先を変える。来た道と違う場所へ戻されると迷子になる。</summary>
    public string BackText { get; }

    public RelayCommand OpenBoothCommand { get; }

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
            var result = await _services.Shops.RefreshImagesAsync(Shop.Subdomain, _services.Images);

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
        => _main.ShowItem(card.Item, (Shop.Name, () => _main.ShowShop(Shop)));

    public string CountText => _all.Count == Items.Count
        ? $"{Items.Count} 件"
        : $"{Items.Count} 件 / 全 {_all.Count} 件";

    public bool IsEmpty => Items.Count == 0;

    private long _totalBytes;

    public async Task ReloadAsync()
    {
        var entries = await _services.Shops.LoadItemsAsync(Shop.Subdomain);

        RunOnUiThread(() =>
        {
            _totalBytes = entries.Sum(entry => entry.SizeBytes);

            _all = entries.Select(entry => new ItemCardViewModel(
                entry.Item,
                _thumbnails,
                _services.Paths.ItemImagesDir(entry.Item.Id))
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
            var path = await _services.Shops.EnsureBannerAsync(Shop.Subdomain, _services.Images);

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
        Items.Clear();
        foreach (var card in _all.Where(card => !_ownedOnly || card.IsOwned))
        {
            Items.Add(card);
        }

        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsEmpty));
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
