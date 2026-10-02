using System.IO;
using System.Windows.Controls;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// ショップ順の区切りの札のアイコンと操作（ユーザ判断 2026-10-02・メモ2-⑤）。
/// アイコンはそのショップの物だけ（無ければ何も出さない）。左クリックでアプリのショップの画面、中クリックで BOOTH、右クリックでその2つ。
/// カテゴリ・公開日・入手日の札は今のまま押せない。どの札もキーボードの止まりにはしない。
/// </summary>
public class SearchShopDividerTests
{
    private static void SortBy(SearchViewModel search, SortKind kind)
        => search.SortField = search.SortFields.First(field => field.Kind == kind);

    private static ItemRecord LocalShop(string id, string name)
    {
        var item = Make.Item(id, "作り物の商品");
        return item with
        {
            Booth = item.Booth with { Shop = null },
            Local = item.Local with { Shop = new LocalShop { Name = name, Subdomain = LocalShopKey.For(name) } },
        };
    }

    [Fact]
    public Task ショップの札だけ押せ_アイコンはそのショップの物があるときだけ() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の商品1", shop: "icon-shop"));
        await app.AddItemAsync(Make.Item("1000002", "作り物の商品2", shop: "plain-shop"));
        var icon = app.Services.Paths.ShopIconFile("icon-shop", "https://example.invalid/icon.png");
        Directory.CreateDirectory(Path.GetDirectoryName(icon)!);
        await File.WriteAllBytesAsync(icon, [0]);
        var search = (await app.StartAsync()).Search;

        SortBy(search, SortKind.Shop);
        var dividers = search.ShownDividers.ToDictionary(divider => divider.Label);

        Assert.True(dividers["icon-shop"].HasIcon);
        Assert.Equal(icon, dividers["icon-shop"].IconPath);
        Assert.False(dividers["plain-shop"].HasIcon);
        Assert.All(dividers.Values, divider => Assert.True(divider.IsPressable));

        SortBy(search, SortKind.Category);
        Assert.All(search.ShownDividers, divider =>
        {
            Assert.False(divider.IsPressable);
            Assert.False(divider.HasIcon);
            Assert.Null(divider.PressHint);
        });
    });

    [Fact]
    public Task 札を押すとアプリのショップの画面を開く() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の商品1", shop: "open-shop"));
        var main = await app.StartAsync();
        SortBy(main.Search, SortKind.Shop);

        main.Search.ShownDividers.Single().OpenShopCommand!.Execute(null);
        await UiThread.Until(() => main.CurrentViewModel is ShopViewModel, "ショップの画面へ移る");

        Assert.Equal("open-shop", ((ShopViewModel)main.CurrentViewModel!).Shop.Subdomain);
    });

    [Fact]
    public Task 手元だけのショップとショップなしの札は_BOOTHを開けない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の商品1", shop: "booth-shop"));
        await app.AddItemAsync(LocalShop("1000002", "手元の作り物の店"));
        var none = Make.Item("1000003", "作り物の商品3");
        await app.AddItemAsync(none with { Booth = none.Booth with { Shop = null } });
        var search = (await app.StartAsync()).Search;
        SortBy(search, SortKind.Shop);
        var dividers = search.ShownDividers.ToDictionary(divider => divider.Label);

        Assert.NotNull(dividers["booth-shop"].OpenInBoothCommand);
        Assert.Equal("押すとショップを開きます。中クリックでBOOTHのページを開きます。", dividers["booth-shop"].PressHint);

        Assert.True(dividers["手元の作り物の店"].IsPressable);
        Assert.Null(dividers["手元の作り物の店"].OpenInBoothCommand);
        Assert.Equal("押すとショップを開きます。", dividers["手元の作り物の店"].PressHint);

        Assert.False(dividers[Core.Services.ItemGroups.NoShop].IsPressable);
    });

    [Fact]
    public Task 右クリックのメニューはショップを開くとBOOTHで開く_手元だけのショップはBOOTHの行が無い() => UiThread.Run(() =>
    {
        var booth = new SortDivider("shop:a", "ショップ", "作り物の店", null)
        {
            OpenShopCommand = new RelayCommand(() => { }),
            OpenInBoothCommand = new RelayCommand(() => { }),
        };
        var local = new SortDivider("shop:b", "ショップ", "手元の店", null) { OpenShopCommand = new RelayCommand(() => { }) };

        Assert.Equal(["ショップを開く", "BOOTHで開く"], SortDividerBorder.BuildMenu(booth).Items.OfType<MenuItem>().Select(item => (string)item.Header));
        Assert.Equal(["ショップを開く"], SortDividerBorder.BuildMenu(local).Items.OfType<MenuItem>().Select(item => (string)item.Header));
    });

    [Fact]
    public Task 札はキーボードで止まらず_ショップの札だけ読み上げから押せる() => UiThread.Run(() =>
    {
        var shop = new SortDivider("shop:a", "ショップ", "作り物の店", null) { OpenShopCommand = new RelayCommand(() => { }) };
        var category = new SortDivider("category:a", "カテゴリ", "衣装", null);

        var shopBorder = new SortDividerBorder { DataContext = shop };
        var categoryBorder = new SortDividerBorder { DataContext = category };
        var shopPeer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(shopBorder);
        var categoryPeer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(categoryBorder);

        Assert.False(shopBorder.Focusable);
        Assert.False(shopPeer.IsKeyboardFocusable());
        Assert.NotNull(shopPeer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke));
        Assert.NotNull(shopBorder.ContextMenu);
        Assert.Null(categoryPeer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke));
        Assert.Null(categoryBorder.ContextMenu);
    });
}
