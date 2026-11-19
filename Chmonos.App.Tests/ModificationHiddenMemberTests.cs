using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 非表示にした商品が「使ったもの」に入っていても、改変の詳細・改変の画面の左の行に出る
/// （非表示は検索・ショップの件数から除く物で、記録の読み出しからは除かない。feedback 2026-11-18）。作り物の名前
/// </summary>
public class ModificationHiddenMemberTests
{
    [Fact]
    public Task 非表示の商品も_使ったものの行に出る() => TestApp.Run(async app =>
    {
        var hidden = Make.Item("9900002", "作り物の衣装");
        hidden = hidden with { Local = hidden.Local with { IsHidden = true } };
        await app.AddItemAsync(Make.Item("9900001", "作り物のアバター"));
        await app.AddItemAsync(hidden);
        var main = await app.StartAsync();
        var created = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("9900001", "夏の改変")));
        await app.Services.Commands.ExecuteAsync(new UiCommand.AddModificationMember(
            created.Record.Id, new ModificationMember { ItemId = "9900002" }));

        // 改変の詳細：手元にある商品として（「手元にありません」ではなく）カードで出る
        var record = (await app.Services.Modifications.LoadAsync(created.Record.Id))!;
        var detail = new ModificationViewModel(record, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => detail.Members.Count == 1, "使ったものの行が並ぶ");
        var row = detail.Members.Single();
        Assert.Equal("作り物の衣装", row.Name);
        Assert.False(row.IsMissing);
        Assert.True(row.HasCard);

        // 改変の画面の左の行（改変の見方で開いた使ったもの）
        var hub = new ModificationHubViewModel(app.Services, main, main.Thumbnails, ModificationHubLevel.Modification);
        await UiThread.Until(() => hub.Lines.OfType<HubModLine>().Any(), "一覧が並ぶ");
        var members = hub.Lines.OfType<HubModLine>().Single().Row.Members;
        Assert.Contains(members, member => member.Name == "作り物の衣装" && !member.IsMissing);
    });
}
