using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

public class StatsServiceTests
{
    private static StatsSnapshot Build(IEnumerable<ItemRecord> items, AvatarRegistry? registry = null)
        => StatsService.Build(items.ToList(), registry ?? new AvatarRegistry(), unresolvedCount: 0);

    private static ItemRecord Item(
        string id,
        LocalBlock? local = null,
        string? shop = null,
        string? category = null,
        bool isAdult = false)
        => new()
        {
            Id = id,
            Booth = new BoothBlock
            {
                Name = "item " + id,
                FetchedAt = DateTimeOffset.Now,
                IsAdult = isAdult,
                Category = category is null ? null : new BoothCategory { Id = 1, Name = category },
                Shop = shop is null
                    ? null
                    : new BoothShop { Subdomain = shop, Name = shop, Url = $"https://{shop}.booth.pm/" },
            },
            Local = local ?? new LocalBlock(),
        };

    private static LocalBlock Owned(
        int? price = null,
        string? acquiredAt = null,
        long size = 100,
        int copies = 1,
        bool gifted = false)
        => new()
        {
            LocalFiles =
            [
                new LocalFileRecord
                {
                    Hash = Guid.NewGuid().ToString("N"),
                    Paths = Enumerable.Range(0, copies).Select(index => $"x{index}").ToList(),
                    SizeBytes = size,
                },
            ],
            Purchases = price is null
                ? []
                : [new Purchase
                {
                    VariationId = 1,
                    Price = price,
                    Kind = gifted ? PurchaseKind.Received : PurchaseKind.ForSelf,
                }],
            AcquiredAt = acquiredAt is null ? null : DateOnly.Parse(acquiredAt),
        };

    [Fact]
    public void CountsOnlyItemsWithFiles()
    {
        var snapshot = Build([
            Item("1", Owned(price: 500)),
            Item("2"),
        ]);

        Assert.Equal(1, snapshot.OwnedCount);
        Assert.Equal(2, snapshot.KnownCount);
        Assert.Equal(500, snapshot.SpentYen);
    }

    /// <summary>非表示とR-18は統計には含める。表示設定で総額が変わると資産の把握に使えない。</summary>
    [Fact]
    public void IncludesHiddenAndAdultItems()
    {
        var hidden = Owned(price: 300, acquiredAt: "2026-01-05") with { IsHidden = true };

        var snapshot = Build([
            Item("1", Owned(price: 200, acquiredAt: "2026-01-05")),
            Item("2", hidden),
            Item("3", Owned(price: 100, acquiredAt: "2026-01-05"), isAdult: true),
        ]);

        Assert.Equal(3, snapshot.OwnedCount);
        Assert.Equal(600, snapshot.SpentYen);
    }

    [Fact]
    public void ExcludesGiftedFromSpending()
    {
        var snapshot = Build([
            Item("1", Owned(price: 800, gifted: true)),
            Item("2", Owned(price: 200)),
        ]);

        Assert.Equal(200, snapshot.SpentYen);
        Assert.Equal(1, snapshot.GiftedCount);
    }

    /// <summary>0円配布は支出0だが件数としては数える。内訳が読めるようにするため。</summary>
    [Fact]
    public void CountsFreeVariationsSeparately()
    {
        var snapshot = Build([Item("1", Owned(price: 0))]);

        Assert.Equal(0, snapshot.SpentYen);
        Assert.Equal(1, snapshot.FreeCount);
    }

    /// <summary>有償の購入記録が無いものは、0円として黙って混ぜずに件数として出す。</summary>
    [Fact]
    public void CountsItemsWithoutPriceRecord()
    {
        var snapshot = Build([
            Item("1", Owned(price: 500)),
            Item("2", Owned()),
            Item("3", Owned(price: 0)),
        ]);

        Assert.Equal(2, snapshot.UnpricedItemCount);
    }

    /// <summary>同じ中身を2箇所に置くと、論理容量は1つ分・実占有量は2つ分になる。</summary>
    [Fact]
    public void SeparatesLogicalAndPhysicalSize()
    {
        var snapshot = Build([Item("1", Owned(size: 1000, copies: 2))]);

        Assert.Equal(1000, snapshot.LogicalBytes);
        Assert.Equal(2000, snapshot.PhysicalBytes);
        Assert.Equal(1000, snapshot.DuplicateBytes);
    }

    /// <summary>買っていない月も残す。詰めると間が空いたことが読めなくなる。</summary>
    [Fact]
    public void KeepsEmptyMonthsBetweenPurchases()
    {
        var snapshot = Build([
            Item("1", Owned(price: 100, acquiredAt: "2026-01-10")),
            Item("2", Owned(price: 300, acquiredAt: "2026-04-02")),
        ]);

        Assert.Equal(["2026-01", "2026-02", "2026-03", "2026-04"], snapshot.Months.Select(month => month.Key));
        Assert.Equal([100, 0, 0, 300], snapshot.Months.Select(month => month.SpentYen));
    }

    [Fact]
    public void GroupsByYearAsWell()
    {
        var snapshot = Build([
            Item("1", Owned(price: 100, acquiredAt: "2024-01-10")),
            Item("2", Owned(price: 300, acquiredAt: "2026-04-02")),
        ]);

        Assert.Equal(["2024", "2025", "2026"], snapshot.Years.Select(year => year.Key));
        Assert.Equal([100, 0, 300], snapshot.Years.Select(year => year.SpentYen));
    }

    /// <summary>日付を決められなかった分は、どこかの月に押し込まず別に出す。</summary>
    [Fact]
    public void KeepsUndatedSpendingOutOfTheTimeline()
    {
        var snapshot = Build([
            Item("1", Owned(price: 100, acquiredAt: "2026-01-10")),
            Item("2", Owned(price: 900)),
        ]);

        Assert.Equal(1000, snapshot.SpentYen);
        Assert.Equal(900, snapshot.UndatedSpentYen);
        Assert.Equal(1, snapshot.UndatedCount);
        Assert.Equal(100, snapshot.Months.Sum(month => month.SpentYen));
    }

    [Fact]
    public void RanksShopsBySpending()
    {
        var snapshot = Build([
            Item("1", Owned(price: 100), shop: "alpha"),
            Item("2", Owned(price: 900), shop: "beta"),
            Item("3", Owned(price: 50), shop: "beta"),
        ]);

        Assert.Equal(["beta", "alpha"], snapshot.Shops.Select(shop => shop.Key));
        Assert.Equal(950, snapshot.Shops[0].SpentYen);
        Assert.Equal(2, snapshot.Shops[0].ItemCount);
        Assert.Equal(2, snapshot.ShopCount);
    }

    [Fact]
    public void GroupsSizeByCategory()
    {
        var snapshot = Build([
            Item("1", Owned(size: 500), category: "3D衣装"),
            Item("2", Owned(size: 1500), category: "3Dキャラクター"),
            Item("3", Owned(size: 200)),
        ]);

        Assert.Equal(["3Dキャラクター", "3D衣装", "分類なし"], snapshot.Categories.Select(entry => entry.Key));
        Assert.Equal(1500, snapshot.Categories[0].Bytes);
    }

    /// <summary>同じアバターに複数の紐付けがあっても1件。推定と手入力が重なることがある。</summary>
    [Fact]
    public void CountsEachAvatarOncePerItem()
    {
        var local = Owned() with
        {
            Avatars =
            [
                new AvatarLink { AvatarItemId = "100", Source = AvatarLinkSource.H2Link },
                new AvatarLink { AvatarItemId = "100", Source = AvatarLinkSource.Manual },
            ],
        };

        var snapshot = Build([Item("1", local)]);

        Assert.Single(snapshot.Avatars);
        Assert.Equal(1, snapshot.Avatars[0].ItemCount);
    }

    /// <summary>素体向けの衣装は、その素体を使っているアバターにも数える。</summary>
    [Fact]
    public void RollsBaseAvatarUpToDerivedAvatars()
    {
        // 素体は「名前のグループ」で、商品として配布されている場合だけIDを持つ
        var registry = new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "base", DisplayName = "素体X", BaseName = "素体X" },
                new AvatarRegistryEntry { ItemId = "derived", DisplayName = "セラフィム", BaseName = "素体X" },
            ],
            BaseGroups = [new AvatarBaseGroup { Name = "素体X", ItemId = "base" }],
        };

        var local = Owned() with
        {
            Avatars = [new AvatarLink { AvatarItemId = "base", Source = AvatarLinkSource.Manual }],
        };

        var snapshot = Build([Item("1", local)], registry);

        Assert.Equal(2, snapshot.Avatars.Count);
        Assert.Contains(snapshot.Avatars, bar => bar.Label == "素体X");
        Assert.Contains(snapshot.Avatars, bar => bar.Label == "セラフィム");
        Assert.All(snapshot.Avatars, bar => Assert.Equal(1, bar.ItemCount));
    }

    /// <summary>紐付けが1件も無いうちは「未設定」だけの棒を出しても読めないので出さない。</summary>
    [Fact]
    public void OmitsResidualRowWhenNoAvatarIsLinked()
    {
        var snapshot = Build([Item("1", Owned())]);

        Assert.Empty(snapshot.Avatars);
        Assert.False(snapshot.HasAnyAvatarLink);
    }

    [Fact]
    public void AddsResidualRowWhenSomeAvatarsAreLinked()
    {
        var linked = Owned() with
        {
            Avatars = [new AvatarLink { AvatarItemId = "100", Name = "マヌカ", Source = AvatarLinkSource.Manual }],
        };

        var snapshot = Build([Item("1", linked), Item("2", Owned())]);

        Assert.Equal(2, snapshot.Avatars.Count);
        Assert.True(snapshot.Avatars[^1].IsResidual);
        Assert.Equal(1, snapshot.Avatars[^1].ItemCount);
    }

    /// <summary>積み残しは所持していないものも数える。編集の対象になるため。</summary>
    [Fact]
    public void CountsBacklogAcrossAllItems()
    {
        var tagged = Owned() with { UserTags = [new UserTagAssignment { Top = "衣装" }] };
        var missing = new LocalBlock
        {
            LocalFiles = [new LocalFileRecord { Hash = "AAAA", Paths = [], SizeBytes = 10 }],
            UserTags = [new UserTagAssignment { Top = "衣装" }],
        };

        var snapshot = StatsService.Build(
            [Item("1", tagged), Item("2"), Item("3", missing)],
            new AvatarRegistry(),
            unresolvedCount: 4);

        Assert.Equal(4, snapshot.Backlog.UnresolvedCount);
        Assert.Equal(1, snapshot.Backlog.NeedsUserTagCount);
        Assert.Equal(1, snapshot.Backlog.MissingFileCount);
    }

    [Fact]
    public void ReturnsEmptySeriesWhenNothingIsOwned()
    {
        var snapshot = Build([Item("1")]);

        Assert.Equal(0, snapshot.OwnedCount);
        Assert.Empty(snapshot.Months);
        Assert.Empty(snapshot.Years);
        Assert.Empty(snapshot.Shops);
    }
}
