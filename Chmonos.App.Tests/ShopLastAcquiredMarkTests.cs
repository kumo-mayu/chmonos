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
        // 入手日もファイルも無い商品。所持していないので、「所持しているものだけ」を切って見る。
        // カードは裏で読むので、読み終えるのを待つ（待たないと、一式の中で走らせたときだけ空のまま読んでいた）
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var main = await app.StartAsync();
        await main.ShowShopAsync("sample-shop");
        var shop = Assert.IsType<ShopViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => shop.IsListReady, "ショップの商品を読む");
        shop.OwnedOnly = false;

        Assert.Equal("—", shop.LastAcquiredText);
        var card = Assert.Single(shop.ListItems.OfType<ItemCardViewModel>());
        Assert.Equal("入手日なし", card.ShopName);
    });
}
