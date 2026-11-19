using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>統計のタイル「ギフトで貰った額」：貰った回が1つ以上ある時だけ出し、額は定価の合計（自分の支出には混ぜない）。</summary>
public sealed class StatsGiftedTileTests
{
    private static ItemRecord Item(string id, PurchaseKind kind, int price) => new()
    {
        Id = id,
        Booth = new BoothBlock
        {
            Name = "item " + id,
            FetchedAt = DateTimeOffset.Now,
            Shop = new BoothShop { Subdomain = "s", Name = "s", Url = "https://s.booth.pm/" },
        },
        Local = new LocalBlock
        {
            LocalFiles = [new LocalFileRecord { Hash = "h" + id, Paths = [@"C:\none\" + id + ".zip"], SizeBytes = 1 }],
            Purchases = [new Purchase { VariationId = 1, Price = price, Kind = kind, PurchasedAt = new DateOnly(2026, 11, 5) }],
        },
    };

    [Fact]
    public Task 貰った物が無ければタイルは出さない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Item("1", PurchaseKind.ForSelf, 100));
        var main = await app.StartAsync();
        var stats = new StatsViewModel(app.Services, main, new DateOnly(2026, 11, 19));
        await UiThread.Until(() => !stats.IsLoading && stats.Shops.Count > 0, "統計の集計");
        Assert.False(stats.HasGifted);
        Assert.Equal(4, stats.TileColumns);
        stats.OnLeaving();
        await app.SettleAsync();
    });

    [Fact]
    public Task 貰った額と件数がタイルに出て_支出には混ざらない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Item("1", PurchaseKind.ForSelf, 100));
        await app.AddItemAsync(Item("2", PurchaseKind.Received, 2000));
        await app.AddItemAsync(Item("3", PurchaseKind.Received, 500));
        var main = await app.StartAsync();
        var stats = new StatsViewModel(app.Services, main, new DateOnly(2026, 11, 19));
        await UiThread.Until(() => !stats.IsLoading && stats.Shops.Count > 0, "統計の集計");
        Assert.True(stats.HasGifted);
        Assert.Equal("¥2,500", stats.GiftedText);
        Assert.Equal("貰った 2 件・定価の合計", stats.GiftedSubText);
        Assert.Equal("¥100", stats.SpentText);
        Assert.Equal(5, stats.TileColumns);
        stats.OnLeaving();
        await app.SettleAsync();
    });
}
