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

    private ShopService Create(bool showAdult = true) => new(_store, new AppSettings { ShowAdult = showAdult });

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
            OrderedVariations = price is null
                ? []
                : [new OrderedVariation { VariationId = 1, Price = price }],
            AcquiredAt = acquiredAt is null ? null : DateOnly.Parse(acquiredAt),
        };

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
            OrderedVariations = [new OrderedVariation { VariationId = 1, Price = 5000 }],
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
            OrderedVariations =
            [
                new OrderedVariation { VariationId = 1, Price = 1200 },
                new OrderedVariation { VariationId = 2, Price = 800, IsGifted = true },
                new OrderedVariation { VariationId = 3, Price = null },
                new OrderedVariation { VariationId = 4, Price = 300, ExistsOnBooth = false },
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
