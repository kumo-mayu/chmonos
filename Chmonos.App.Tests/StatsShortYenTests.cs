using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>統計の軸の金額の短い表記（設定「千・万・億」・ユーザ判断 2026-11-20）。</summary>
public sealed class StatsShortYenTests
{
    private static ItemRecord Item() => new()
    {
        Id = "1000001",
        Booth = new BoothBlock { Name = "item", FetchedAt = DateTimeOffset.Now },
        Local = new LocalBlock
        {
            LocalFiles = [new LocalFileRecord { Hash = "h1", Paths = [@"C:\none\a.zip"], SizeBytes = 1 }],
            Purchases = [new Purchase { VariationId = 1, Price = 12_000, PurchasedAt = new DateOnly(2026, 11, 5) }],
        },
    };

    [Theory]
    [InlineData(false, "¥15k")]
    [InlineData(true, "¥1.5万")]
    public Task 軸の額は設定の表記で出る(bool kanji, string expectedTop) => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Item());
        await app.ChangeSettingsAsync(settings => settings with { ShortYenInKanji = kanji });
        var main = await app.StartAsync();
        var stats = new StatsViewModel(app.Services, main, new DateOnly(2026, 11, 19));
        await UiThread.Until(() => !stats.IsLoading && stats.OwnedText == "1", "統計の集計");

        Assert.Equal(expectedTop, stats.AxisTopText);
        stats.OnLeaving();
        await app.SettleAsync();
    });
}
