using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;

namespace Chmonos.App.Tests;

/// <summary>
/// 取り込み画面の「展開先フォルダの削除」は、商品に登録したフォルダを消さない（2026-10-05・file-lifecycle.md「気になった所」4）。
/// 判定は Core（<see cref="UnpackedFolderRemover"/>）の試験で確かめ、ここはアプリの組み立てが今の登録を渡しているかを見る。
/// 消さないので、ごみ箱へ送る道は通らない。
/// </summary>
public class UnpackedRemovalTests
{
    [Fact]
    public Task 商品に登録したフォルダは_展開先として選んでも消さず理由を返す() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_1.0.zip");
        var folder = Path.Combine(Path.GetDirectoryName(zip)!, "作り物_1.0");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "中身.png"), [1, 2, 3]);

        var item = Make.Item("9900031", "作り物の衣装");
        await app.AddItemAsync(item with
        {
            Local = item.Local with
            {
                LocalFolders = [new LocalFolderRecord { Path = folder, FileCount = 1, TotalBytes = 3, RegisteredAt = DateTimeOffset.UnixEpoch }],
            },
        });
        await app.StartAsync();

        var result = await app.Services.Commands.ExecuteAsync(new UiCommand.RemoveUnpackedFolders(
        [
            new UnpackedFolder { Path = folder, ArchivePath = zip, FileCount = 1, TotalBytes = 3 },
        ]));

        var removal = Assert.Single(Assert.IsType<CommandResult.UnpackedFoldersRemoved>(result).Results);
        Assert.False(removal.Removed);
        Assert.Equal("商品に登録したフォルダです。削除しません。", removal.Reason);
        Assert.True(Directory.Exists(folder));
    });
}
