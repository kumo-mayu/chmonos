using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 表示順の「改変に使った回数」（ユーザ指示 2026-10-07）。数えるのは改変の「使ったもの」に入れている改変の数で、
/// アバターにしている改変は数えない。1つの改変に2回入れても1回と数える
/// </summary>
public sealed class SearchModificationCountSortTests
{
    private static async Task<string> CreateAsync(TestApp app, string avatarId, string name, params string[] memberIds)
    {
        var created = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification(avatarId, name)));
        foreach (var memberId in memberIds)
        {
            await app.Services.Commands.ExecuteAsync(
                new UiCommand.AddModificationMember(created.Record.Id, new ModificationMember { ItemId = memberId }));
        }

        return created.Record.Id;
    }

    [Fact]
    public Task 改変に使った回数が多い順と少ない順に並び_アバターにしている改変は数えない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900700", "作り物 アバター"));
        await app.AddItemAsync(Make.Item("9900701", "作り物 二つの改変で使った衣装"));
        await app.AddItemAsync(Make.Item("9900702", "作り物 一つの改変で使った衣装"));
        await app.AddItemAsync(Make.Item("9900703", "作り物 使っていない衣装"));
        var main = await app.StartAsync();
        await CreateAsync(app, "9900700", "夏", "9900701", "9900702");
        await CreateAsync(app, "9900700", "冬", "9900701");
        var search = main.Search;
        search.NoteModificationsChanged();

        var field = search.SortFields.Single(entry => entry.Kind == SortKind.ModificationCount);
        Assert.Equal("改変に使った回数", field.Label);
        Assert.Equal("多い順", field.DescendingLabel);
        search.SortField = field;
        search.SortsDescending = true;
        await UiThread.Until(
            () => search.ListItems.Select(card => card.Item.Id).Take(3).SequenceEqual(["9900701", "9900702", "9900700"])
                || search.ListItems.Select(card => card.Item.Id).Take(3).SequenceEqual(["9900701", "9900702", "9900703"]),
            "改変を読んで並べ直す");

        // アバター（9900700）は使ったものではないので 0 回。使っていない衣装と同じ 0 回の並び
        var descending = search.ListItems.Select(card => card.Item.Id).ToList();
        Assert.Equal(["9900701", "9900702"], descending.Take(2));
        Assert.Equal(4, descending.Count);

        search.SortsAscending = true;
        await app.SettleAsync();
        Assert.Equal(["9900702", "9900701"], search.ListItems.Select(card => card.Item.Id).TakeLast(2));
    });
}
