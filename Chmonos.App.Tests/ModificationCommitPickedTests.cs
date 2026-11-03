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

    /// <summary>
    /// 送らない「改変に追加」（商品ページ・カード）。前は商品ページの道だけ既に入っている改変にも重ねていた
    /// （Codex の点検 2026-11-03 の2回目）。
    /// </summary>
    [Fact]
    public Task 送らずに足すときは_既に入っている改変に重ねない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1790001", "作り物のアバター"));
        var costume = Make.Item("1790002", "作り物の衣装");
        await app.StartAsync();
        var summer = await CreateAsync(app, "作り物の夏");
        var winter = await CreateAsync(app, "作り物の冬");
        await app.Services.Commands.ExecuteAsync(new Chmonos.Core.Commands.UiCommand.AddModificationMembers(
            summer.Id, [new ModificationMember { ItemId = costume.Id }]));
        summer = (await app.Services.Modifications.LoadAsync(summer.Id))!;

        var result = await ItemSelectionActions.AddPickedAsync(
            app.Services, [summer, winter], [costume], new MemberFilePickViewModel([costume]));

        Assert.Equal("「作り物の夏」には既に入っています。\n「作り物の冬」に追加しました。", result.Text);
        Assert.False(result.Failed);
        Assert.Single((await app.Services.Modifications.LoadAsync(summer.Id))!.Members);
        Assert.Single((await app.Services.Modifications.LoadAsync(winter.Id))!.Members);
    });

    /// <summary>前は商品ごとに保存して結果を見ず、消えた改変にも「追加しました」と言っていた（Codex の点検 2026-11-03 の2回目）。</summary>
    [Fact]
    public Task 送らずに足すときも_足せない改変で止め_足せた所までを言う() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1790001", "作り物のアバター"));
        var items = new[] { Make.Item("1790002", "作り物の衣装"), Make.Item("1790003", "作り物の髪") };
        await app.StartAsync();
        var summer = await CreateAsync(app, "作り物の夏");
        var gone = new ModificationRecord { Id = "mod-0000c003", AvatarItemId = "1790001", Name = "消された改変" };
        var later = await CreateAsync(app, "作り物の秋");

        var result = await ItemSelectionActions.AddPickedAsync(
            app.Services, [summer, gone, later], items, new MemberFilePickViewModel(items));

        Assert.True(result.Failed);
        Assert.True(result.Changed);
        Assert.StartsWith("「作り物の夏」に 2 件を追加しました。\n「消された改変」に追加できませんでした。\n\n", result.Text);
        Assert.DoesNotContain("Unity", result.Text);
        Assert.Equal(2, (await app.Services.Modifications.LoadAsync(summer.Id))!.Members.Count);
        Assert.Empty((await app.Services.Modifications.LoadAsync(later.Id))!.Members);
    });

    [Fact]
    public void 名前は3つまで並べ_それより多ければほか何件にする()
    {
        static ModificationRecord Mod(string name) => new() { Id = $"mod-{name}", AvatarItemId = "1", Name = name };

        Assert.Equal("「あ」「い」「う」", ModificationPicking.NamesText([Mod("あ"), Mod("い"), Mod("う")]));
        Assert.Equal("「あ」「い」ほか 2 件", ModificationPicking.NamesText([Mod("あ"), Mod("い"), Mod("う"), Mod("え")]));
    }

    /// <summary>窓の中の「選んでいる改変」は名前を省かない。絞って見えなくなった選びも分かるように（Codex の点検 2026-11-03 の2回目）。</summary>
    [Fact]
    public Task 選んでいる改変の行は_4つ以上でも名前を全部並べる() => TestApp.Run(async app =>
    {
        await app.StartAsync();
        var records = new[] { "あ", "い", "う", "え" }
            .Select(name => new ModificationRecord { Id = $"mod-{name}", AvatarItemId = "1790001", Name = name })
            .ToList();
        var model = ModificationPicking.BuildDialog(app.Services, "題", "見出し", "", records, "既存", "決定", "空");
        foreach (var row in model.Rows)
        {
            model.PickCommand.Execute(row);
        }

        Assert.Equal("選んでいる改変：「あ」「い」「う」「え」", model.PickedText);
    });
}
