using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 設定の「非表示にした商品」は検索と同じカード・リストで出し、右クリックの項目で非表示を戻す（open.md 172・ユーザ判断 2026-11-18）。
/// </summary>
public class SettingsHiddenItemsTests
{
    private static async Task<(SettingsViewModel Settings, MainViewModel Main)> OpenAsync(TestApp app)
    {
        var hidden = Make.Item("9700001", "作り物の隠した商品", "shop-a");
        await app.Services.Store.Items.SaveAsync(hidden with { Local = hidden.Local with { IsHidden = true } });
        await app.Services.Store.Items.SaveAsync(Make.Item("9700002", "作り物の見える商品", "shop-a"));
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");
        return (settings, main);
    }

    [Fact]
    public Task 非表示の商品だけが_カードの元の行として並ぶ() => TestApp.Run(async app =>
    {
        var (settings, _) = await OpenAsync(app);

        var row = Assert.Single(settings.HiddenItems);
        Assert.Equal("9700001", row.ItemId);
        Assert.Equal("1 件", settings.HiddenText);
        Assert.True(settings.HasHidden);

        // 検索と同じカードを作れる（宛先はこの画面）
        Assert.NotNull(row.Card);
        settings.OnLeaving();
    });

    [Fact]
    public Task 右クリックの非表示の項目は_解除の字になり_押すと非表示が戻る() => TestApp.Run(async app =>
    {
        var (settings, _) = await OpenAsync(app);
        Assert.Equal("非表示を解除", settings.MenuCommands.HideItemHeader);

        var card = settings.HiddenItems.Single().Card!;
        settings.MenuCommands.HideItemCommand.Execute(card);
        await UiThread.Until(() => settings.HiddenItems.Count == 0, "解除して一覧から消える");

        var saved = await app.Services.Store.Items.LoadAsync("9700001");
        Assert.False(saved!.Local.IsHidden);
        settings.OnLeaving();
    });

    [Fact]
    public Task 商品ページへ移る前に_流した位置を預ける() => TestApp.Run(async app =>
    {
        var (settings, _) = await OpenAsync(app);
        var called = 0;
        settings.OpeningItem += () => called++;

        settings.HiddenItems.Single().OpenCommand!.Execute(null);
        Assert.Equal(1, called);
        settings.OnLeaving();
    });
}

/// <summary>設定の「について」のリンク先（open.md 174）。配った版は印のマニュアル、開発中は master。</summary>
public class SettingsLinkTests
{
    [Fact]
    public void マニュアルの先は_配った版なら印_開発中ならmaster()
    {
        Assert.Equal("https://github.com/kumo-mayu/chmonos/tree/manual-v1.2.0/manual", Chmonos.Core.Services.UpdateCheck.ManualPage("1.2.0", released: true));
        Assert.Equal("https://github.com/kumo-mayu/chmonos/blob/manual-v1.2.0/manual/40-api.md", Chmonos.Core.Services.UpdateCheck.ManualPage("1.2.0", released: true, "40-api.md"));
        Assert.Equal("https://github.com/kumo-mayu/chmonos/tree/master/manual", Chmonos.Core.Services.UpdateCheck.ManualPage("1.2.0", released: false));
        Assert.StartsWith(Chmonos.Core.Services.UpdateCheck.RepositoryPage, Chmonos.Core.Services.UpdateCheck.DownloadPage);
    }
}