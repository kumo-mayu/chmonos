using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>統計の期間（open.md 104）：期間の決め方と、期間で絞った集計の決まり。</summary>
public class StatsPeriodsTests
{
    private static readonly DateOnly Today = new(2026, 11, 19);

    private static Purchase Bought(int price, DateOnly? at, PurchaseKind kind = PurchaseKind.ForSelf, int variation = 1)
        => new() { VariationId = variation, Price = price, PurchasedAt = at, Kind = kind };

    private static ItemRecord Item(string id, string shop, DateOnly? itemDate, params Purchase[] purchases) => new()
    {
        Id = id,
        Booth = new BoothBlock
        {
            Name = "item " + id,
            FetchedAt = DateTimeOffset.Now,
            Shop = new BoothShop { Subdomain = shop, Name = shop, Url = $"https://{shop}.booth.pm/" },
        },
        Local = new LocalBlock
        {
            AcquiredAt = itemDate,
            LocalFiles = [new LocalFileRecord { Hash = id, Paths = ["x" + id], SizeBytes = 1 }],
            Purchases = purchases,
        },
    };

    private static ItemRecord Unowned(ItemRecord item) => item with { Local = item.Local with { LocalFiles = [] } };

    private static StatsSnapshot Period(StatsDateRange range, params ItemRecord[] items)
        => StatsPeriods.Build(StatsPeriods.Narrow(StatsPeriods.Date(items), range), new AvatarRegistry(), 0, true);

    // ---- 期間の決め方 ----

    [Fact]
    public void 直近12ヶ月は今月を含む月初から月末まで()
    {
        Assert.Equal(new StatsDateRange(new(2025, 12, 1), new(2026, 11, 30)), StatsPeriods.LastMonths(Today, 12));
        Assert.Equal(new StatsDateRange(new(2024, 12, 1), new(2026, 11, 30)), StatsPeriods.LastMonths(Today, 24));
    }

    /// <summary>入手日が先の日付になっている購入があれば、その月まで伸ばす（前のグラフと同じ）</summary>
    [Fact]
    public void 先の日付の購入があればその月まで伸ばす()
        => Assert.Equal(new StatsDateRange(new(2026, 2, 1), new(2027, 1, 31)), StatsPeriods.LastMonths(Today, 12, new DateOnly(2027, 1, 5)));

    [Fact]
    public void 今月_先月_年()
    {
        Assert.Equal(new StatsDateRange(new(2026, 11, 1), new(2026, 11, 30)), StatsPeriods.ThisMonth(Today));
        Assert.Equal(new StatsDateRange(new(2026, 10, 1), new(2026, 10, 31)), StatsPeriods.LastMonth(Today));
        Assert.Equal(new StatsDateRange(new(2025, 12, 1), new(2025, 12, 31)), StatsPeriods.LastMonth(new DateOnly(2026, 1, 10)));
        Assert.Equal(new StatsDateRange(new(2025, 1, 1), new(2025, 12, 31)), StatsPeriods.Year(2025));
    }

    [Fact]
    public void 開始と終了が逆なら入れ替える()
        => Assert.Equal(new StatsDateRange(new(2025, 1, 1), new(2025, 3, 1)), StatsPeriods.Between(new(2025, 3, 1), new(2025, 1, 1)));

    [Fact]
    public void 見出しの字()
    {
        Assert.Equal("2025年", StatsPeriods.Label(StatsPeriods.Year(2025)));
        Assert.Equal("2026年10月", StatsPeriods.Label(StatsPeriods.LastMonth(Today)));
        Assert.Equal("2025年12月〜2026年11月", StatsPeriods.Label(StatsPeriods.LastMonths(Today, 12)));
        Assert.Equal("2025/01/05〜2025/02/03", StatsPeriods.Label(StatsPeriods.Between(new(2025, 1, 5), new(2025, 2, 3))));
    }

    [Fact]
    public void 二十四ヶ月までは月で_それより長いと年で並べる()
    {
        Assert.True(StatsPeriods.ByMonth(StatsPeriods.LastMonths(Today, 24)));
        Assert.False(StatsPeriods.ByMonth(StatsPeriods.LastMonths(Today, 25)));
    }

    // ---- 期間で絞った集計 ----

    [Fact]
    public void 期間に手に入れた商品だけを数え_支出は期間の購入だけ()
    {
        var range = StatsPeriods.Year(2025);
        var snapshot = Period(range,
            Item("1", "a", null, Bought(100, new(2024, 5, 1)), Bought(200, new(2025, 5, 1), variation: 2)),
            Item("2", "b", null, Bought(300, new(2024, 5, 1))),
            Item("3", "c", new DateOnly(2025, 7, 1)));

        Assert.Equal(2, snapshot.OwnedCount);
        Assert.Equal(200, snapshot.SpentYen);
        Assert.Equal(2, snapshot.ShopCount);
    }

    /// <summary>グラフの棒の支出の和は、期間の支出のタイルと同じ。期間の外の月は混ざらない</summary>
    [Fact]
    public void 棒の支出の和は期間の支出と同じで_期間の外の月が混ざらない()
    {
        var range = StatsPeriods.Between(new(2025, 3, 15), new(2025, 6, 10));
        var snapshot = Period(range,
            Item("1", "a", null, Bought(100, new(2025, 3, 1)), Bought(200, new(2025, 4, 1), variation: 2)),
            Item("2", "a", null, Bought(300, new(2025, 6, 9)), Bought(999, new(2025, 6, 20), variation: 2)));

        Assert.Equal(500, snapshot.SpentYen);
        Assert.Equal(snapshot.SpentYen, snapshot.Months.Sum(month => month.SpentYen));
        Assert.Equal(snapshot.SpentYen, snapshot.Years.Sum(year => year.SpentYen));
        Assert.DoesNotContain(snapshot.Months, month => month.SpentYen > 0 && (string.CompareOrdinal(month.Key, "2025-03") < 0 || string.CompareOrdinal(month.Key, "2025-06") > 0));
    }

    /// <summary>2つの月で買った商品も、期間の商品数では1件（棒の件数の和ではない）</summary>
    [Fact]
    public void 二つの月で買った商品は期間の商品数で1件()
    {
        var snapshot = Period(StatsPeriods.Year(2025),
            Item("1", "a", null, Bought(100, new(2025, 3, 1)), Bought(200, new(2025, 4, 1), variation: 2)));

        Assert.Equal(1, snapshot.OwnedCount);
        Assert.Equal(2, snapshot.Months.Sum(month => month.ItemCount));
    }

    [Fact]
    public void 日付の分からない商品はどの期間にも入らず_数は控える()
    {
        var undated = Item("1", "a", null, Bought(100, null));
        var narrowed = StatsPeriods.Narrow(StatsPeriods.Date([undated]), StatsPeriods.LastMonths(Today, 1200));

        Assert.Empty(narrowed.Items);
        Assert.Equal(1, narrowed.UndatedOwnedCount);
    }

    /// <summary>贈った分は所持していない商品も含め、贈った日で数える。期間の所持・支出には入れない</summary>
    [Fact]
    public void 贈った分は贈った日で数え_所持していない商品も入る()
    {
        var range = StatsPeriods.Year(2025);
        var snapshot = Period(range,
            Unowned(Item("1", "a", null, Bought(500, new(2025, 2, 1), PurchaseKind.Given))),
            Item("2", "b", null, Bought(100, new(2024, 2, 1)), Bought(700, new(2025, 2, 1), PurchaseKind.Given, variation: 2)),
            Item("3", "c", null, Bought(900, new(2024, 2, 1), PurchaseKind.Given)));

        Assert.Equal(2, snapshot.GivenCount);
        Assert.Equal(1200, snapshot.GivenSpentYen);
        Assert.Equal(0, snapshot.OwnedCount);
        Assert.Equal(0, snapshot.SpentYen);
    }

    /// <summary>期間で切っても、期間の中に全部の購入がある商品は元と同じ値（全期間に当たる長い期間で、全期間の集計と一致する）</summary>
    [Fact]
    public void 全部を含む期間は全期間の集計と同じ()
    {
        ItemRecord[] items =
        [
            Item("1", "a", null, Bought(100, new(2024, 5, 1)), Bought(200, new(2025, 5, 1), variation: 2)),
            Item("2", "b", new DateOnly(2023, 1, 1)),
            Item("3", "b", null, Bought(300, new(2025, 1, 1), PurchaseKind.Received)),
            Unowned(Item("4", "c", null, Bought(400, new(2025, 1, 1), PurchaseKind.Given))),
        ];
        var all = StatsService.Build(items, new AvatarRegistry(), 0);
        var wide = Period(StatsPeriods.Between(new(2000, 1, 1), new(2100, 12, 31)), items);

        Assert.Equal(all.OwnedCount, wide.OwnedCount);
        Assert.Equal(all.SpentYen, wide.SpentYen);
        Assert.Equal(all.ShopCount, wide.ShopCount);
        Assert.Equal(all.GivenSpentYen, wide.GivenSpentYen);
        Assert.Equal(all.GiftedCount, wide.GiftedCount);
        Assert.Equal(all.UnpricedItemCount, wide.UnpricedItemCount);
    }
}
