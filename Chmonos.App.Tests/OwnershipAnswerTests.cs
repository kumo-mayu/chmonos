using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 所持の答えが画面で食い違わないこと（file-lifecycle.md「気になった所」6）。
/// 所持＝ファイルかフォルダを1つ以上持つ。フォルダだけの商品が、検索のカードで「未取得」・改変の画面で「無い」にならない。
/// </summary>
public class OwnershipAnswerTests
{
    private const string Avatar = "9900001";
    private const string FolderOnly = "9900002";

    private static ItemRecord FolderOnlyItem()
        => Make.Item(FolderOnly, "作り物のフォルダだけの商品").WithFiles() with
        {
            Local = new LocalBlock
            {
                LocalFolders = [new LocalFolderRecord { Path = @"D:\folders\9900002", FileCount = 4, TotalBytes = 2048 }],
            },
        };

    [Fact]
    public Task フォルダだけの商品は_検索のカードで所持になり容量が出る() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(FolderOnlyItem());
        var main = await app.StartAsync();
        await app.SettleAsync();

        var card = main.Search.CreateCard(FolderOnlyItem());

        Assert.True(card.IsOwned);
        Assert.Equal(DisplayText.Size(2048), card.SizeText);
    });

    [Fact]
    public Task 何も持たない商品は_検索のカードで未取得のまま() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item(FolderOnly, "作り物の何も無い商品").WithFiles());
        var main = await app.StartAsync();

        var card = main.Search.CreateCard(Make.Item(FolderOnly, "作り物の何も無い商品").WithFiles());

        Assert.False(card.IsOwned);
        Assert.Equal("未取得", card.SizeText);
    });

    [Fact]
    public Task フォルダだけの商品は_改変の使ったものの行で無い扱いにならない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item(Avatar, "作り物のアバター"));
        await app.AddItemAsync(FolderOnlyItem());
        var main = await app.StartAsync();
        var created = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification(Avatar, "夏の改変")));
        await app.Services.Commands.ExecuteAsync(new UiCommand.AddModificationMember(
            created.Record.Id, new ModificationMember { ItemId = FolderOnly }));

        var record = await app.Services.Modifications.LoadAsync(created.Record.Id);
        var detail = new ModificationViewModel(record!, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => detail.Members.Count == 1, "使ったものが並ぶ");

        Assert.False(detail.Members.Single().IsMissing);
    });
}
