using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// フォルダ・ショップの画面から「まとめて編集する」へ行って戻った時、選んでいた商品が選んだままになる（open.md 135 行・ユーザ判断 2026-11-20）。
/// 検索は1つの画面を持ち回すので残るが、この2つは戻るたびに作り直す。送った組を画面の履歴の控えへ預けて選び直す。
/// 編集へ送らずに別の画面へ行って戻った時は、今までどおり選びを戻さない。
/// </summary>
public class EditReturnKeepsSelectionTests
{
    [Fact]
    public Task ショップから編集へ送って戻ると_選んでいた商品が選ばれている() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900501", "作り物の衣装"));
        await app.AddItemAsync(Make.Item("9900502", "作り物の髪型"));
        await app.AddItemAsync(Make.Item("9900503", "作り物の靴"));
        var main = await app.StartAsync();
        await main.ShowShopAsync("sample-shop");
        var shop = Assert.IsType<ShopViewModel>(main.CurrentViewModel);
        await app.SettleAsync();

        Assert.Empty(shop.SelectedItemIdsForPlugins());
        shop.SelectAllShown();
        Assert.Equal(3, shop.SelectedItemIdsForPlugins().Count);

        // 1件だけ外して、2件を選んだ状態にする
        var third = shop.ListItems.OfType<ItemCardViewModel>().First();
        shop.SelectByClick(third, control: true, shift: false);
        var sent = shop.SelectedItemIdsForPlugins().OrderBy(id => id).ToList();
        Assert.Equal(2, sent.Count);

        shop.SendSelectionToEditCommand.Execute(null);
        await app.SettleAsync();
        Assert.IsType<EditViewModel>(main.CurrentViewModel);

        main.GoBack();
        var back = await ShopAsync(main, app);
        Assert.NotSame(shop, back);
        Assert.Equal(sent, back.SelectedItemIdsForPlugins().OrderBy(id => id).ToList());
    });

    [Fact]
    public Task ショップから編集へ送らずに別の画面へ行って戻ると_選びは戻さない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900511", "作り物の衣装"));
        var main = await app.StartAsync();
        await main.ShowShopAsync("sample-shop");
        var shop = Assert.IsType<ShopViewModel>(main.CurrentViewModel);
        await app.SettleAsync();
        shop.SelectAllShown();
        Assert.Single(shop.SelectedItemIdsForPlugins());

        main.ShowStatsCommand.Execute(null);
        await app.SettleAsync();
        main.GoBack();

        var back = await ShopAsync(main, app);
        Assert.Empty(back.SelectedItemIdsForPlugins());
    });

    [Fact]
    public Task フォルダから編集へ送って戻ると_選んでいた商品が選ばれている() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900521", "作り物のドレス").WithFiles(Make.File(app.NewFile(@"lib\a\dress.zip"))));
        await app.AddItemAsync(Make.Item("9900522", "作り物の髪型").WithFiles(Make.File(app.NewFile(@"lib\a\hair.zip"))));
        var main = await app.StartAsync();
        main.ShowFoldersCommand.Execute(null);
        var folders = await LoadedAsync(main);

        // 2商品を直下に持つフォルダを選ぶ
        var folder = System.IO.Path.Combine(app.Root, "files", "lib", "a");
        folders.Selected = folders.Rows.FirstOrDefault(row => string.Equals(row.Path.TrimEnd('\\'), folder, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(string.Join(" | ", folders.Rows.Select(row => $"{row.Kind}:{row.Path}")));
        await UiThread.Until(() => folders.Detail is FolderViewDetail, "フォルダの右の欄が出る");
        var detail = (FolderViewDetail)folders.Detail!;
        await UiThread.Until(() => detail.ListItems.OfType<ItemCardViewModel>().Count() == 2, "フォルダの右の欄に商品が並ぶ");
        detail.SelectAllShown();
        var sent = detail.SelectedItemIdsForPlugins().OrderBy(id => id).ToList();
        Assert.Equal(2, sent.Count);
        var key = folders.Selected!.Key;

        detail.SendSelectionToEditCommand.Execute(null);
        await app.SettleAsync();
        Assert.IsType<EditViewModel>(main.CurrentViewModel);

        main.GoBack();
        var back = await LoadedAsync(main);
        Assert.NotSame(folders, back);
        await UiThread.Until(() => back.Detail is FolderViewDetail, "戻った先で右の一覧が出る");
        await app.SettleAsync();
        Assert.Equal(key, back.Selected!.Key);
        var restored = Assert.IsType<FolderViewDetail>(back.Detail);
        Assert.Equal(sent, restored.SelectedItemIdsForPlugins().OrderBy(id => id).ToList());
    });

    private static async Task<ShopViewModel> ShopAsync(MainViewModel main, TestApp app)
    {
        await UiThread.Until(() => main.CurrentViewModel is ShopViewModel, "ショップへ戻る");
        await app.SettleAsync();
        return Assert.IsType<ShopViewModel>(main.CurrentViewModel);
    }

    private static async Task<FolderViewModel> LoadedAsync(MainViewModel main)
    {
        var folders = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => folders.EmptyText != "読み込んでいます…", "フォルダビューの読み込みが済む");
        return folders;
    }
}
