using SixLabors.ImageSharp;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class ShopServiceTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _store;

    public ShopServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-shop-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private ShopService Create(bool showAdult = true, IBoothClient? client = null, int recheckDays = 30)
        => new(
            _store,
            new AppSettings { ShowAdult = showAdult, ShopBannerRecheckDays = recheckDays },
            client);

    /// <summary>ショップページのHTMLを返すだけの偽物。受信量も数える。</summary>
    private sealed class FakeBoothClient : IBoothClient
    {
        private string _html;

        public FakeBoothClient(string html) => _html = html;

        public int Requests { get; private set; }

        /// <summary>取得そのものを失敗させる。通信できなかった場合の扱いを見るため。</summary>
        public bool Fails { get; set; }

        /// <summary>画像の取得も成功させるか。既定は失敗（記録だけを見たいとき用）。</summary>
        public byte[]? Image { get; set; }

        /// <summary>打ち切りが効いているか見るため、実際に渡した文字数を記録する。</summary>
        public int DeliveredChars { get; private set; }

        public void SetPage(string html) => _html = html;

        public Task<BoothFetchResult<string>> GetTextUntilAsync(
            string url,
            Func<string, bool> found,
            int maxBytes = 262144,
            CancellationToken cancellationToken = default)
        {
            Requests++;

            if (Fails)
            {
                return Task.FromResult(BoothFetchResult<string>.Temporary("繋がらなかった"));
            }

            // 実物と同じく、少しずつ足しながら見つかった時点で止める
            var text = new System.Text.StringBuilder();
            foreach (var chunk in Chunks(_html, 4096))
            {
                text.Append(chunk);
                if (found(text.ToString()))
                {
                    break;
                }
            }

            DeliveredChars = text.Length;
            return Task.FromResult(BoothFetchResult<string>.Success(text.ToString()));
        }

        private static IEnumerable<string> Chunks(string value, int size)
        {
            for (var i = 0; i < value.Length; i += size)
            {
                yield return value.Substring(i, Math.Min(size, value.Length - i));
            }
        }

        public Task<BoothFetchResult<string>> GetItemJsonAsync(string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BoothFetchResult<string>> GetItemHtmlAsync(string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BoothFetchResult<byte[]>> GetBinaryAsync(string url, CancellationToken cancellationToken = default)
            => Task.FromResult(Image is null
                ? BoothFetchResult<byte[]>.Temporary("画像は取りに行かない")
                : BoothFetchResult<byte[]>.Success(Image));

        public Task<BoothFetchResult<string>> SearchAsync(string query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public int CurrentIntervalMs => 0;

        public bool IsThrottled => false;

        /// <summary>このフェイクは待たないので、知らせることが無い。</summary>
#pragma warning disable CS0067
        public event Action<BoothActivity>? ActivityChanged;
#pragma warning restore CS0067
    }

    private static string PageWithBanner(string url)
        => new string('x', 20000)
            + $"<img class=\"header-image\" alt=\"shop\" src=\"{url}\">"
            + new string('y', 80000);

    private static BoothShop Shop(string subdomain, string name) => new()
    {
        Name = name,
        Subdomain = subdomain,
        Url = $"https://{subdomain}.booth.pm/",
    };

    private Task SaveItemAsync(
        string id,
        BoothShop shop,
        LocalBlock? local = null,
        bool isAdult = false,
        DateTimeOffset? fetchedAt = null)
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock
            {
                Name = "item " + id,
                Shop = shop,
                IsAdult = isAdult,
                FetchedAt = fetchedAt ?? DateTimeOffset.Now,
            },
            Local = local ?? new LocalBlock(),
        });

    private static LocalBlock Owned(int? price = null, string? acquiredAt = null, long size = 100)
        => new()
        {
            LocalFiles = [new LocalFileRecord { Hash = Guid.NewGuid().ToString("N"), Paths = ["x"], SizeBytes = size }],
            Purchases = price is null ? [] : [new Purchase { VariationId = 1, Price = price }],
            AcquiredAt = acquiredAt is null ? null : DateOnly.Parse(acquiredAt),
        };

    /// <summary>
    /// ショップの画面が空のときに、なぜ無いのか（非表示・R-18を出さない設定）を言うための数（動線の点検 D8）。
    /// 非表示でR-18のものは非表示の方に数える。別の店の商品は数えない
    /// </summary>
    [Fact]
    public async Task CountsItemsLeftOutOfTheShop()
    {
        await SaveItemAsync("1", Shop("a", "A"), Owned() with { IsHidden = true });
        await SaveItemAsync("2", Shop("a", "A"), Owned(), isAdult: true);
        await SaveItemAsync("3", Shop("a", "A"), Owned() with { IsHidden = true }, isAdult: true);
        await SaveItemAsync("4", Shop("a", "A"), Owned());
        await SaveItemAsync("5", Shop("b", "B"), Owned() with { IsHidden = true });
        var items = (await _store.Items.LoadAllAsync()).Items;

        Assert.Equal((2, 1), Create(showAdult: false).ExcludedOf(items, "a"));
        Assert.Equal((2, 0), Create(showAdult: true).ExcludedOf(items, "a"));
    }

    /// <summary>集計キーはサブドメイン。名前で束ねると、改名した店が2つに割れる。</summary>
    [Fact]
    public async Task GroupsBySubdomainNotByName()
    {
        await SaveItemAsync("1", Shop("kotori", "kotori shop"), Owned(), fetchedAt: DateTimeOffset.Now.AddDays(-2));
        await SaveItemAsync("2", Shop("kotori", "ことりショップ"), Owned(), fetchedAt: DateTimeOffset.Now);

        var shop = Assert.Single(await Create().LoadAsync());

        Assert.Equal("kotori", shop.Subdomain);
        Assert.Equal(2, shop.OwnedCount);

        // 名前は最後に取得したものを採る
        Assert.Equal("ことりショップ", shop.Name);
    }

    /// <summary>
    /// 「所持」はファイルを持っていること。持っていないitemは件数にも支出にも入れない
    /// （決定事項：全画面で同じ定義を使う）。
    /// </summary>
    [Fact]
    public async Task CountsOnlyItemsThatHaveFiles()
    {
        await SaveItemAsync("1", Shop("a", "A"), Owned(price: 1000));
        await SaveItemAsync("2", Shop("a", "A"), new LocalBlock
        {
            Purchases = [new Purchase { VariationId = 1, Price = 5000 }],
        });

        var shop = Assert.Single(await Create().LoadAsync());

        Assert.Equal(2, shop.KnownCount);
        Assert.Equal(1, shop.OwnedCount);
        Assert.Equal(1000, shop.SpentYen);
    }

    /// <summary>フォルダ登録も所持として数える。zipが残っていない配布物のため。</summary>
    [Fact]
    public async Task TreatsARegisteredFolderAsOwned()
    {
        await SaveItemAsync("1", Shop("a", "A"), new LocalBlock
        {
            LocalFolders = [new LocalFolderRecord { Path = @"D:\x", FileCount = 3, TotalBytes = 900 }],
        });

        Assert.Equal(1, Assert.Single(await Create().LoadAsync()).OwnedCount);
    }

    [Fact]
    public async Task ExcludesHiddenItemsFromTheCount()
    {
        await SaveItemAsync("1", Shop("a", "A"), Owned());
        await SaveItemAsync("2", Shop("a", "A"), Owned() with { IsHidden = true });

        var shop = Assert.Single(await Create().LoadAsync());

        Assert.Equal(1, shop.KnownCount);
        Assert.Equal(1, shop.OwnedCount);
    }

    /// <summary>R-18は設定で表示を切っているときだけ外す。</summary>
    [Fact]
    public async Task ExcludesAdultItemsOnlyWhenHidden()
    {
        await SaveItemAsync("1", Shop("a", "A"), Owned());
        await SaveItemAsync("2", Shop("a", "A"), Owned(), isAdult: true);

        Assert.Equal(2, Assert.Single(await Create(showAdult: true).LoadAsync()).OwnedCount);
        Assert.Equal(1, Assert.Single(await Create(showAdult: false).LoadAsync()).OwnedCount);
    }

    /// <summary>ギフトは自分の支出ではない。未入力は0として扱う。</summary>
    [Fact]
    public async Task SumsPurchasePricesWithoutGifts()
    {
        await SaveItemAsync("1", Shop("a", "A"), new LocalBlock
        {
            LocalFiles = [new LocalFileRecord { Hash = "h1", Paths = ["x"], SizeBytes = 1 }],
            Purchases =
            [
                new Purchase { VariationId = 1, Price = 1200 },
                new Purchase { VariationId = 2, Price = 800, Kind = PurchaseKind.Received },
                new Purchase { VariationId = 3, Price = null },
                new Purchase { VariationId = 4, Price = 300, ExistsOnBooth = false },
            ],
        });

        // 消えたvariationも、払った事実は変わらないので含める
        Assert.Equal(1500, Assert.Single(await Create().LoadAsync()).SpentYen);
    }

    /// <summary>
    /// 入手日は商品ページと同じ求め方をする。手入力に限ると、商品ページには日付が出ているのに
    /// 一覧では空、という食い違いが起きる。代えたことは持ち回って画面で断る。
    /// </summary>
    [Fact]
    public async Task FallsBackToTheFileDateLikeTheItemPage()
    {
        var file = Path.Combine(_root, "sample.zip");
        await File.WriteAllTextAsync(file, "x");
        File.SetLastWriteTime(file, new DateTime(2026, 4, 9));

        await SaveItemAsync("1", Shop("a", "A"), new LocalBlock
        {
            LocalFiles = [new LocalFileRecord { Hash = "h", Paths = [file], SizeBytes = 1 }],
        });

        var shop = Assert.Single(await Create().LoadAsync());

        Assert.Equal(new DateOnly(2026, 4, 9), shop.LastAcquiredAt);
        Assert.True(shop.LastAcquiredIsFallback);

        var item = Assert.Single(await Create().LoadItemsAsync("a"));
        Assert.True(item.AcquiredIsFallback);
    }

    [Fact]
    public async Task TakesTheLatestAcquiredDate()
    {
        await SaveItemAsync("1", Shop("a", "A"), Owned(acquiredAt: "2026-01-05"));
        await SaveItemAsync("2", Shop("a", "A"), Owned(acquiredAt: "2026-03-20"));
        await SaveItemAsync("3", Shop("a", "A"), Owned());

        Assert.Equal(new DateOnly(2026, 3, 20), Assert.Single(await Create().LoadAsync()).LastAcquiredAt);
    }

    [Fact]
    public async Task ReportsHowManyItemsHaveAnUnreadUpdateNotice()
    {
        await SaveItemAsync("1", Shop("a", "A"), Owned());
        await SaveItemAsync("2", Shop("a", "A"), Owned());

        await _store.Notifications.SaveAsync(
        [
            new NotificationRecord
            {
                Id = "n1",
                Kind = NotificationKind.ItemUpdated,
                ItemId = "1",
                Title = "更新",
                Detail = string.Empty,
                CreatedAt = DateTimeOffset.Now,
            },
            new NotificationRecord
            {
                Id = "n2",
                Kind = NotificationKind.ItemUpdated,
                ItemId = "2",
                Title = "更新",
                Detail = string.Empty,
                CreatedAt = DateTimeOffset.Now,
                IsRead = true,
            },
        ]);

        Assert.Equal(1, Assert.Single(await Create().LoadAsync()).UpdatedCount);
    }

    /// <summary>ショップの商品一覧。所持していないものも、情報があるなら出す。</summary>
    [Fact]
    public async Task ListsItemsOfOneShopWithTheirOwnedState()
    {
        await SaveItemAsync("1", Shop("a", "A"), Owned(acquiredAt: "2026-02-01"));
        await SaveItemAsync("2", Shop("a", "A"), new LocalBlock());
        await SaveItemAsync("3", Shop("b", "B"), Owned());

        var items = await Create().LoadItemsAsync("a");

        Assert.Equal(2, items.Count);
        Assert.True(items[0].IsOwned);
        Assert.False(items[1].IsOwned);
    }

    /// <summary>
    /// バナーはHTMLの先頭付近にあるので、見つかった時点で受信をやめる。
    /// 100KB超のページを毎回最後まで読む理由が無い。
    /// </summary>
    [Fact]
    public async Task StopsReadingOnceTheBannerIsFound()
    {
        const string Url = "https://s6.booth.pm/aaa/bbb.png";
        var client = new FakeBoothClient(PageWithBanner(Url))
        {
            Image = await File.ReadAllBytesAsync(WritePng()),
        };

        await Create(client: client).EnsureBannerAsync("shop", new ImagePipeline(client, _store.Paths));

        Assert.Equal(1, client.Requests);

        // 全体10万字超のうち、バナーの位置（2万字）付近で止まっている
        Assert.InRange(client.DeliveredChars, 20000, 30000);

        var record = Assert.Single(_store.ShopBanners.Load());
        Assert.Equal(Url, record.SourceUrl);
    }

    /// <summary>
    /// バナーを置いていないショップは記録して、しばらくは探しに行かない。
    /// 確かめるにはページを最後まで読むしかないので、毎回は見に行けない。
    /// </summary>
    [Fact]
    public async Task RemembersThatAShopHasNoBanner()
    {
        var client = new FakeBoothClient(new string('x', 60000));
        var service = Create(client: client);
        var images = new ImagePipeline(client, _store.Paths);

        Assert.Null(await service.EnsureBannerAsync("shop", images));
        Assert.Equal(1, client.Requests);

        var record = Assert.Single(_store.ShopBanners.Load());
        Assert.False(record.HasBanner);
        Assert.Null(record.SourceUrl);

        // 期間内は取りに行かない
        Assert.Null(await service.EnsureBannerAsync("shop", images));
        Assert.Equal(1, client.Requests);
    }

    /// <summary>
    /// 期間が過ぎたら見に行き直す。後からバナーを付けるショップもあるので、
    /// 一度「無い」と分かっただけで永久に決めつけない。
    /// </summary>
    [Fact]
    public async Task LooksAgainAfterTheRecheckPeriod()
    {
        var client = new FakeBoothClient(new string('x', 60000));
        var images = new ImagePipeline(client, _store.Paths);

        Assert.Null(await Create(client: client).EnsureBannerAsync("shop", images));
        Assert.Equal(1, client.Requests);

        // 確かめた日を過去にずらす（期間が過ぎた状態にする）
        var stale = _store.ShopBanners.Load()
            .Select(record => record with { CheckedAt = DateTimeOffset.Now.AddDays(-40) })
            .ToList();

        await _store.ShopBanners.SaveAsync(stale);

        await Create(client: client, recheckDays: 30).EnsureBannerAsync("shop", images);
        Assert.Equal(2, client.Requests);
    }

    /// <summary>
    /// 繋がらなかっただけのときは何も決めない。
    /// ここで「バナー無し」と覚えると、一度の不調で次に確かめるまで出なくなる。
    /// </summary>
    [Fact]
    public async Task DoesNotRecordAnythingWhenTheFetchFailed()
    {
        var client = new FakeBoothClient(PageWithBanner("https://s6.booth.pm/a/b.png")) { Fails = true };
        var service = Create(client: client);
        var images = new ImagePipeline(client, _store.Paths);

        Assert.Null(await service.EnsureBannerAsync("shop", images));
        Assert.Empty(_store.ShopBanners.Load());

        // 次に開いたときはもう一度試す
        Assert.Null(await service.EnsureBannerAsync("shop", images));
        Assert.Equal(2, client.Requests);
    }

    /// <summary>画像だけ取れなかったときも記録しない。次に開いたときにやり直せるように。</summary>
    [Fact]
    public async Task DoesNotRecordWhenOnlyTheImageFailed()
    {
        var client = new FakeBoothClient(PageWithBanner("https://s6.booth.pm/a/b.png"));
        var service = Create(client: client);

        Assert.Null(await service.EnsureBannerAsync("shop", new ImagePipeline(client, _store.Paths)));
        Assert.Empty(_store.ShopBanners.Load());
    }

    /// <summary>
    /// BOOTH側からバナーが消えても、手元のものは残して出し続ける。
    /// 消えた画像は取り直せないので、商品画像と同じくアーカイブとして扱う。
    /// </summary>
    [Fact]
    public async Task KeepsTheLocalBannerAfterItDisappearsFromBooth()
    {
        var client = new FakeBoothClient(PageWithBanner("https://s6.booth.pm/a/b.png"))
        {
            Image = await File.ReadAllBytesAsync(WritePng()),
        };

        var images = new ImagePipeline(client, _store.Paths);

        var path = await Create(client: client).EnsureBannerAsync("shop", images);
        Assert.NotNull(path);
        Assert.True(File.Exists(path));

        // BOOTH側からバナーが消え、期間も過ぎた状態にする
        client.SetPage(new string('x', 60000));
        await _store.ShopBanners.SaveAsync(_store.ShopBanners.Load()
            .Select(record => record with { CheckedAt = DateTimeOffset.Now.AddDays(-40) })
            .ToList());

        var again = await Create(client: client).EnsureBannerAsync("shop", images);

        Assert.Equal(path, again);
        Assert.False(Assert.Single(_store.ShopBanners.Load()).HasBanner);
    }

    /// <summary>同じURLのままなら落とし直さない。日付だけ新しくする。</summary>
    [Fact]
    public async Task DoesNotDownloadAgainWhenTheUrlIsUnchanged()
    {
        var client = new FakeBoothClient(PageWithBanner("https://s6.booth.pm/a/b.png"))
        {
            Image = await File.ReadAllBytesAsync(WritePng()),
        };

        var images = new ImagePipeline(client, _store.Paths);
        await Create(client: client).EnsureBannerAsync("shop", images);

        var savedAt = File.GetLastWriteTimeUtc(_store.Paths.ShopBannerFile("shop"));

        await _store.ShopBanners.SaveAsync(_store.ShopBanners.Load()
            .Select(record => record with { CheckedAt = DateTimeOffset.Now.AddDays(-40) })
            .ToList());

        await Create(client: client).EnsureBannerAsync("shop", images);

        Assert.Equal(savedAt, File.GetLastWriteTimeUtc(_store.Paths.ShopBannerFile("shop")));
        Assert.True(Assert.Single(_store.ShopBanners.Load()).CheckedAt > DateTimeOffset.Now.AddDays(-1));
    }

    /// <summary>変換できる最小のPNGを1枚置く。</summary>
    private string WritePng()
    {
        var path = Path.Combine(_root, "banner-source.png");
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1500, 500);
        image.SaveAsPng(path);
        return path;
    }

    /// <summary>
    /// バナーの有無が分かっているかを画面へ伝える。
    /// 分からない間だけ場所を空けて待ち、分かっている店では最初から正しい高さで開く。
    /// </summary>
    [Fact]
    public async Task ReportsWhetherTheBannerStateIsKnown()
    {
        await SaveItemAsync("1", Shop("unknown", "まだ調べていない"), Owned());
        await SaveItemAsync("2", Shop("absent", "置いていない"), Owned());
        await SaveItemAsync("3", Shop("present", "持っている"), Owned());

        await _store.ShopBanners.SaveAsync(
        [
            new ShopBannerRecord { Subdomain = "absent", HasBanner = false, CheckedAt = DateTimeOffset.Now },
        ]);

        Directory.CreateDirectory(_store.Paths.ShopIconsDir);
        await File.WriteAllBytesAsync(_store.Paths.ShopBannerFile("present"), [1, 2, 3]);

        var shops = (await Create().LoadAsync()).ToDictionary(shop => shop.Subdomain);

        Assert.Equal(ShopBannerState.Unknown, shops["unknown"].BannerState);
        Assert.Equal(ShopBannerState.Absent, shops["absent"].BannerState);
        Assert.Equal(ShopBannerState.Present, shops["present"].BannerState);
    }

    /// <summary>
    /// 一覧の集計は置き場を1回だけ列挙した表から引く。店ごとに探したときと同じアイコンとバナーを返す。
    /// </summary>
    [Fact]
    public async Task SummarizesIconsAndBannersLikeLookingUpEachShop()
    {
        await SaveItemAsync("1", Shop("alpha", "A"), Owned());
        await SaveItemAsync("2", Shop("beta", "B"), Owned());
        await SaveItemAsync("3", Shop("gamma", "C"), Owned());

        Directory.CreateDirectory(_store.Paths.ShopIconsDir);
        var old = Path.Combine(_store.Paths.ShopIconsDir, "alpha_11111111.webp");
        var fresh = Path.Combine(_store.Paths.ShopIconsDir, "alpha_22222222.webp");
        await File.WriteAllBytesAsync(old, [1]);
        await File.WriteAllBytesAsync(fresh, [1]);
        File.SetLastWriteTimeUtc(old, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(fresh, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        await File.WriteAllBytesAsync(_store.Paths.ShopBannerFile("alpha"), [1]);
        await File.WriteAllBytesAsync(_store.Paths.ShopBannerFile("gamma"), [1]);

        var shops = (await Create().LoadAsync()).ToDictionary(shop => shop.Subdomain);

        foreach (var subdomain in new[] { "alpha", "beta", "gamma" })
        {
            Assert.Equal(_store.Paths.FindShopIcon(subdomain), shops[subdomain].IconPath);
            var banner = _store.Paths.ShopBannerFile(subdomain);
            Assert.Equal(File.Exists(banner) ? banner : null, shops[subdomain].BannerPath);
        }

        Assert.Equal(fresh, shops["alpha"].IconPath);
        Assert.Null(shops["beta"].IconPath);
        Assert.Equal(ShopBannerState.Present, shops["gamma"].BannerState);
    }

    /// <summary>
    /// 確かめ直す時期が来た店は「分からない」に戻す。
    /// これを Absent のままにすると、場所を空けずに開いた後でバナーが見つかり、
    /// 結局そこで中身が下へずれる。
    /// </summary>
    [Fact]
    public async Task TreatsAShopDueForRecheckAsUnknown()
    {
        await SaveItemAsync("1", Shop("stale", "しばらく確かめていない"), Owned());

        await _store.ShopBanners.SaveAsync(
        [
            new ShopBannerRecord
            {
                Subdomain = "stale",
                HasBanner = false,
                CheckedAt = DateTimeOffset.Now.AddDays(-40),
            },
        ]);

        Assert.Equal(
            ShopBannerState.Unknown,
            Assert.Single(await Create(recheckDays: 30).LoadAsync()).BannerState);
    }

    /// <summary>
    /// 「取り直す」でも、URLが同じなら落とし直さない。
    /// BOOTHのバナーは名前が乱数なので、差し替えれば必ずURLが変わる。
    /// 押されるたびに同じ絵を取り直すのは、最大4MBの無駄になる。
    /// </summary>
    [Fact]
    public async Task DoesNotRedownloadTheBannerWhenTheUrlIsUnchanged()
    {
        const string Url = "https://s6.booth.pm/a/b.png";
        var client = new FakeBoothClient(PageWithBanner(Url))
        {
            Image = await File.ReadAllBytesAsync(WritePng()),
        };

        var service = Create(client: client);
        var images = new ImagePipeline(client, _store.Paths);

        await service.EnsureBannerAsync("shop", images);
        var savedAt = File.GetLastWriteTimeUtc(_store.Paths.ShopBannerFile("shop"));

        var result = await service.RefreshImagesAsync("shop", images);

        Assert.False(result.BannerUpdated);
        Assert.False(result.Failed);
        Assert.Equal(savedAt, File.GetLastWriteTimeUtc(_store.Paths.ShopBannerFile("shop")));
    }

    /// <summary>差し替えられていれば取り直す。</summary>
    [Fact]
    public async Task RedownloadsTheBannerWhenTheUrlChanged()
    {
        var client = new FakeBoothClient(PageWithBanner("https://s6.booth.pm/a/old.png"))
        {
            Image = await File.ReadAllBytesAsync(WritePng()),
        };

        var service = Create(client: client);
        var images = new ImagePipeline(client, _store.Paths);

        await service.EnsureBannerAsync("shop", images);

        client.SetPage(PageWithBanner("https://s6.booth.pm/a/new.png"));
        var result = await service.RefreshImagesAsync("shop", images);

        Assert.True(result.BannerUpdated);
        Assert.Equal("https://s6.booth.pm/a/new.png", Assert.Single(_store.ShopBanners.Load()).SourceUrl);
    }

    /// <summary>
    /// アイコンもショップページから取り直す。
    /// 商品JSON側のURLはitemを取り直すまで古いままなので、ここではページの値を使う。
    /// </summary>
    [Fact]
    public async Task PicksUpANewIconFromTheShopPage()
    {
        const string Icon = "https://booth.pximg.net/c/128x128/users/1/icon_image/new.jpg";
        var client = new FakeBoothClient(new string('x', 5000) + $"<img src=\"{Icon}\">" + new string('y', 5000))
        {
            Image = await File.ReadAllBytesAsync(WritePng()),
        };

        var result = await Create(client: client)
            .RefreshImagesAsync("shop", new ImagePipeline(client, _store.Paths));

        Assert.True(result.IconUpdated);
        Assert.NotNull(result.IconPath);
        Assert.True(result.BannerAbsent);
    }

    /// <summary>取得手段が無いとき（テストや将来の切り離し）に落ちない。</summary>
    [Fact]
    public async Task DoesNothingWithoutAClient()
    {
        Assert.Null(await Create().EnsureBannerAsync("shop", new ImagePipeline(new FakeBoothClient(""), _store.Paths)));
    }

    /// <summary>同じ中身のファイルは1回だけ数える。複数箇所に置いていても容量は1つ分。</summary>
    [Fact]
    public async Task CountsDuplicateFilesOnce()
    {
        await SaveItemAsync("1", Shop("a", "A"), new LocalBlock
        {
            LocalFiles =
            [
                new LocalFileRecord { Hash = "same", Paths = ["x"], SizeBytes = 500 },
                new LocalFileRecord { Hash = "same", Paths = ["y"], SizeBytes = 500 },
                new LocalFileRecord { Hash = "other", Paths = ["z"], SizeBytes = 200 },
            ],
        });

        Assert.Equal(700, Assert.Single(await Create().LoadItemsAsync("a")).SizeBytes);
    }
}
