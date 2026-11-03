using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 「改変に追加して送る」で選んだ物を記録する所（Codex の点検 2026-11-03）。
/// 前は1行ずつ足し、途中で失敗すると一部だけ残り、黙って送らずに終わり、やり直すと先の分が2重になった。
/// </summary>
public class ModificationCommitPickedTests
{
    [Fact]
    public Task 選んだ物はまとめて記録する() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1790001", "作り物のアバター"));
        await app.StartAsync();
        var created = Assert.IsType<Chmonos.Core.Commands.CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new Chmonos.Core.Commands.UiCommand.CreateModification("1790001", "作り物の夏")));
        var model = ModificationPicking.BuildDialog(app.Services, "題", "見出し", "", [created.Record], "既存", "決定", "空");
        model.PickCommand.Execute(model.Rows[0]);

        var record = await ItemUnityActions.CommitPickedAsync(
            app.Services, model, "題", null,
            [new ModificationMember { ItemId = "1790002", Package = "a.unitypackage" }, new ModificationMember { ItemId = "1790002", Package = "b.unitypackage" }]);

        Assert.NotNull(record);
        var saved = (await app.Services.Modifications.LoadAsync(created.Record.Id))!;
        Assert.Equal(["a.unitypackage", "b.unitypackage"], saved.Members.Select(member => member.Package));
        Assert.Empty(app.Notices);
    });

    [Fact]
    public Task 記録できなければ_何も足さず_送らないと言う() => TestApp.Run(async app =>
    {
        await app.StartAsync();

        // 選んだ後に別の所で消された改変（記録に失敗する）
        var gone = new ModificationRecord { Id = "mod-0000c001", AvatarItemId = "1790001", Name = "消された改変" };
        var model = ModificationPicking.BuildDialog(app.Services, "題", "見出し", "", [gone], "既存", "決定", "空");
        model.PickCommand.Execute(model.Rows[0]);

        var record = await ItemUnityActions.CommitPickedAsync(
            app.Services, model, "題", null,
            [new ModificationMember { ItemId = "1790002" }, new ModificationMember { ItemId = "1790003" }]);

        Assert.Null(record);
        var notice = Assert.Single(app.Notices);
        Assert.StartsWith("「消された改変」に追加できませんでした。Unityへは送っていません。", notice.Text);
        Assert.Null(await app.Services.Modifications.LoadAsync("mod-0000c001"));
    });
}
