using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 使っている商品の更新の促し（<c>docs/spec/used-updates.md</c>）：商品ページの帯と、改変の行の札。名前・ID は作り物。
/// </summary>
public class UsedUpdatePromptTests
{
    private const string Avatar = "9907401";
    private const string Costume = "9907402";

    /// <summary>中身の更新を見つけてある衣装。持っているファイルは更新の前からの1つ。</summary>
    private static ItemRecord UpdatedCostume()
    {
        var item = Make.Item(Costume, "作り物の衣装");
        return item with { Local = item.Local with { ContentUpdate = UsedUpdates.Found(item.Local, DateTimeOffset.Now.AddDays(-1)) } };
    }

    private static async Task<string> AddModificationAsync(TestApp app, string? fileHash)
    {
        var created = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification(Avatar, "作り物の改変")));
        await app.Services.Commands.ExecuteAsync(new UiCommand.AddModificationMember(
            created.Record.Id, new ModificationMember { ItemId = Costume, FileHash = fileHash }));
        return created.Record.Id;
    }

    private static async Task<ItemViewModel> OpenItemAsync(TestApp app, MainViewModel main)
    {
        await main.UsedItems.RequestAsync();
        main.ShowItem((await app.Store.Items.LoadAsync(Costume))!);
        var page = Assert.IsType<ItemViewModel>(main.CurrentViewModel);
        await app.SettleAsync();
        return page;
    }

    [Fact]
    public Task 改変で使っている商品に新しい版が出たら帯を出し_確認したで消える() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item(Avatar, "作り物のアバター"));
        await app.AddItemAsync(UpdatedCostume());
        var main = await app.StartAsync();
        await AddModificationAsync(app, fileHash: null);

        var page = await OpenItemAsync(app, main);

        Assert.True(page.ShowsUsedUpdateBar);
        Assert.Equal("改変 1 件で使っています。新しい版が出ています。", page.UsedUpdateText);

        page.CheckContentUpdateCommand.Execute(null);
        await UiThread.Until(() => !page.ShowsUsedUpdateBar, "帯が消える");

        Assert.NotNull((await app.Store.Items.LoadAsync(Costume))!.Local.ContentUpdate!.CheckedAt);
    });

    [Fact]
    public Task 使っていない商品には帯を出さない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(UpdatedCostume());
        var main = await app.StartAsync();

        var page = await OpenItemAsync(app, main);

        Assert.False(page.ShowsUsedUpdateBar);
    });

    [Fact]
    public Task 改変の行は更新の前のファイルなら札を出し_改変の確認したで消える() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item(Avatar, "作り物のアバター"));
        var costume = UpdatedCostume();
        await app.AddItemAsync(costume);
        var main = await app.StartAsync();
        var id = await AddModificationAsync(app, fileHash: costume.Local.LocalFiles[0].Hash);

        var detail = new ModificationViewModel((await app.Services.Modifications.LoadAsync(id))!, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => detail.Members.Count == 1, "使ったものが並ぶ");
        Assert.True(detail.Members[0].IsBehind);

        detail.CheckMemberUpdateCommand.Execute(detail.Members[0]);
        await UiThread.Until(() => detail.Members.Count == 1 && !detail.Members[0].IsBehind, "札が消える");

        Assert.NotNull((await app.Services.Modifications.LoadAsync(id))!.Members[0].UpdateCheckedAt);

        // 商品の促しは改変の確認では消えない（改変ごと・ユーザ判断 2026-11-09）
        Assert.True((await app.Store.Items.LoadAsync(Costume))!.Local.HasPendingContentUpdate);
    });
}
