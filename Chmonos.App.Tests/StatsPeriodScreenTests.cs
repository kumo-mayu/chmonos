using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 統計の期間（open.md 104）：期間を替えると期間で変わる欄だけが変わり、タイルは合計と期間の両方、
/// 見出しの札は選んだ期間の実際の日付を出す。グラフの棒は期間の長さで月か年。
/// </summary>
public sealed class StatsPeriodScreenTests
{
    private static readonly DateOnly Today = new(2026, 11, 19);

    private static ItemRecord Item(string id, string shop, int price, DateOnly? at) => new()
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
            LocalFiles = [new LocalFileRecord { Hash = "h" + id, Paths = [@"C:\none\" + id + ".zip"], SizeBytes = 1 }],
            Purchases = [new Purchase { VariationId = 1, Price = price, PurchasedAt = at }],
        },
    };

    private static StatsRangeOption Option(StatsRangeKind kind, int months = 0)
        => StatsViewModel.Ranges.First(option => option.Kind == kind && (months == 0 || option.Months == months));

    [Fact]
    public Task 期間を替えると期間で変わる欄だけが変わる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Item("1", "now", 100, new DateOnly(2026, 11, 5)));
        await app.AddItemAsync(Item("2", "old", 300, new DateOnly(2025, 3, 1)));
        await app.AddItemAsync(Item("3", "nodate", 500, null));
        var main = await app.StartAsync();

        var stats = new StatsViewModel(app.Services, main, Today);
        await UiThread.Until(() => !stats.IsLoading && stats.Shops.Count > 0, "統計の集計");

        // 既定は直近12ヶ月
        Assert.Equal("2025年12月〜2026年11月", stats.PeriodText);
        Assert.True(stats.ShowsPeriodTiles);
        Assert.Equal("3", stats.OwnedText);
        Assert.Equal("2025年12月〜2026年11月 1", stats.PeriodOwnedText);
        Assert.Equal("2025年12月〜2026年11月 ¥100", stats.PeriodSpentText);
        Assert.Equal(["now"], stats.Shops.Select(row => row.Label));
        Assert.Equal(12, stats.Chart.Count);
        Assert.Contains("1 件", stats.PeriodNote);

        // 全期間：絞らない（日付の分からない商品も入る）。最初に買った月から今月まで21ヶ月なので月で並べる
        stats.Range = Option(StatsRangeKind.All);
        await UiThread.Until(() => stats.Shops.Count == 3, "全期間の集計");
        Assert.Equal("全期間", stats.PeriodText);
        Assert.False(stats.ShowsPeriodTiles);
        Assert.False(stats.HasPeriodNote);
        Assert.Equal(21, stats.Chart.Count);

        // 去年
        stats.Range = Option(StatsRangeKind.LastYear);
        await UiThread.Until(() => stats.Shops.Count == 1 && stats.Shops[0].Label == "old", "去年の集計");
        Assert.Equal("2025年", stats.PeriodText);
        Assert.Equal(12, stats.Chart.Count);
        Assert.Equal(300, stats.Chart.Sum(bar => bar.SpentYen));

        // 年を選ぶ：候補は購入のある年と今年
        Assert.Equal([2026, 2025], stats.Years);
        stats.Range = Option(StatsRangeKind.PickYear);
        await UiThread.Until(() => stats.Shops.Count == 1 && stats.Shops[0].Label == "now", "今年の集計");
        stats.SelectedYear = 2025;
        await UiThread.Until(() => stats.Shops.Count == 1 && stats.Shops[0].Label == "old", "選んだ年の集計");

        // 開始と終了：終了を空にすると終わりを切らない
        stats.Range = Option(StatsRangeKind.Custom);
        stats.CustomFrom = new DateTime(2025, 1, 1);
        stats.CustomTo = null;
        await UiThread.Until(() => stats.Shops.Count == 2, "開始だけ選んだ期間の集計");
        Assert.Equal("終了の日", stats.CustomToText);

        stats.OnLeaving();
        await app.SettleAsync();
    });

    /// <summary>読み込みの途中で期間を替えても、読み込みが済んだ後は替えた期間で数える（始めた時の期間のまま残さない）</summary>
    [Fact]
    public Task 読み込みの途中で替えた期間で数える() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Item("1", "now", 100, new DateOnly(2026, 11, 5)));
        await app.AddItemAsync(Item("2", "old", 300, new DateOnly(2020, 3, 1)));
        var main = await app.StartAsync();

        var stats = new StatsViewModel(app.Services, main, Today);
        stats.Range = Option(StatsRangeKind.All);
        await UiThread.Until(() => !stats.IsLoading && stats.Shops.Count == 2, "全期間の集計");
        Assert.Equal("全期間", stats.PeriodText);
        stats.OnLeaving();
        await app.SettleAsync();
    });

    /// <summary>24ヶ月を超える期間は年で並べる</summary>
    [Fact]
    public Task 長い期間は年で並べる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Item("1", "a", 100, new DateOnly(2020, 5, 1)));
        await app.AddItemAsync(Item("2", "b", 200, new DateOnly(2026, 1, 1)));
        var main = await app.StartAsync();

        var stats = new StatsViewModel(app.Services, main, Today);
        await UiThread.Until(() => !stats.IsLoading && stats.Shops.Count > 0, "統計の集計");
        stats.Range = Option(StatsRangeKind.All);
        await UiThread.Until(() => stats.Shops.Count == 2, "全期間の集計");

        Assert.Equal(["2020", "2021", "2022", "2023", "2024", "2025", "2026"], stats.Chart.Select(bar => bar.Label));
        stats.OnLeaving();
        await app.SettleAsync();
    });
}
