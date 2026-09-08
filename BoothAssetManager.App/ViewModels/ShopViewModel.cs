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
        ThumbnailLoader thumbnails)
    {
        Shop = shop;
        _services = services;
        _main = main;
        _thumbnails = thumbnails;

        BackCommand = new RelayCommand(main.ShowShops);
        OpenBoothCommand = new RelayCommand(OpenBooth, () => !string.IsNullOrEmpty(Shop.Url));

        _ = ReloadAsync();
    }

    public ShopSummary Shop { get; }

    public ObservableCollection<ItemCardViewModel> Items { get; } = [];

    public RelayCommand BackCommand { get; }

    public RelayCommand OpenBoothCommand { get; }

    public string Name => Shop.Name;

    public string DomainText => $"{Shop.Subdomain}.booth.pm";

    public string Initial => Shop.Name.Length == 0 ? "?" : Shop.Name[..1];

    /// <summary>落としてあるアイコン。まだ無ければ頭文字のタイルで代える。</summary>
    public System.Windows.Media.Imaging.BitmapSource? Icon
        => Shop.IconPath is null ? null : _thumbnails.Load(Shop.IconPath);

    public bool HasIcon => Icon is not null;

    public bool ShowInitial => Icon is null;

    private System.Windows.Media.Imaging.BitmapSource? _banner;

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
            }
        }
    }

    public bool HasBanner => Banner is not null;

    public string OwnedText => $"{Shop.OwnedCount}";

    public string SpentText => $"¥{Shop.SpentYen:N0}";

    public string LastAcquiredText => Shop.LastAcquiredAt?.ToString("yyyy-MM-dd") ?? "—";

    public string SizeText => FormatSize(_totalBytes);

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
                Name = entry.Item.Booth.Name ?? entry.Item.Id,
                // カードの2行目は入手日にする。ショップ画面では店名が全部同じで意味が無い
                ShopName = AcquiredText(entry),
                SizeText = entry.IsOwned ? FormatSize(entry.SizeBytes) : "未取得",
                IsOwned = entry.IsOwned,
                NeedsEdit = entry.Item.Local.AppTags.Count == 0,
                AppTagText = string.Join(" / ", entry.Item.Local.AppTags.Select(tag => tag.Top)),
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

        try
        {
            var path = await _services.Shops.EnsureBannerAsync(Shop.Subdomain, _services.Images);
            if (path is not null)
            {
                RunOnUiThread(() => Banner = _thumbnails.Load(path));
            }
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            // バナーは飾りなので、取れなくても画面は成立する
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

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
