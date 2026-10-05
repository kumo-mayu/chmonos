using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索の条件「入手日」は、購入記録ごとの日付のどれか1件が範囲に入れば当たる（メモ45・2-A）。ファイルの日付では代えない。
/// </summary>
public class SearchPurchaseDateTests
{
    private static IEnumerable<string> ShownIds(SearchViewModel search) => search.ListItems.Select(card => card.Item.Id).Order();

    private static ItemRecord Item(string id, DateOnly? acquired, params Purchase[] purchases)
    {
        var item = Make.Item(id, "作り物 " + id);
        return item with { Local = item.Local with { AcquiredAt = acquired, Purchases = [.. purchases] } };
    }

    private static Purchase Buy(DateOnly? at = null) => new() { VariationId = 1, Price = 500, PurchasedAt = at };

    private static readonly DateOnly June2024 = new(2024, 6, 1);

    /// <summary>
    /// 表：購入の日付あり／なし × 商品の日付あり／なし／ファイルの日付だけ（作り物のファイルは在らないので、日付なしと同じく外れる）。
    /// </summary>
    private static async Task<SearchViewModel> StartAsync(TestApp app)
    {
        // 入手日は2024年6月、2025年3月に別の種類を買い足した
        await app.AddItemAsync(Item("9900101", June2024, Buy(), Buy(new DateOnly(2025, 3, 10))));
        // 入手日なし、買った日だけ
        await app.AddItemAsync(Item("9900102", null, Buy(new DateOnly(2025, 3, 20))));
        // 購入記録なし、入手日だけ
        await app.AddItemAsync(Item("9900103", new DateOnly(2025, 3, 5)));
        // 入手日だけ（買った日なし）で範囲の外
        await app.AddItemAsync(Item("9900104", June2024, Buy()));
        // 日付なし（ファイルだけ）
        await app.AddItemAsync(Item("9900105", null, Buy()));
        return (await app.StartAsync()).Search;
    }

    [Fact]
    public Task 買い足した月で絞ると_その購入を持つ商品が当たる() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);

        var module = (DateModule)SearchModuleMenuTests.Add(search, SearchModuleKind.AcquiredAt);
        module.SinceText = "2025-03";
        module.TillText = "2025-03";

        await UiThread.Until(() => search.ResultSummary == "3 件", "2025年3月で絞り直す");
        Assert.Equal(["9900101", "9900102", "9900103"], ShownIds(search));
    });

    [Fact]
    public Task 除くと_どの日付も範囲に入らない商品だけが残る() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);

        var module = (DateModule)SearchModuleMenuTests.Add(search, SearchModuleKind.AcquiredAt);
        module.SinceText = "2025-03";
        module.TillText = "2025-03";
        module.IsExcluded = true;

        // 日付の分からない商品は除くときも外す（ユーザ判断 2026-10-01）
        await UiThread.Until(() => search.ResultSummary == "1 件", "2025年3月を除いて絞り直す");
        Assert.Equal(["9900104"], ShownIds(search));
    });

    [Fact]
    public Task 足したときの範囲は_購入の日付も含めた一番古い日から一番新しい日() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);

        var module = (DateModule)SearchModuleMenuTests.Add(search, SearchModuleKind.AcquiredAt);

        Assert.Equal("2024-06-01", module.SinceText);
        Assert.Equal("2025-03-20", module.TillText);
    });
}
