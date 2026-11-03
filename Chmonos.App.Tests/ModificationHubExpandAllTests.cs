using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変の画面の「すべて畳む」「すべて広げる」（ユーザ指示 2026-11-03）。
/// 開け閉めはアプリを閉じるまで覚える（<c>HubExpansion</c>・プロセスで1つ）ので、ほかの試験と重ならない商品IDを使う。
/// </summary>
public class ModificationHubExpandAllTests
{
    private static async Task<ModificationHubViewModel> OpenAsync(TestApp app, ModificationHubLevel level, params string[] names)
    {
        await app.AddItemAsync(Make.Item("1780001", "作り物のアバター"));
        await app.AddItemAsync(Make.Item("1780002", "作り物の衣装"));
        var main = await app.StartAsync();
        foreach (var name in names)
        {
            var created = Assert.IsType<CommandResult.ModificationCreated>(
                await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("1780001", name)));
            await app.Services.Commands.ExecuteAsync(new UiCommand.AddModificationMember(created.Record.Id, new Chmonos.Core.Models.ModificationMember { ItemId = "1780002" }));
        }

        var hub = new ModificationHubViewModel(app.Services, main, main.Thumbnails, level);
        await UiThread.Until(() => hub.Groups.Count > 0, "一覧が出る");
        return hub;
    }

    private static IEnumerable<HubExpandable> All(ModificationHubViewModel hub)
        => hub.Groups.OfType<HubExpandable>()
            .Concat(hub.Groups.OfType<HubAvatarGroup>().SelectMany(group => group.Modifications));

    [Fact]
    public Task すべて畳むは中身まで畳み_すべて広げるは一番上の段だけ広げる() => TestApp.Run(async app =>
    {
        var hub = await OpenAsync(app, ModificationHubLevel.Avatar, "作り物の夏", "作り物の冬");
        var avatar = Assert.IsType<HubAvatarGroup>(Assert.Single(hub.Groups));
        avatar.Modifications[0].IsExpanded = true;

        hub.CollapseAllCommand.Execute(null);

        Assert.All(All(hub), row => Assert.False(row.IsExpanded));
        Assert.Single(hub.Lines.OfType<HubAvatarLine>());
        Assert.Empty(hub.Lines.OfType<HubModLine>());

        hub.ExpandAllCommand.Execute(null);

        Assert.True(avatar.IsExpanded);
        Assert.All(avatar.Modifications, mod => Assert.False(mod.IsExpanded));
        Assert.Equal(2, hub.Lines.OfType<HubModLine>().Count());
        Assert.Empty(hub.Lines.OfType<HubMemberLine>());
    });

    [Fact]
    public Task 改変の見方では_すべて広げるで改変の中身を出す() => TestApp.Run(async app =>
    {
        var hub = await OpenAsync(app, ModificationHubLevel.Modification, "作り物の秋");
        var mod = Assert.IsType<HubModificationRow>(Assert.Single(hub.Groups));

        hub.CollapseAllCommand.Execute(null);
        Assert.False(mod.IsExpanded);

        hub.ExpandAllCommand.Execute(null);
        Assert.True(mod.IsExpanded);
    });
}
