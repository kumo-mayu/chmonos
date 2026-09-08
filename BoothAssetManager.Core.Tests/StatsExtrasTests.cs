using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class StatsExtrasTests
{
    private static ItemRecord Item(
        string id,
        int? price = null,
        bool gifted = false,
        string? category = null,
        string[]? appTags = null,
        long size = 100,
        string? acquiredAt = null,
        bool endOfSale = false,
        bool soldOut = false,
        int wish = 0,
        int? currentPrice = null,
        Dictionary<string, int>? attributes = null,
        bool hidden = false)
        => new()
        {
            Id = id,
            Booth = new BoothBlock
            {
                Name = "item " + id,
                FetchedAt = DateTimeOffset.Now,
                IsEndOfSale = endOfSale,
                IsSoldOut = soldOut,
                WishListsCount = wish,
                Category = category is null ? null : new BoothCategory { Id = 1, Name = category },
                Variations = currentPrice is null
                    ? []
                    : [new BoothVariation { Id = 1, Price = currentPrice.Value }],
            },
            Local = new LocalBlock
            {
                IsHidden = hidden,
                LocalFiles = [new LocalFileRecord { Hash = id, Paths = ["x"], SizeBytes = size }],
                Purchases = price is null
                    ? []
                    : [new Purchase
                    {
                        VariationId = 1,
                        Price = price,
                        Kind = gifted ? PurchaseKind.Received : PurchaseKind.ForSelf,
                    }],
                AppTags = (appTags ?? []).Select(top => new AppTagAssignment { Top = top }).ToList(),
                Attributes = attributes ?? new Dictionary<string, int>(),
                AcquiredAt = acquiredAt is null ? null : DateOnly.Parse(acquiredAt),
            },
        };

    private static StatsSnapshot Build(params ItemRecord[] items)
        => StatsService.Build(items, new AvatarRegistry(), 0);

    /// <summary>BoothBlock は record ではないので、ショップ付きの複製はここで作る。</summary>
    private static ItemRecord InShop(ItemRecord item, string subdomain) => item with
    {
        Booth = new BoothBlock
        {
            Name = item.Booth.Name,
            FetchedAt = item.Booth.FetchedAt,
            Category = item.Booth.Category,
            Variations = item.Booth.Variations,
            Shop = new BoothShop { Name = subdomain, Subdomain = subdomain },
        },
    };

    /// <summary>買った時と今で価格が違う商品を拾う。現存するvariationだけを比べる。</summary>
    [Fact]
    public void FindsPriceChanges()
    {
        var snapshot = Build(
            Item("1", price: 1000, currentPrice: 1500),
            Item("2", price: 800, currentPrice: 800),
            Item("3", price: 500));

        var change = Assert.Single(snapshot.PriceChanges);
        Assert.Equal("1", change.ItemId);
        Assert.Equal(500, change.DiffYen);
    }

    /// <summary>
    /// 贈った商品はファイルが手元に来ないので所持には入らない。
    /// 集計対象を「ファイルを持つitem」と決めてあるので、贈答だけは全itemから数える。
    /// </summary>
    [Fact]
    public void CountsGivenGiftsAcrossAllItemsNotJustOwned()
    {
        var given = new ItemRecord
        {
            Id = "gift",
            Booth = new BoothBlock { Name = "贈った商品", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock
            {
                Purchases =
                [
                    new Purchase { VariationId = 1, Price = 1500, Kind = PurchaseKind.Given, Note = "誕生日" },
                    new Purchase { VariationId = 1, Price = 1500, Kind = PurchaseKind.Given },
                ],
            },
        };

        var snapshot = StatsService.Build([Item("1", price: 1000), given], new AvatarRegistry(), 0);

        // 自分用の支出には贈答を混ぜない
        Assert.Equal(1000, snapshot.SpentYen);
        Assert.Equal(2, snapshot.GivenCount);
        Assert.Equal(3000, snapshot.GivenSpentYen);
    }

    /// <summary>同じ版を3人に贈れば3回ぶんの額になる（旧形式では1回ぶんに潰れていた）。</summary>
    [Fact]
    public void CountsEveryGiftOccasionSeparately()
    {
        var given = new ItemRecord
        {
            Id = "gift",
            Booth = new BoothBlock { Name = "贈った商品", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock
            {
                Purchases = Enumerable.Range(0, 3)
                    .Select(_ => new Purchase { VariationId = 7, Price = 800, Kind = PurchaseKind.Given })
                    .ToList(),
            },
        };

        var snapshot = StatsService.Build([given], new AvatarRegistry(), 0);

        Assert.Equal(3, snapshot.GivenCount);
        Assert.Equal(2400, snapshot.GivenSpentYen);
    }

    [Fact]
    public void CountsGiftsSeparatelyFromFree()
    {
        var snapshot = Build(
            Item("1", price: 2000, gifted: true),
            Item("2", price: 0),
            Item("3", price: 500));

        Assert.Equal(1, snapshot.GiftedItemCount);
        Assert.Equal(2000, snapshot.GiftedValueYen);
        Assert.Equal(1, snapshot.FreeItemCount);
    }

    [Fact]
    public void GroupsSpendByCategory()
    {
        var snapshot = Build(
            Item("1", price: 1000, category: "3D衣装"),
            Item("2", price: 500, category: "3D衣装"),
            Item("3", price: 3000, category: "3Dキャラクター"));

        Assert.Equal("3Dキャラクター", snapshot.CategorySpend[0].Key);
        Assert.Equal(1500, snapshot.CategorySpend[1].SpentYen);
    }

    /// <summary>
    /// appTagのトップは複数選べるので、1つのitemが複数の分類に満額で入る。
    /// 合計は支出と一致しない（画面でその旨を書く）。
    /// </summary>
    [Fact]
    public void SpendByAppTagCountsAnItemInEachTag()
    {
        var snapshot = Build(Item("1", price: 1000, appTags: ["衣装", "ギミック"]));

        Assert.Equal(2, snapshot.AppTagSpend.Count);
        Assert.All(snapshot.AppTagSpend, bar => Assert.Equal(1000, bar.SpentYen));
    }

    [Fact]
    public void CountsUntaggedAsResidual()
    {
        var snapshot = Build(Item("1", appTags: ["衣装"]), Item("2"));

        var residual = Assert.Single(snapshot.AppTagCounts, bar => bar.IsResidual);
        Assert.Equal(1, residual.ItemCount);
    }

    [Fact]
    public void RanksHeavyItems()
    {
        var snapshot = Build(Item("1", size: 100), Item("2", size: 5000), Item("3", size: 900));

        Assert.Equal(["2", "3", "1"], snapshot.HeavyItems.Select(entry => entry.ItemId));
    }

    /// <summary>1点だけのショップと、2点以上のショップを分けて数える。</summary>
    [Fact]
    public void SplitsShopsByRepeat()
    {
        var snapshot = Build(InShop(Item("1", price: 100), "a"),
            InShop(Item("2", price: 100), "b"),
            InShop(Item("3", price: 100), "b"));

        Assert.Equal(1, snapshot.ShopsBoughtOnce);
        Assert.Equal(1, snapshot.ShopsBoughtMany);
        Assert.Equal("b", snapshot.ShopsByCount[0].Key);
    }

    [Fact]
    public void BuildsAttributeDistribution()
    {
        var snapshot = Build(
            Item("1", attributes: new() { ["かっこいい"] = 85 }),
            Item("2", attributes: new() { ["かっこいい"] = 10 }),
            Item("3"));

        var distribution = Assert.Single(snapshot.AttributeDistributions);
        Assert.Equal("かっこいい", distribution.Name);
        Assert.Equal(2, distribution.Rated);
        Assert.Equal(47.5, distribution.Average);
        Assert.Equal(1, distribution.Buckets[0]);
        Assert.Equal(1, distribution.Buckets[4]);
    }

    /// <summary>件数が足りない組は相関を出さない。少ないと数字が暴れる。</summary>
    [Fact]
    public void SkipsCorrelationWhenTooFewItems()
    {
        var items = Enumerable.Range(0, 5)
            .Select(i => Item(i.ToString(), attributes: new() { ["a"] = i * 10, ["b"] = i * 10 }))
            .ToArray();

        Assert.Empty(Build(items).AttributeCorrelations);
    }

    [Fact]
    public void FindsPerfectCorrelation()
    {
        var items = Enumerable.Range(0, 12)
            .Select(i => Item(i.ToString(), attributes: new() { ["a"] = i * 8, ["b"] = i * 8 }))
            .ToArray();

        var correlation = Assert.Single(Build(items).AttributeCorrelations);
        Assert.Equal(1.0, correlation.R, 3);
        Assert.Equal(12, correlation.Count);
    }

    [Fact]
    public void FindsNegativeCorrelation()
    {
        var items = Enumerable.Range(0, 12)
            .Select(i => Item(i.ToString(), attributes: new() { ["a"] = i * 8, ["b"] = 100 - (i * 8) }))
            .ToArray();

        Assert.Equal(-1.0, Assert.Single(Build(items).AttributeCorrelations).R, 3);
    }

    [Fact]
    public void CountsEndOfSaleAndSoldOut()
    {
        var snapshot = Build(
            Item("1", price: 1000, endOfSale: true),
            Item("2", soldOut: true),
            Item("3"));

        Assert.Equal(1, snapshot.EndOfSaleCount);
        Assert.Equal(1, snapshot.SoldOutCount);
        Assert.Equal(1000, snapshot.EndOfSaleSpentYen);
    }

    /// <summary>所持しているのに仕分けていないもの。積み残しの全件数とは対象が違う。</summary>
    [Fact]
    public void CountsUnsortedOwnedItems()
    {
        var snapshot = Build(Item("1", appTags: ["衣装"]), Item("2"), Item("3"));

        Assert.Equal(2, snapshot.UnsortedOwnedCount);
    }

    [Fact]
    public void CountsHiddenItems()
        => Assert.Equal(1, Build(Item("1", hidden: true), Item("2")).HiddenCount);

    [Fact]
    public void BuildsWishBuckets()
    {
        var snapshot = Build(Item("1", wish: 50), Item("2", wish: 3000), Item("3", wish: 0));

        Assert.Equal(1, snapshot.WishBuckets[0].Count);
        Assert.Contains(snapshot.WishBuckets, bucket => bucket.Count == 1 && bucket.Label.StartsWith("1,000"));
    }

    /// <summary>月ごとのカテゴリ構成。入手日が分かるものだけ。</summary>
    [Fact]
    public void BuildsMonthlyCategories()
    {
        var snapshot = Build(
            Item("1", category: "3D衣装", acquiredAt: "2026-01-10"),
            Item("2", category: "3D衣装", acquiredAt: "2026-01-20"),
            Item("3", category: "ギミック", acquiredAt: "2026-02-01"));

        Assert.Equal(2, snapshot.MonthlyCategories.Count);
        Assert.Equal(2, snapshot.MonthlyCategories[0].Total);
        Assert.Equal(2, snapshot.MonthlyCategories[0].Counts["3D衣装"]);
    }
}
