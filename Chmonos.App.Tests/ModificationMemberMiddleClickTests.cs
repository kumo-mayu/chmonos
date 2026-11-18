using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変の画面の使ったものの名前・絵を中クリックすると BOOTH を開く（open.md 136・ユーザ判断 2026-11-19）。
/// 開く先が無い物（BOOTH に無い商品）には付けない＝命令が押せない扱いになる。
/// </summary>
public sealed class ModificationMemberMiddleClickTests
{
    private static ModificationMemberRowViewModel Row(string itemId, ItemCardViewModel? card = null) => new()
    {
        Index = 0,
        Total = 1,
        Member = new ModificationMember { ItemId = itemId },
        Name = "作り物の衣装",
        SourceText = string.Empty,
        IsMissing = card is null,
        VariationText = string.Empty,
        Card = card,
    };

    [Fact]
    public Task BOOTHの番号の商品は開ける_番号でない物と行でない物は開けない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物のアバター"));
        var main = await app.StartAsync();
        var created = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("1000001", "夏の改変")));
        var modification = new ModificationViewModel(created.Record, app.Services, main, main.Thumbnails);

        var command = modification.OpenMemberBoothCommand;
        Assert.True(command.CanExecute(Row("1000002")));
        Assert.False(command.CanExecute(Row("local-abc")));
        Assert.False(command.CanExecute(null));
        Assert.False(command.CanExecute("1000002"));
    });
}
