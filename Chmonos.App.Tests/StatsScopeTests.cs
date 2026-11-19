using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>統計の上の帯の「非表示を含む」「R-18を含む」（ユーザ判断 2026-11-20）：切ると数から外れ、値は設定に残る。</summary>
public sealed class StatsScopeTests
{
    private static ItemRecord Item(string id, bool hidden = false, bool adult = false) => new()
    {
        Id = id,
        Booth = new BoothBlock
        {
            Name = "item " + id,
            IsAdult = adult,
            FetchedAt = DateTimeOffset.Now,
            Shop = new BoothShop { Subdomain = "s", Name = "s", Url = "https://s.booth.pm/" },
        },
        Local = new LocalBlock
        {
            IsHidden = hidden,
            LocalFiles = [new LocalFileRecord { Hash = "h" + id, Paths = [@"C:\none\" + id + ".zip"], SizeBytes = 1 }],
            Purchases = [new Purchase { VariationId = 1, Price = 100, PurchasedAt = new DateOnly(2026, 11, 5) }],
        },
    };

    [Fact]
    public Task 既定では全部数え_切ると外れ_設定に残る() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Item("1000001"));
        await app.AddItemAsync(Item("1000002", hidden: true));
        await app.AddItemAsync(Item("1000003", adult: true));
        var main = await app.StartAsync();
        var stats = new StatsViewModel(app.Services, main, new DateOnly(2026, 11, 19));
        await UiThread.Until(() => !stats.IsLoading && stats.Shops.Count > 0, "統計の集計");
        Assert.Equal("3", stats.OwnedText);
        Assert.True(stats.IncludeHidden);
        Assert.True(stats.IncludeAdult);

        stats.IncludeHidden = false;
        await UiThread.Until(() => !stats.IsLoading && stats.OwnedText == "2", "非表示を外した集計");

        stats.IncludeAdult = false;
        await UiThread.Until(() => !stats.IsLoading && stats.OwnedText == "1", "R-18も外した集計");

        // 値は設定に残る（画面を作り直しても同じ）。検索の「R-18を表示」は変わらない
        await UiThread.Until(() => !app.Services.Settings.StatsIncludeHidden && !app.Services.Settings.StatsIncludeAdult, "設定への書き込み");
        Assert.True(app.Services.Settings.ShowAdult);
        stats.OnLeaving();

        var again = new StatsViewModel(app.Services, main, new DateOnly(2026, 11, 19));
        await UiThread.Until(() => !again.IsLoading && again.Shops.Count > 0, "作り直した統計の集計");
        Assert.False(again.IncludeHidden);
        Assert.Equal("1", again.OwnedText);

        again.IncludeHidden = true;
        again.IncludeAdult = true;
        await UiThread.Until(() => !again.IsLoading && again.OwnedText == "3", "戻した集計");
        again.OnLeaving();
        await app.SettleAsync();
    });
}
