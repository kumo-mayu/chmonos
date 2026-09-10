using System.Collections.ObjectModel;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>ショップ一覧の並べ替え方。</summary>
public sealed class ShopSortOption
{
    public required string Label { get; init; }

    public required Func<ShopSummary, IComparable> Key { get; init; }

    public bool Descending { get; init; }
}

/// <summary>ショップ一覧の1枚。</summary>
public sealed class ShopCardViewModel : ViewModelBase
{
    private System.Windows.Media.Imaging.BitmapSource? _icon;

    public required ShopSummary Shop { get; init; }

    /// <summary>落としてあるアイコン。まだ無ければ頭文字のタイルで代える。</summary>
    public System.Windows.Media.Imaging.BitmapSource? Icon
    {
        get => _icon;
        set
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

    public string Name => Shop.Name;

    /// <summary>
    /// ショップのドメイン。**手元だけのショップには付けない**——
    /// BOOTHに無い鍵に .booth.pm を足すと、実在しないURLを名乗ることになる。
    /// </summary>
    public string DomainText => Core.Models.LocalShopKey.IsLocal(Shop.Subdomain)
        ? "BOOTHのショップに結び付いていません"
        : $"{Shop.Subdomain}.booth.pm";

    public string OwnedText => $"{Shop.OwnedCount}";

    public string SpentText => $"¥{Shop.SpentYen:N0}";

    public string LastAcquiredText => Shop.LastAcquiredAt?.ToString("yyyy-MM-dd") ?? "—";

    /// <summary>ファイルの日付で代えた日は、そうと分かるようにする。手入力と同じ顔で出さない。</summary>
    public bool LastAcquiredIsFallback => Shop.LastAcquiredIsFallback;

    public string LastAcquiredTooltip => Shop.LastAcquiredIsFallback
        ? "入手日が手入力されていないので、ファイルの日付で代えています。"
        : string.Empty;

    /// <summary>情報はあるが所持していない商品数。0なら出さない。</summary>
    public bool HasUnowned => Shop.KnownCount > Shop.OwnedCount;

    public string UnownedText => $"ほかに情報だけ {Shop.KnownCount - Shop.OwnedCount} 件";

    public bool HasUpdate => Shop.UpdatedCount > 0;

    public string UpdateText => $"更新のあった商品が {Shop.UpdatedCount} 件";

    /// <summary>
    /// アイコンの代わり。ショップのアイコンはBOOTH側のURLしか持っておらず、
    /// ローカルには落としていない（商品画像と違って一覧を出すためだけに取りに行く価値が薄い）。
    /// 頭文字と、サブドメインから決まる色で見分けが付くようにする。
    /// </summary>
    public string Initial => Shop.Name.Length == 0 ? "?" : Shop.Name[..1];

    public System.Windows.Media.Brush InitialBrush => TileBrush(Shop.Subdomain);

    public RelayCommand? OpenCommand { get; set; }

    /// <summary>名前から色を決める。同じショップは常に同じ色になる。</summary>
    private static System.Windows.Media.Brush TileBrush(string key)
    {
        var hash = key.Aggregate(17, (current, character) => (current * 31) + character);
        var hue = Math.Abs(hash) % 360;

        var color = System.Windows.Media.ColorConverter.ConvertFromString(
            $"#{FromHue(hue):X6}");

        return new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)color!);
    }

    /// <summary>彩度と明度は固定。背景として落ち着く範囲に収める。</summary>
    private static int FromHue(int hue)
    {
        var section = hue / 60;
        var offset = (hue % 60) / 60.0;

        const int Low = 0xC8;
        const int High = 0xE4;
        var rising = Low + (int)((High - Low) * offset);
        var falling = High - (int)((High - Low) * offset);

        return section switch
        {
            0 => (High << 16) | (rising << 8) | Low,
            1 => (falling << 16) | (High << 8) | Low,
            2 => (Low << 16) | (High << 8) | rising,
            3 => (Low << 16) | (falling << 8) | High,
            4 => (rising << 16) | (Low << 8) | High,
            _ => (High << 16) | (Low << 8) | falling,
        };
    }
}

/// <summary>
/// ショップ一覧。
///
/// 数え方は決定事項に合わせてある（所持＝ファイルあり、非表示とR-18は件数から除く）。
/// 内部の扱いを隠さないよう、その但し書きは画面にも出す。
/// </summary>
public sealed class ShopsViewModel : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;
    private readonly Services.ThumbnailLoader _thumbnails;
    private readonly CancellationTokenSource _iconFetch = new();

    private List<ShopCardViewModel> _all = [];
    private string _iconStatus = string.Empty;
    private string _filterText = string.Empty;
    private ShopSortOption _sort;
    private bool _isLoading;

    public ShopsViewModel(AppServiceContainer services, MainViewModel main, Services.ThumbnailLoader thumbnails)
    {
        _services = services;
        _main = main;
        _thumbnails = thumbnails;

        SortOptions =
        [
            new ShopSortOption { Label = "所持が多い順", Key = shop => shop.OwnedCount, Descending = true },
            new ShopSortOption { Label = "支出が多い順", Key = shop => shop.SpentYen, Descending = true },
            new ShopSortOption
            {
                Label = "最終購入が新しい順",
                Key = shop => shop.LastAcquiredAt ?? DateOnly.MinValue,
                Descending = true,
            },
            new ShopSortOption { Label = "ショップ名順", Key = shop => shop.Name },
        ];

        _sort = SortOptions[0];
        RefreshCommand = new RelayCommand(() => _ = ReloadAsync());

        _ = ReloadAsync();
    }

    public ObservableCollection<ShopCardViewModel> Shops { get; } = [];

    public IReadOnlyList<ShopSortOption> SortOptions { get; }

    public RelayCommand RefreshCommand { get; }

    public ShopSortOption Sort
    {
        get => _sort;
        set
        {
            if (value is not null && SetField(ref _sort, value))
            {
                Rebuild();
            }
        }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value))
            {
                Rebuild();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public string HeaderText => $"{_all.Count} ショップ";

    public bool IsEmpty => !IsLoading && Shops.Count == 0;

    public string EmptyText => _all.Count == 0
        ? "ショップがありません"
        : "該当するショップがありません。検索語を短くしてみてください。";

    public async Task ReloadAsync()
    {
        IsLoading = true;
        try
        {
            var shops = await _services.Shops.LoadAsync();

            RunOnUiThread(() =>
            {
                _all = shops.Select(shop =>
                {
                    var card = new ShopCardViewModel
                    {
                        Shop = shop,
                        Icon = shop.IconPath is null ? null : _thumbnails.Load(shop.IconPath),
                    };

                    card.OpenCommand = new RelayCommand(() => _main.ShowShop(shop));
                    return card;
                }).ToList();

                Rebuild();
                OnPropertyChanged(nameof(HeaderText));
            });
        }
        finally
        {
            IsLoading = false;
            RunOnUiThread(() => OnPropertyChanged(nameof(IsEmpty)));
        }

        await FetchMissingIconsAsync();
    }

    /// <summary>
    /// まだ持っていないアイコンを裏で順に落とす。
    ///
    /// URLは商品JSONにしか入っていないので、取り込み済みのitemではアイコンだけが抜けている。
    /// 全部揃うまで画面を止めず、届いたものからその場で差し替える。
    /// BOOTHへは1件ずつ間隔を空けて行くので、店数が多いと時間がかかる。
    /// </summary>
    private async Task FetchMissingIconsAsync()
    {
        var missing = _all.Count(card => card.Icon is null && card.Shop.ThumbnailUrl is not null);
        if (missing == 0)
        {
            return;
        }

        IconStatus = $"ショップのアイコンを取得しています（残り {missing} 件）…";

        try
        {
            var done = 0;

            await _services.Shops.SyncMissingIconsAsync(
                _services.Images,
                (subdomain, path) =>
                {
                    done++;
                    RunOnUiThread(() =>
                    {
                        var card = _all.FirstOrDefault(entry =>
                            string.Equals(entry.Shop.Subdomain, subdomain, StringComparison.OrdinalIgnoreCase));

                        if (card is not null)
                        {
                            card.Icon = _thumbnails.Load(path);
                        }

                        IconStatus = done >= missing
                            ? string.Empty
                            : $"ショップのアイコンを取得しています（残り {missing - done} 件）…";
                    });

                    return Task.CompletedTask;
                },
                _iconFetch.Token);
        }
        catch (OperationCanceledException)
        {
            // 画面を離れたら取りに行くのをやめる
        }
        finally
        {
            RunOnUiThread(() => IconStatus = string.Empty);
        }
    }

    /// <summary>画面を離れるときに呼ぶ。取得を続ける意味がないので止める。</summary>
    public void StopFetching() => _iconFetch.Cancel();

    public string IconStatus
    {
        get => _iconStatus;
        private set
        {
            if (SetField(ref _iconStatus, value))
            {
                OnPropertyChanged(nameof(HasIconStatus));
            }
        }
    }

    public bool HasIconStatus => IconStatus.Length > 0;

    private void Rebuild()
    {
        var filter = _filterText.Trim();

        var matches = _all.Where(card => filter.Length == 0
            || card.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
            || card.Shop.Subdomain.Contains(filter, StringComparison.OrdinalIgnoreCase));

        var sorted = _sort.Descending
            ? matches.OrderByDescending(card => _sort.Key(card.Shop))
            : matches.OrderBy(card => _sort.Key(card.Shop));

        Shops.Clear();
        foreach (var card in sorted.ThenBy(card => card.Name, StringComparer.CurrentCulture))
        {
            Shops.Add(card);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }
}
