using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// フォルダビューの右の欄の幅の配り。右に組み込んだ商品ページが横に送らずに済む幅を先に残し、木の方を縮める
/// （幅 1280 の窓で商品ページが横に送られていた。2026-10-04。改変の画面と同じ直し方）。
/// </summary>
public class FolderDetailWidthTests
{
    [Fact]
    public Task 商品ページを選ぶと商品ページが横に送らずに済む幅を残し_ほかを選ぶと残さない() => TestApp.Run(async app =>
    {
        var path = app.NewFile(@"lib\a\dress.zip");
        await app.AddItemAsync(Make.Item("9900601", "作り物のドレス").WithFiles(Make.File(path)));
        var main = await app.StartAsync();
        main.ShowFoldersCommand.Execute(null);
        var folders = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => folders.EmptyText != "読み込んでいます…", "フォルダビューの読み込みが済む");
        while (folders.Rows.FirstOrDefault(row => row.CanExpand && !row.IsExpanded) is { } closed)
        {
            folders.ToggleCommand.Execute(closed);
        }

        // 何も選んでいない
        Assert.Null(folders.Detail);
        Assert.Equal(0, folders.DetailWidthWanted);

        // フォルダを選ぶと、右の欄の最小で足りる
        folders.Selected = folders.Rows.First(row => row.CanExpand);
        await app.SettleAsync();
        Assert.IsNotType<ItemViewModel>(folders.Detail);
        Assert.Equal(0, folders.DetailWidthWanted);

        // 商品に結び付いたファイルを選ぶと、商品ページが組み込まれる
        folders.Selected = folders.Rows.First(row => row.Name == "dress.zip");
        await app.SettleAsync();
        var page = Assert.IsType<ItemViewModel>(folders.Detail);
        Assert.True(page.BodyMinWidth > 0);
        Assert.Equal(page.BodyMinWidth + SystemParameters.VerticalScrollBarWidth, folders.DetailWidthWanted);
    });
}
