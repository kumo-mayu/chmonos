using Chmonos.Core.Booth;
using Chmonos.Core.Models;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 「BOOTHで開く」のリンクは、商品ページなら商品ID から、ショップなら https の BOOTH のホストだけ（2026-10-06 外部の点検・L106）。
/// 記録の URL（手で直せる JSON）に実行ファイルの場所や別の種類の URL が書かれていても、それを外のアプリへ渡さない。
/// </summary>
public class BoothLinksTests
{
    private static ItemRecord Item(string id, string? url) => new()
    {
        Id = id,
        Booth = new BoothBlock { Name = "作り物", Url = url },
    };

    [Theory]
    [InlineData(@"C:\Windows\System32\calc.exe")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("https://evil.example.com/items/9900001")]
    [InlineData(null)]
    public void 商品ページは記録のURLを使わず商品IDから作る(string? recorded)
        => Assert.Equal("https://booth.pm/ja/items/9900001", BoothLinks.ItemPage(Item("9900001", recorded)));

    [Theory]
    [InlineData("local-0a1b2c3d")]
    [InlineData(@"..\..\x")]
    [InlineData("9900001/../../x")]
    [InlineData("9900001?x=1")]
    [InlineData("")]
    public void 番号でない商品IDからは商品ページを作らない(string id)
        => Assert.Null(BoothLinks.ItemPage(Item(id, "https://booth.pm/ja/items/9900001")));

    [Theory]
    [InlineData("https://booth.pm/")]
    [InlineData("https://sample-shop.booth.pm/")]
    [InlineData("https://Sample-Shop.booth.pm/items")]
    public void ショップのURLはhttpsのBOOTHのホストだけ(string url)
        => Assert.NotNull(BoothLinks.ShopPage(url));

    [Theory]
    [InlineData("http://sample-shop.booth.pm/")]
    [InlineData("https://sample-shop.booth.pm.example.com/")]
    [InlineData("https://evilbooth.pm/")]
    [InlineData("https://a.b.booth.pm/")]
    [InlineData("https://user@sample-shop.booth.pm/")]
    [InlineData("https://sample-shop.booth.pm:8443/")]
    [InlineData(@"C:\Windows\System32\calc.exe")]
    [InlineData("file:///C:/x")]
    [InlineData(@"\\server\share")]
    [InlineData("booth:open")]
    [InlineData("")]
    [InlineData(null)]
    public void ショップのURLがBOOTHでなければ開かない(string? url)
        => Assert.Null(BoothLinks.ShopPage(url));

    [Fact]
    public void ショップの記録のURLが使えなければサブドメインから作る()
    {
        Assert.Equal("https://sample-shop.booth.pm/", BoothLinks.ShopPage("sample-shop", @"C:\Windows\System32\calc.exe"));
        Assert.Null(BoothLinks.ShopPage(@"..\x", null));
        Assert.Null(BoothLinks.ShopPage("evil.example.com/x", null));
        Assert.Null(BoothLinks.ShopPage("local-0a1b2c3d", null));
    }

    // 商品のショップの BOOTH のページ（商品ページのショップ名の中クリック・ショップ順の区切りの札。open.md 136）
    private static ItemRecord WithShops(BoothShop? booth, LocalShop? local) => new()
    {
        Id = "9900001",
        Booth = new BoothBlock { Name = "作り物", Shop = booth },
        Local = new LocalBlock { Shop = local },
    };

    [Fact]
    public void 商品のショップは_人が入れたショップのURLを取れた側より先に使う()
    {
        var item = WithShops(
            new BoothShop { Name = "取れた店", Subdomain = "fetched-shop", Url = "https://fetched-shop.booth.pm/" },
            new LocalShop { Name = "直した店", Subdomain = "fixed-shop", Url = "https://fixed-shop.booth.pm/" });

        Assert.Equal("https://fixed-shop.booth.pm/", BoothLinks.ShopPage(item));
    }

    [Fact]
    public void 商品のショップのURLが無ければ_サブドメインから作る()
        => Assert.Equal("https://sample-shop.booth.pm/",
            BoothLinks.ShopPage(WithShops(new BoothShop { Name = "店", Subdomain = "sample-shop" }, null)));

    [Fact]
    public void 手元だけのショップとショップの無い商品には_BOOTHのページが無い()
    {
        Assert.Null(BoothLinks.ShopPage(WithShops(null, new LocalShop { Name = "手元の店", Subdomain = "local-0a1b2c3d" })));
        Assert.Null(BoothLinks.ShopPage(WithShops(null, null)));
    }
}
