using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 統計の「ショップ」の欄（open.md 107）：金額・商品数・スキ数で並べ替え、右端は選んだ値、後ろにほかの1つを添える。
/// 選んだ物は開き直した画面にも引き継ぐ（アプリを閉じるまで）。
/// </summary>
public sealed class StatsShopOrderTests
{
    private static ItemRecord Item(string id, string shop, int price, int wish) => new()
    {
        Id = id,
        Booth = new BoothBlock
        {
            Name = "item " + id,
            FetchedAt = DateTimeOffset.Now,
            WishListsCount = wish,
            Shop = new BoothShop { Subdomain = shop, Name = shop, Url = $"https://{shop}.booth.pm/" },
        },
        Local = new LocalBlock
        {
            LocalFiles = [new LocalFileRecord { Hash = "h" + id, Paths = [@"C:\none\" + id + ".zip"], SizeBytes = 1 }],
            Purchases = [new Purchase { VariationId = 1, Price = price }],
        },
    };

    [Fact]
    public Task 並べる物を選ぶと順と値の字が変わり_開き直しても続く() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Item("1", "rich", 5000, 1));
        await app.AddItemAsync(Item("2", "many", 100, 2));
        await app.AddItemAsync(Item("3", "many", 100, 3));
        await app.AddItemAsync(Item("4", "liked", 100, 900));
        var main = await app.StartAsync();

        var stats = new StatsViewModel(app.Services, main);
        await UiThread.Until(() => !stats.IsLoading && stats.Shops.Count > 0, "統計の集計");

        Assert.True(stats.ShopOrderSpent);
        Assert.Equal(["rich", "many", "liked"], stats.Shops.Select(row => row.Label));
        Assert.Equal("¥5,000", stats.Shops[0].ValueText);
        Assert.Equal("1 件", stats.Shops[0].SubText);

        stats.ShopOrderCount = true;
        Assert.Equal("many", stats.Shops[0].Label);
        Assert.Equal("2 件", stats.Shops[0].ValueText);
        Assert.Equal("¥200", stats.Shops[0].SubText);

        stats.ShopOrderWish = true;
        Assert.False(stats.ShopOrderCount);
        Assert.Equal("liked", stats.Shops[0].Label);
        Assert.Equal("スキ 900", stats.Shops[0].ValueText);
        Assert.All(stats.Shops, row => Assert.True(row.CanOpen));

        var reopened = new StatsViewModel(app.Services, main);
        await UiThread.Until(() => !reopened.IsLoading && reopened.Shops.Count > 0, "統計の集計");
        Assert.True(reopened.ShopOrderWish);
        Assert.Equal("liked", reopened.Shops[0].Label);
        stats.OnLeaving();
        reopened.OnLeaving();
        await app.SettleAsync();
    });
}
