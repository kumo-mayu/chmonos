using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 見つからない登録フォルダの候補を、取り込み画面の探した結果の下に並べ、人が選んだら差し替える（見つからない・移動の点検 10-A）。
/// </summary>
public class MovedFolderCandidateTests
{
    private static string MakeFolder(string path)
    {
        Directory.CreateDirectory(Path.Combine(path, "texture"));
        File.WriteAllBytes(Path.Combine(path, "costume.unitypackage"), new byte[300]);
        File.WriteAllBytes(Path.Combine(path, "texture", "a.psd"), new byte[200]);
        return path;
    }

    private static async Task<(string Old, string Moved, string Watched)> ArrangeAsync(TestApp app)
    {
        var watched = Path.Combine(app.Root, "files", "watched");
        var old = Path.Combine(app.Root, "files", "old", "costume_v1");
        var moved = MakeFolder(Path.Combine(watched, "costume_v1"));
        await app.AddItemAsync(Make.Item("9900601", "作り物の衣装") with
        {
            Local = new LocalBlock
            {
                LocalFolders =
                [
                    new LocalFolderRecord
                    {
                        Path = old,
                        FileCount = 2,
                        TotalBytes = 500,
                        RegisteredAt = DateTimeOffset.UnixEpoch,
                        MissingSince = DateTimeOffset.UnixEpoch,
                    },
                ],
            },
        });
        return (old, moved, watched);
    }

    [Fact]
    public Task 探すと見つからない登録フォルダに候補が並び_押すまで場所は変えない() => TestApp.Run(async app =>
    {
        var (old, moved, watched) = await ArrangeAsync(app);
        var main = await app.StartAsync();
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });

        main.Import.FindMissingFilesCommand.Execute(null);
        await app.SettleAsync();

        var row = Assert.Single(main.Import.MissingFolders);
        Assert.True(main.Import.HasMissingFolders);
        Assert.Equal("作り物の衣装", row.ItemName);
        Assert.Equal(old, row.Path);
        Assert.Equal("2 ファイル・500 B", row.CountText);
        var candidate = Assert.Single(row.Candidates);
        Assert.Equal(moved, candidate.Path);
        Assert.Equal("名前・ファイル数・サイズが同じ", candidate.KindText);
        Assert.False(row.HasStatus);
        Assert.Equal(old, Assert.Single((await app.Store.Items.LoadAsync("9900601"))!.Local.LocalFolders).Path);
    });

    [Fact]
    public Task この場所にするを押すと差し替わり_行に結果が出て_カードの印が消える() => TestApp.Run(async app =>
    {
        var (_, moved, watched) = await ArrangeAsync(app);
        var main = await app.StartAsync();
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        Assert.True(main.Search.ListItems.Single().HasMissingFile);
        main.Import.FindMissingFilesCommand.Execute(null);
        await app.SettleAsync();
        var row = Assert.Single(main.Import.MissingFolders);

        main.Import.UseFolderCandidateCommand.Execute(Assert.Single(row.Candidates));
        await app.SettleAsync();

        var folder = Assert.Single((await app.Store.Items.LoadAsync("9900601"))!.Local.LocalFolders);
        Assert.Equal(moved, folder.Path);
        Assert.Null(folder.MissingSince);
        Assert.Empty(row.Candidates);
        Assert.Equal($"「{moved}」に差し替えました。", row.StatusText);
        await UiThread.Until(() => !main.Search.ListItems.Single().HasMissingFile, "カードの印が消える");
    });

    [Fact]
    public Task 押す前に候補のフォルダが無くなっていれば_差し替えず理由を行に出す() => TestApp.Run(async app =>
    {
        var (old, moved, watched) = await ArrangeAsync(app);
        var main = await app.StartAsync();
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        main.Import.FindMissingFilesCommand.Execute(null);
        await app.SettleAsync();
        var row = Assert.Single(main.Import.MissingFolders);
        Directory.Delete(moved, recursive: true);

        main.Import.UseFolderCandidateCommand.Execute(Assert.Single(row.Candidates));
        await app.SettleAsync();

        Assert.Equal("選んだフォルダが見つかりません。もう一度探してください。", row.StatusText);
        Assert.Equal(old, Assert.Single((await app.Store.Items.LoadAsync("9900601"))!.Local.LocalFolders).Path);
    });

    [Fact]
    public void 候補が無い登録フォルダは_合うフォルダが無かったと言う()
    {
        var row = new MissingFolderRow(new MissingFolder { ItemId = "9900602", ItemName = "作り物の髪型", Path = @"D:\old\hair" });

        Assert.Empty(row.Candidates);
        Assert.Equal("探したフォルダの中に、合うフォルダはありませんでした。移した先のフォルダを追加して、もう一度探してください。", row.StatusText);
    }

    [Theory]
    [InlineData(FolderMatchKind.NameAndContents, "名前・ファイル数・サイズが同じ")]
    [InlineData(FolderMatchKind.Contents, "ファイル数・サイズが同じ")]
    [InlineData(FolderMatchKind.Name, "名前が同じ")]
    public void 合い方の言い方(FolderMatchKind kind, string expected)
        => Assert.Equal(expected, ImportViewModel.MatchText(kind));

    [Fact]
    public void 中を数えられなかった候補は_数を出さない()
        => Assert.Equal(string.Empty, ImportViewModel.CountText(null, null));
}
