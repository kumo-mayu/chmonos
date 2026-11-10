using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// ショップの上の段の「最終購入」：ファイルの日付で代えた日には※と吹き出しを付ける（B3・2026-11-10）。
/// ショップの一覧とカードは付けていたのに、上の段だけ手入力と同じ顔で出ていた
/// </summary>
public class ShopLastAcquiredMarkTests
{
    [Fact]
    public Task 入手日が空でファイルの日付で代えたら_上の段に印と吹き出しを出す() => TestApp.Run(async app =>
    {
        var file = app.NewFile("作り物.zip");
        File.SetLastWriteTime(file, new DateTime(2026, 3, 4, 12, 0, 0));
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装").WithFiles(Make.File(file)));
        var main = await app.StartAsync();
        await main.ShowShopAsync("sample-shop");
        var shop = Assert.IsType<ShopViewModel>(main.CurrentViewModel);

        Assert.Equal("2026-03-04", shop.LastAcquiredText);
        Assert.True(shop.LastAcquiredIsFallback);
        Assert.Equal("入手日が未入力なので、ファイルの日付を表示しています。", shop.LastAcquiredTooltip);
    });

    [Fact]
    public Task 入手日を入れてあれば_印も吹き出しも出さない() => TestApp.Run(async app =>
    {
        var item = Make.Item("1000001", "作り物の衣装");
        await app.AddItemAsync(item with { Local = item.Local with { AcquiredAt = new DateOnly(2026, 5, 6) } });
        var main = await app.StartAsync();
        await main.ShowShopAsync("sample-shop");
        var shop = Assert.IsType<ShopViewModel>(main.CurrentViewModel);

        Assert.Equal("2026-05-06", shop.LastAcquiredText);
        Assert.False(shop.LastAcquiredIsFallback);
        Assert.Equal(string.Empty, shop.LastAcquiredTooltip);
    });

    /// <summary>
    /// 入手日が無いとき、見出しのある上の段は「—」、見出しの無いカードの2行目は「入手日なし」と言葉で書く（ユーザ判断 2026-11-10）
    /// </summary>
    [Fact]
    public Task 入手日が無ければ_上の段は印で_カードの2行目は言葉で書く() => TestApp.Run(async app =>
    {
        // 所持していない商品はショップの画面で既定では隠れるので、ファイルの記録は持たせる。
        // そのファイルは手元に無いので、ファイルの日付でも代えられない
        var gone = Path.Combine(Path.GetTempPath(), "chmonos-shop-no-date-" + Guid.NewGuid().ToString("N"), "作り物.zip");
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装").WithFiles(Make.File(gone)));
        var main = await app.StartAsync();
        await main.ShowShopAsync("sample-shop");
        var shop = Assert.IsType<ShopViewModel>(main.CurrentViewModel);

        Assert.Equal("—", shop.LastAcquiredText);
        var card = Assert.Single(shop.ListItems.OfType<ItemCardViewModel>());
        Assert.Equal("入手日なし", card.ShopName);
    });
}
