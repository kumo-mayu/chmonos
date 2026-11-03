using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 「改変に追加（して送る）」で選んだ物を記録する所（Codex の点検 2026-11-03・改変の複数選び 2026-11-03）。
/// 前は1行ずつ足し、途中で失敗すると一部だけ残り、黙って送らずに終わり、やり直すと先の分が2重になった。
/// </summary>
public class ModificationCommitPickedTests
{
    private static async Task<ModificationRecord> CreateAsync(TestApp app, string name)
        => Assert.IsType<Chmonos.Core.Commands.CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new Chmonos.Core.Commands.UiCommand.CreateModification("1790001", name))).Record;

    private static readonly ModificationMember[] Members =
        [new ModificationMember { ItemId = "1790002", Package = "a.unitypackage" }, new ModificationMember { ItemId = "1790002", Package = "b.unitypackage" }];

    [Fact]
    public Task 選んだ物はまとめて記録する() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1790001", "作り物のアバター"));
        await app.StartAsync();
        var created = await CreateAsync(app, "作り物の夏");
        var model = ModificationPicking.BuildDialog(app.Services, "題", "見出し", "", [created], "既存", "決定", "空");
        model.PickCommand.Execute(model.Rows[0]);

        var records = await ItemUnityActions.CommitPickedAsync(app.Services, model, "題", null, Members);

        Assert.Single(records!);
        var saved = (await app.Services.Modifications.LoadAsync(created.Id))!;
        Assert.Equal(["a.unitypackage", "b.unitypackage"], saved.Members.Select(member => member.Package));
        Assert.Empty(app.Notices);
    });

    [Fact]
    public Task 改変を2つ選ぶと_両方に同じ物を記録する() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1790001", "作り物のアバター"));
        await app.StartAsync();
        var summer = await CreateAsync(app, "作り物の夏");
        var winter = await CreateAsync(app, "作り物の冬");
        var model = ModificationPicking.BuildDialog(app.Services, "題", "見出し", "", [summer, winter], "既存", "決定", "空");
        model.PickCommand.Execute(model.Rows[0]);
        model.PickCommand.Execute(model.Rows[1]);

        var records = await ItemUnityActions.CommitPickedAsync(app.Services, model, "題", null, Members);

        Assert.Equal(["作り物の夏", "作り物の冬"], records!.Select(record => record.Name));
        foreach (var id in new[] { summer.Id, winter.Id })
        {
            Assert.Equal(2, (await app.Services.Modifications.LoadAsync(id))!.Members.Count);
        }

        Assert.Equal("「作り物の夏」「作り物の冬」", ModificationPicking.NamesText(records!));
    });

    [Fact]
    public Task 記録できなければ_何も足さず_送らないと言う() => TestApp.Run(async app =>
    {
        await app.StartAsync();

        // 選んだ後に別の所で消された改変（記録に失敗する）
        var gone = new ModificationRecord { Id = "mod-0000c001", AvatarItemId = "1790001", Name = "消された改変" };
        var model = ModificationPicking.BuildDialog(app.Services, "題", "見出し", "", [gone], "既存", "決定", "空");
        model.PickCommand.Execute(model.Rows[0]);

        var records = await ItemUnityActions.CommitPickedAsync(app.Services, model, "題", null, Members);

        Assert.Null(records);
        var notice = Assert.Single(app.Notices);
        Assert.StartsWith("「消された改変」に追加できませんでした。Unityへは送っていません。", notice.Text);
        Assert.Null(await app.Services.Modifications.LoadAsync("mod-0000c001"));
    });

    [Fact]
    public Task 二つ目の改変に足せなければ_そこで止め_足せた所までを言う() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1790001", "作り物のアバター"));
        await app.StartAsync();
        var summer = await CreateAsync(app, "作り物の夏");
        var gone = new ModificationRecord { Id = "mod-0000c002", AvatarItemId = "1790001", Name = "消された改変" };
        var later = await CreateAsync(app, "作り物の秋");
        var model = ModificationPicking.BuildDialog(app.Services, "題", "見出し", "", [summer, gone, later], "既存", "決定", "空");
        foreach (var row in model.Rows)
        {
            model.PickCommand.Execute(row);
        }

        var records = await ItemUnityActions.CommitPickedAsync(app.Services, model, "題", null, Members);

        Assert.Null(records);
        Assert.StartsWith(
            "「作り物の夏」には追加しました。「消された改変」に追加できませんでした。Unityへは送っていません。",
            Assert.Single(app.Notices).Text);
        Assert.Equal(2, (await app.Services.Modifications.LoadAsync(summer.Id))!.Members.Count);
        Assert.Empty((await app.Services.Modifications.LoadAsync(later.Id))!.Members);
    });

    [Fact]
    public Task 送らずに足すだけなら_送っていないとは言わない() => TestApp.Run(async app =>
    {
        await app.StartAsync();
        var gone = new ModificationRecord { Id = "mod-0000c003", AvatarItemId = "1790001", Name = "消された改変" };
        var model = ModificationPicking.BuildDialog(app.Services, "題", "見出し", "", [gone], "既存", "決定", "空");
        model.PickCommand.Execute(model.Rows[0]);

        await ItemUnityActions.CommitPickedAsync(app.Services, model, "題", null, Members[0], sending: false);

        Assert.DoesNotContain("Unity", Assert.Single(app.Notices).Text);
    });

    [Fact]
    public void 名前は3つまで並べ_それより多ければほか何件にする()
    {
        static ModificationRecord Mod(string name) => new() { Id = $"mod-{name}", AvatarItemId = "1", Name = name };

        Assert.Equal("「あ」「い」「う」", ModificationPicking.NamesText([Mod("あ"), Mod("い"), Mod("う")]));
        Assert.Equal("「あ」「い」ほか 2 件", ModificationPicking.NamesText([Mod("あ"), Mod("い"), Mod("う"), Mod("え")]));
    }
}
