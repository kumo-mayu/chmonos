using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変の複製（メモ44・2026-10-05）。左の一覧の行の右クリックと詳細の帯のボタンは同じ道で、
/// 複製したら新しい改変を右に開き、名前の欄へフォーカスを置く（置くのは画面。ここでは置く印を見る）。
/// </summary>
public class ModificationDuplicateTests
{
    private static async Task<(ModificationHubViewModel Hub, ModificationRecord Source, MainViewModel Main)> OpenAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("9900001", "作り物のアバター"));
        await app.AddItemAsync(Make.Item("9900002", "作り物の衣装"));
        var main = await app.StartAsync();
        var created = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("9900001", "夏の改変")));
        await app.Services.Commands.ExecuteAsync(new UiCommand.AddModificationMember(
            created.Record.Id, new ModificationMember { ItemId = "9900002" }));
        await app.Services.Commands.ExecuteAsync(new UiCommand.SetModificationMemo(created.Record.Id, "靴は最後"));

        var hub = new ModificationHubViewModel(app.Services, main, main.Thumbnails, ModificationHubLevel.Modification);
        await UiThread.Until(() => hub.EmptyText != "読み込んでいます…", "改変の画面の読み込みが済む");
        var source = (await app.Services.Modifications.LoadAsync(created.Record.Id))!;
        return (hub, source, main);
    }

    [Fact]
    public Task 行の右クリックの複製で_複製ができて右に開き_名前の欄へフォーカスを置く印が付く() => TestApp.Run(async app =>
    {
        var (hub, source, _) = await OpenAsync(app);

        hub.DuplicateModificationCommand.Execute(source);
        await UiThread.Until(() => hub.Detail is ModificationViewModel { Record.Name: "夏の改変のコピー" }, "複製が右に開く");

        var detail = Assert.IsType<ModificationViewModel>(hub.Detail);
        Assert.NotEqual(source.Id, detail.Record.Id);
        Assert.True(detail.IsEmbedded);
        Assert.True(detail.WantsNameFocus);
        Assert.Equal("靴は最後", detail.Record.Memo);
        Assert.Equal("9900002", Assert.Single(detail.Record.Members).ItemId);

        // 保存されている（元と並ぶ）
        var all = (await app.Services.Modifications.LoadAllAsync()).Modifications;
        Assert.Equal(2, all.Count);
    });

    [Fact]
    public Task 詳細のボタンも同じ道で複製し_2つ目には番号が付く() => TestApp.Run(async app =>
    {
        var (hub, source, _) = await OpenAsync(app);
        hub.ShowModificationCommand.Execute(source);
        var first = Assert.IsType<ModificationViewModel>(hub.Detail);
        Assert.False(first.WantsNameFocus);

        first.DuplicateCommand.Execute(null);
        await UiThread.Until(() => hub.Detail is ModificationViewModel { Record.Name: "夏の改変のコピー" }, "1つ目の複製が開く");
        var copy = Assert.IsType<ModificationViewModel>(hub.Detail);
        Assert.True(copy.WantsNameFocus);

        // 元をもう一度複製すると、見分けが付くように番号が付く
        hub.ShowModificationCommand.Execute(source);
        Assert.IsType<ModificationViewModel>(hub.Detail).DuplicateCommand.Execute(null);
        await UiThread.Until(() => hub.Detail is ModificationViewModel { Record.Name: "夏の改変のコピー 2" }, "2つ目の複製が開く");
    });

    [Fact]
    public Task 元が無いときは_何も開かず失敗の文を出す() => TestApp.Run(async app =>
    {
        var (hub, source, _) = await OpenAsync(app);
        await app.Services.Commands.ExecuteAsync(new UiCommand.DeleteModification(source.Id));

        await hub.DuplicateModificationAsync(source.Id);

        Assert.Null(hub.Detail);
        Assert.Equal("対象の改変が見つかりませんでした。", hub.Status);
    });
}
