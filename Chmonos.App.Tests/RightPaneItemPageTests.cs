using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 左右に分かれた画面で、右の欄から商品ページを開くと右の欄の中に重ねる（open.md 95 行・ユーザ判断 2026-11-18・11-19）。
/// 左の選びは残り、「← 一覧に戻る」（Alt+← も）で前の右の中身へ戻る。1段だけで、Alt+← の2回目は画面の履歴へ回る。
/// </summary>
public class RightPaneItemPageTests
{
    private static async Task<(MainViewModel Main, FolderViewModel Folders, FolderViewDetail Detail)> OpenFolderAsync(TestApp app)
    {
        for (var index = 1; index <= 3; index++)
        {
            var file = app.NewFile($@"shop\作り物{index}.zip");
            await app.AddItemAsync(Make.Item($"10000{index:00}", $"作り物の商品{index}").WithFiles(Make.File(file)));
        }

        var main = await app.StartAsync();
        main.ShowFoldersCommand.Execute(null);
        var folders = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => folders.Rows.Count > 0, "フォルダビューの読み込みが済む");
        var shopFolder = System.IO.Path.Combine(app.Root, "files", "shop");
        folders.Selected = folders.Rows.First(row => string.Equals(row.Path.TrimEnd('\\'), shopFolder, StringComparison.OrdinalIgnoreCase));
        await UiThread.Until(
            () => folders.Detail is FolderViewDetail detail && detail.ListItems.OfType<ItemCardViewModel>().Count() == 3,
            "フォルダの右の欄に商品が並ぶ");
        return (main, folders, (FolderViewDetail)folders.Detail!);
    }

    [Fact]
    public Task フォルダの右の一覧から開くと右の欄に重ね_一覧に戻ると前の一覧と左の選びが残っている() => TestApp.Run(async app =>
    {
        var (main, folders, detail) = await OpenFolderAsync(app);
        var selected = folders.Selected;
        var card = detail.ListItems.OfType<ItemCardViewModel>().First();

        detail.OpenItem(card);

        Assert.Same(folders, main.CurrentViewModel);
        Assert.Equal(card.Item.Id, folders.RightItem.Page?.Item.Id);
        Assert.True(folders.RightItem.Page!.IsEmbedded);
        Assert.Same(selected, folders.Selected);
        Assert.Same(detail, folders.Detail);

        folders.RightItem.BackCommand.Execute(null);

        Assert.False(folders.RightItem.IsOpen);
        Assert.Same(detail, folders.Detail);
    });

    [Fact]
    public Task AltLeftの1回目は右の中を閉じ_2回目は画面の履歴へ戻る() => TestApp.Run(async app =>
    {
        var (main, folders, detail) = await OpenFolderAsync(app);
        detail.OpenItem(detail.ListItems.OfType<ItemCardViewModel>().First());

        Assert.True(main.RunShortcut(ShortcutAction.Back));
        Assert.Same(folders, main.CurrentViewModel);
        Assert.False(folders.RightItem.IsOpen);

        Assert.True(main.RunShortcut(ShortcutAction.Back));
        await app.SettleAsync();
        Assert.IsType<SearchViewModel>(main.CurrentViewModel);
    });

    [Fact]
    public Task 左で別の物を選ぶと_重ねた商品ページは閉じる() => TestApp.Run(async app =>
    {
        var (_, folders, detail) = await OpenFolderAsync(app);
        detail.OpenItem(detail.ListItems.OfType<ItemCardViewModel>().First());

        folders.Selected = folders.Rows.First(row => !ReferenceEquals(row, folders.Selected));

        Assert.False(folders.RightItem.IsOpen);
    });

    [Fact]
    public Task 窓が手前に戻って木を読み直しても_重ねた商品ページは閉じない() => TestApp.Run(async app =>
    {
        var (_, folders, detail) = await OpenFolderAsync(app);
        detail.OpenItem(detail.ListItems.OfType<ItemCardViewModel>().First());

        folders.NoteWindowActivated();
        await app.SettleAsync();

        Assert.True(folders.RightItem.IsOpen);
    });

    [Fact]
    public Task 別の画面へ移って戻ると_同じフォルダの上に同じ商品を重ね直す() => TestApp.Run(async app =>
    {
        var (main, folders, detail) = await OpenFolderAsync(app);
        var card = detail.ListItems.OfType<ItemCardViewModel>().Last();
        detail.OpenItem(card);

        main.ShowStatsCommand.Execute(null);
        await app.SettleAsync();
        main.GoBack();
        await app.SettleAsync();

        var reopened = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        Assert.NotSame(folders, reopened);
        await UiThread.Until(() => reopened.RightItem.IsOpen, "戻ると商品ページを重ね直す");
        Assert.Equal(card.Item.Id, reopened.RightItem.Page!.Item.Id);
        Assert.IsType<FolderViewDetail>(reopened.Detail);
    });

    [Fact]
    public Task 重ねた商品ページに落としたzipは_その商品へ紐付けるかを聞く() => TestApp.Run(async app =>
    {
        var (main, folders, detail) = await OpenFolderAsync(app);
        detail.OpenItem(detail.ListItems.OfType<ItemCardViewModel>().First());
        Assert.Same(folders.RightItem.Page, main.CurrentItemPage);

        var zip = app.NewFile("作り物_v2.zip");
        app.Choose = _ => ChoiceDialogResult.Cancel;
        await main.HandleDropAsync([zip], null);
        await app.SettleAsync();

        Assert.Equal("この商品に紐付ける", Assert.Single(app.Choices).First);
    });

    [Fact]
    public Task 改変の画面の右の商品ページを開くも右の欄に重ねる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900001", "作り物のアバター"));
        var main = await app.StartAsync();
        var record = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("9900001", "作り物の改変"))).Record;
        main.ShowModifications(ModificationHubLevel.Modification);
        var hub = Assert.IsType<ModificationHubViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => hub.Groups.OfType<HubModificationRow>().Any(), "改変の画面の読み込みが済む");
        hub.ShowModificationCommand.Execute(record);
        var modification = Assert.IsType<ModificationViewModel>(hub.Detail);
        Assert.NotNull(modification.OpenItemInPane);

        hub.OpenItemPageCommand.Execute("9900001");
        await UiThread.Until(() => hub.RightItem.IsOpen, "右の欄に商品ページを重ねる");

        Assert.Same(hub, main.CurrentViewModel);
        Assert.Same(modification, hub.Detail);
        Assert.Same(hub.RightItem.Page, main.CurrentItemPage);

        Assert.True(main.RunShortcut(ShortcutAction.Back));
        Assert.False(hub.RightItem.IsOpen);
        Assert.Same(modification, hub.Detail);
    });
}
