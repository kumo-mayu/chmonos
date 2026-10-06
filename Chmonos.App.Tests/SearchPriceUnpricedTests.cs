using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 価格の「価格が設定されていない商品も表示」（ユーザ判断 2026-10-06。既定は切・有料・無料の同じチェックとは同期しない）。
/// 「設定されていない」は元の数が1つも無いこと（D）。外れ値を外して残らない商品（G）は設定されているので足さない。
/// **除くときも足す**（文の「も表示」のとおり）。表は <see cref="SearchPriceMatchAllTests"/> と同じ7件（範囲 500〜1,000円・外れ値の境 5,000円）：
///
/// | 入れたとき | どれか | どれか・除く | 全て | 全て・除く |
/// |---|---|---|---|---|
/// | 切（前と同じ） | 1,2,5,6 | 3 | 1,6 | 2,3,5 |
/// | 入 | 1,2,4,5,6 | 3,4 | 1,4,6 | 2,3,4,5 |
/// </summary>
public class SearchPriceUnpricedTests
{
    private static readonly SearchModuleContext Context = new(
        () => throw new InvalidOperationException("対応アバターの索引は使わない"),
        ModificationUsage.Empty,
        RecentTimes.Empty,
        null,
        new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(9)));

    private static readonly Dictionary<string, int[]> Prices = new()
    {
        ["9900601"] = [800],
        ["9900602"] = [300, 800],
        ["9900603"] = [300],
        ["9900604"] = [],
        ["9900605"] = [800, 2000],
        ["9900606"] = [800, 99999],
        ["9900607"] = [99999],
    };

    private static readonly ItemRecord[] Items = [.. Prices.Keys.Select(id => Make.Item(id, "作り物 " + id))];

    private static RangeModule Price(bool matchAll, bool exclude, bool unpriced)
    {
        var module = new RangeModule(
            SearchModuleKind.Price,
            (item, _) => Prices[item.Id],
            "円",
            [new ChoiceOption("paid", "購入額"), new ChoiceOption("booth", "BOOTHの価格")])
        {
            AllValuesOf = _ => Enumerable.Repeat(1000, 20).Append(99999),
            Floor = 100,
            SupportsOutliers = true,
            SupportsMatchAll = true,
            SupportsUnpriced = true,
        };
        module.RefreshBounds();
        module.MinText = "500";
        module.MaxText = "1000";
        module.MatchAll = matchAll;
        module.IsExcluded = exclude;
        module.IncludeUnpriced = unpriced;
        return module;
    }

    private static string[] Passing(SearchModule module)
    {
        module.Prepare(Context);
        return Items.Where(item => module.Passes(item, Context)).Select(item => item.Id[^1..]).ToArray();
    }

    [Theory]
    [InlineData(false, false, false, "1,2,5,6")]
    [InlineData(false, true, false, "3")]
    [InlineData(true, false, false, "1,6")]
    [InlineData(true, true, false, "2,3,5")]
    [InlineData(false, false, true, "1,2,4,5,6")]
    [InlineData(false, true, true, "3,4")]
    [InlineData(true, false, true, "1,4,6")]
    [InlineData(true, true, true, "2,3,4,5")]
    public void 全てか_どれかか_除くか_と組み合わせて_価格の無い商品だけを足す(bool matchAll, bool exclude, bool unpriced, string expected)
    {
        Assert.Equal(expected.Split(','), Passing(Price(matchAll, exclude, unpriced)));
    }

    [Fact]
    public void 既定は切で_入れたことは状態に残り_読み直しても効く()
    {
        Assert.False(new RangeModule(SearchModuleKind.Price, (_, _) => [], "円") { SupportsUnpriced = true }.IncludeUnpriced);

        var state = Price(matchAll: false, exclude: false, unpriced: true).Save();
        Assert.True(state.Flag);

        var restored = Price(matchAll: false, exclude: false, unpriced: false);
        restored.Load(state);

        Assert.True(restored.IncludeUnpriced);
        Assert.Equal(["1", "2", "4", "5", "6"], Passing(restored));
        Assert.EndsWith("・価格が設定されていない商品も表示", restored.SummaryText);
    }

    [Fact]
    public void 条件をクリアすると切に戻り_切り替えを持たない条件では読まない()
    {
        var module = Price(matchAll: false, exclude: false, unpriced: true);
        module.Clear();
        Assert.False(module.IncludeUnpriced);

        var likes = new RangeModule(SearchModuleKind.WishList, (_, _) => [], string.Empty) { AllValuesOf = _ => [10] };
        likes.Load(new SearchModuleState { Kind = "WishList", Flag = true, Min = "0", Max = "10" });
        Assert.False(likes.IncludeUnpriced);
        Assert.False(likes.Save().Flag);
    }

    [Fact]
    public Task 検索の価格の条件は切り替えを持ち_払った額を入れていない商品を足せる() => TestApp.Run(async app =>
    {
        var item = Make.Item("9900611", "作り物");
        await app.AddItemAsync(item with { Local = item.Local with { Purchases = [new Purchase { Price = 500 }] } });
        await app.AddItemAsync(item with { Id = "9900612" });
        var search = (await app.StartAsync()).Search;
        var module = (RangeModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Price);

        Assert.True(module.SupportsUnpriced);
        Assert.Equal(["9900611"], search.ListItems.Select(card => card.Item.Id));

        module.IncludeUnpriced = true;
        Assert.Equal(["9900611", "9900612"], search.ListItems.Select(card => card.Item.Id).Order(StringComparer.Ordinal));
    });
}
