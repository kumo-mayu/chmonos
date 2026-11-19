using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>統計の上の帯の読み直し（↺）：画面を作った後に増えた商品が、読み直すと集計に入る。</summary>
public sealed class StatsRefreshTests
{
    private static ItemRecord Item(string id) => new()
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
            Purchases = [new Purchase { VariationId = 1, Price = 100, PurchasedAt = new DateOnly(2026, 11, 5) }],
        },
    };

    [Fact]
    public Task 読み直すと_開いた後に増えた商品が集計に入り_読み込み中を経て戻る() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Item("1000001"));
        var main = await app.StartAsync();
        var stats = new StatsViewModel(app.Services, main, new DateOnly(2026, 11, 19));
        await UiThread.Until(() => !stats.IsLoading && stats.Shops.Count > 0, "統計の集計");
        Assert.Equal("1", stats.OwnedText);

        await app.AddItemAsync(Item("1000002"));
        Assert.Equal("1", stats.OwnedText);

        stats.RefreshCommand.Execute(null);
        Assert.True(stats.IsLoading);

        await UiThread.Until(() => !stats.IsLoading && stats.OwnedText == "2", "読み直した集計");
        stats.OnLeaving();
        await app.SettleAsync();
    });

    [Fact]
    public Task 読み直しを続けて押しても_最後の読み込みが終われば読み込み中が下りる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Item("1000001"));
        var main = await app.StartAsync();
        var stats = new StatsViewModel(app.Services, main, new DateOnly(2026, 11, 19));
        await UiThread.Until(() => !stats.IsLoading && stats.Shops.Count > 0, "統計の集計");

        stats.RefreshCommand.Execute(null);
        stats.RefreshCommand.Execute(null);
        stats.RefreshCommand.Execute(null);

        await UiThread.Until(() => !stats.IsLoading && stats.HasData, "続けて読み直した集計");
        Assert.Equal("1", stats.OwnedText);
        stats.OnLeaving();
        await app.SettleAsync();
    });
}
