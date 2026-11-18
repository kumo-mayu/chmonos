using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>統計の「ショップ」の欄（open.md 107）：金額・商品数・スキ数（所持している商品のスキ数の合計）で並べ替える。</summary>
public class StatsShopsTests
{
    private static ItemRecord Item(string id, string shop, int price, int wish, bool owned = true) => new()
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
            LocalFiles = owned ? [new LocalFileRecord { Hash = id, Paths = ["x" + id], SizeBytes = 1 }] : [],
            Purchases = [new Purchase { VariationId = 1, Price = price }],
        },
    };

    private static StatsSnapshot Build(params ItemRecord[] items)
        => StatsService.Build(items, new AvatarRegistry(), unresolvedCount: 0);

    [Fact]
    public void スキ数は所持している商品のスキ数を足す()
    {
        var snapshot = Build(Item("1", "a", 100, 30), Item("2", "a", 100, 20), Item("3", "a", 100, 999, owned: false));

        var shop = Assert.Single(snapshot.Shops);
        Assert.Equal(50, shop.WishCount);
        Assert.Equal(2, shop.ItemCount);
        Assert.Equal(200, shop.SpentYen);
    }

    [Fact]
    public void 並べる物で順が変わる()
    {
        var snapshot = Build(
            Item("1", "rich", 5000, 1),
            Item("2", "many", 100, 1), Item("3", "many", 100, 1),
            Item("4", "liked", 100, 900));

        Assert.Equal(["rich", "many", "liked"], StatsExtras.SortShops(snapshot.Shops, StatsShopOrder.Spent).Select(bar => bar.Key));
        Assert.Equal("many", StatsExtras.SortShops(snapshot.Shops, StatsShopOrder.Count).First().Key);
        Assert.Equal("liked", StatsExtras.SortShops(snapshot.Shops, StatsShopOrder.Wish).First().Key);
    }

    /// <summary>同じ値のときは、ほかの値、それも同じなら名前の順（開くたびに揺れない）</summary>
    [Fact]
    public void 同じ値のときはほかの値と名前で決める()
    {
        var snapshot = Build(Item("1", "b", 100, 5), Item("2", "a", 100, 5), Item("3", "c", 100, 9));

        Assert.Equal(["c", "a", "b"], StatsExtras.SortShops(snapshot.Shops, StatsShopOrder.Count).Select(bar => bar.Key));
    }
}
