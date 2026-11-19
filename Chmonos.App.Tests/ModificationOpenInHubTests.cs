using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページ・アバターの管理から改変を開くと、左の一覧のある改変の画面で、その改変を右に選んで開く
/// （ユーザ指示 2026-11-18）。戻る（Alt+←）で元の画面へ戻れる。作り物の名前
/// </summary>
public class ModificationOpenInHubTests
{
    [Fact]
    public Task 商品ページから開くと_改変の画面が改変を選んで開き_戻ると商品ページへ戻る() => TestApp.Run(async app =>
    {
        var avatar = Make.Item("9900001", "作り物のアバター");
        await app.AddItemAsync(avatar);
        var main = await app.StartAsync();
        var created = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("9900001", "夏の改変")));
        main.ShowItem(avatar);

        main.ShowModification(created.Record);

        var hub = Assert.IsType<ModificationHubViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => hub.Detail is ModificationViewModel, "その改変が右に開く");
        Assert.Equal(created.Record.Id, Assert.IsType<ModificationViewModel>(hub.Detail).Record.Id);
        Assert.Contains(hub.Lines.OfType<HubModLine>(), line => line.Row.Record.Id == created.Record.Id);

        Assert.True(main.CanGoBack);
        main.GoBack();
        var back = Assert.IsType<ItemViewModel>(main.CurrentViewModel);
        Assert.Equal("9900001", back.Item.Id);
    });
}
