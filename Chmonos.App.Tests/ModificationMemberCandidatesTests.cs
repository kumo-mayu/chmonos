using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変の「使ったものを追加」の候補（メモ25 B）。足した物とアバター自身は出さず、
/// 同じ名前の別の商品はショップ名で見分け、選んだ名前から正しい商品を足す。
/// </summary>
public class ModificationMemberCandidatesTests
{
    [Fact]
    public Task 足した物とアバター自身は候補に出さず_同じ名前はショップ名で見分けて正しい商品を足す() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物のアバター"));
        await app.AddItemAsync(Make.Item("1000002", "作り物の衣装", "shop-a"));
        await app.AddItemAsync(Make.Item("1000003", "作り物の衣装", "shop-b"));
        await app.AddItemAsync(Make.Item("1000004", "作り物の小物"));
        var main = await app.StartAsync();

        var created = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("1000001", "夏の改変")));
        var modification = new ModificationViewModel(created.Record, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => modification.ItemSuggestions.Count > 0, "候補が出る");

        Assert.Equal(
            ["作り物の衣装（shop-a）", "作り物の衣装（shop-b）", "作り物の小物"],
            modification.ItemSuggestions.ToList());

        modification.AddMemberCommand.Execute("作り物の衣装（shop-b）");
        await UiThread.Until(() => modification.Members.Count == 1, "足される");

        Assert.Equal("1000003", modification.Members.Single().Member.ItemId);
        await UiThread.Until(() => modification.ItemSuggestions.Count == 2, "足した物が候補から外れる");

        // 足した後は、残った同名が1件だけなのでショップ名は付かない
        Assert.Equal(
            ["作り物の衣装", "作り物の小物"],
            modification.ItemSuggestions.ToList());
    });
}
