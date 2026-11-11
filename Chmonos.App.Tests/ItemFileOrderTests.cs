using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 設定の「ファイルの表示順」（ユーザ指示 2026-11-11）が、商品ページと編集画面の「手元のファイル」の両方に効く。
/// 並べ方の細かい所は Core の <c>LocalFileOrderTests</c>。名前・ID は作り物。
/// </summary>
public class ItemFileOrderTests
{
    private const string ItemId = "9900851";

    /// <summary>記録の順は v10・外した物・v2・別名。</summary>
    private static ItemRecord WithFiles()
    {
        var item = Make.Item(ItemId, "作り物の衣装");
        return item with
        {
            Local = item.Local with
            {
                LocalFiles =
                [
                    Make.File(@"D:\files\Sample_v10.zip"),
                    Make.File(@"D:\files\Another.zip", detached: true),
                    Make.File(@"D:\files\Sample_v2.zip"),
                    Make.File(@"D:\files\Base.zip"),
                ],
            },
        };
    }

    private static async Task<(string[] Item, string[] Edit)> ShownAsync(TestApp app, MainViewModel main)
    {
        main.ShowItem((await app.Store.Items.LoadAsync(ItemId))!);
        await app.SettleAsync();
        var page = Assert.IsType<ItemViewModel>(main.CurrentViewModel);
        var onItem = page.LocalFiles.Select(row => row.FileName).ToArray();

        main.ShowEditCommand.Execute(null);
        await app.SettleAsync();
        var edit = Assert.IsType<EditViewModel>(main.CurrentViewModel);
        var onEdit = edit.ItemPage!.LocalFiles.Select(row => row.FileName).ToArray();
        return (onItem, onEdit);
    }

    [Fact]
    public Task 既定は追加した順で_今までの並びのまま() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(WithFiles());
        await app.ChangeSettingsAsync(settings => settings with { ReturnToSearchWhenEditDone = false });
        var main = await app.StartAsync();

        Assert.Equal(FileOrder.Added, app.Services.Settings.FileOrder);
        var (item, edit) = await ShownAsync(app, main);
        string[] expected = ["Sample_v10.zip", "Sample_v2.zip", "Base.zip", "Another.zip"];
        Assert.Equal(expected, item);
        Assert.Equal(expected, edit);
    });

    [Fact]
    public Task 設定の画面で選び直すと_商品ページと編集画面の両方の並びが変わる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(WithFiles());
        await app.ChangeSettingsAsync(settings => settings with { ReturnToSearchWhenEditDone = false });
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);

        settings.SelectedFileOrder = SettingsViewModel.FileOrders.Single(option => option.Value == FileOrder.Name);
        await app.SettleAsync();
        Assert.Equal(FileOrder.Name, app.Services.Settings.FileOrder);
        var (item, edit) = await ShownAsync(app, main);
        // 外したファイルはどの順でも後ろ
        string[] byName = ["Base.zip", "Sample_v10.zip", "Sample_v2.zip", "Another.zip"];
        Assert.Equal(byName, item);
        Assert.Equal(byName, edit);

        settings.SelectedFileOrder = SettingsViewModel.FileOrders.Single(option => option.Value == FileOrder.NameWithVersion);
        await app.SettleAsync();
        (item, edit) = await ShownAsync(app, main);
        string[] byVersion = ["Base.zip", "Sample_v2.zip", "Sample_v10.zip", "Another.zip"];
        Assert.Equal(byVersion, item);
        Assert.Equal(byVersion, edit);

        // 向きを反転すると両方が逆になる。外したファイルは後ろのまま
        settings.FileOrderDescending = true;
        await app.SettleAsync();
        Assert.True(app.Services.Settings.FileOrderDescending);
        (item, edit) = await ShownAsync(app, main);
        string[] byVersionDown = ["Sample_v10.zip", "Sample_v2.zip", "Base.zip", "Another.zip"];
        Assert.Equal(byVersionDown, item);
        Assert.Equal(byVersionDown, edit);
    });

    [Fact]
    public Task 向きの語は並べる物で変わり_並べる物を選び直すとその既定の向きに戻る() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);

        // 既定は追加した順の古い順（今までの並び）
        Assert.Equal(FileOrder.Added, settings.FileOrder);
        Assert.True(settings.FileOrderAscending);
        Assert.Equal(("新しい順", "古い順"), (settings.FileOrderDescendingLabel, settings.FileOrderAscendingLabel));

        settings.FileOrderDescending = true;
        settings.SelectedFileOrder = SettingsViewModel.FileOrders.Single(option => option.Value == FileOrder.Name);
        Assert.False(settings.FileOrderDescending);
        Assert.Equal(("降順", "昇順"), (settings.FileOrderDescendingLabel, settings.FileOrderAscendingLabel));

        settings.SelectedFileOrder = SettingsViewModel.FileOrders.Single(option => option.Value == FileOrder.Size);
        Assert.True(settings.FileOrderDescending);
        Assert.Equal(("大きい順", "小さい順"), (settings.FileOrderDescendingLabel, settings.FileOrderAscendingLabel));
        await app.SettleAsync();
        Assert.Equal(new FileSort(FileOrder.Size, true), app.Services.Settings.FileSort);

        // 右のボタン（外れる側の知らせの false）は何も変えない
        settings.FileOrderAscending = false;
        Assert.True(settings.FileOrderDescending);
        settings.FileOrderAscending = true;
        await app.SettleAsync();
        Assert.Equal(new FileSort(FileOrder.Size, false), app.Services.Settings.FileSort);
    });
}
