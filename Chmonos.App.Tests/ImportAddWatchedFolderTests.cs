using System.IO;
using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// 取り込み画面の監視の欄の「フォルダを追加…」（ユーザ判断 2026-11-20）。取り込み対象を通さずに監視フォルダに足すだけで、
/// 中のまだ取り込んでいないファイルの数を言う。取り込みはしない・BOOTH へは聞かない
/// </summary>
public class ImportAddWatchedFolderTests
{
    [Fact]
    public Task 監視フォルダに足すだけで_取り込み対象には入れず_まだ取り込んでいない数を言う() => TestApp.Run(async app =>
    {
        var folder = Path.GetDirectoryName(app.NewFile(@"new-watch\costume.zip", [1, 2, 3, 4]))!;
        app.NewFile(@"new-watch\hair.zip", [5, 6, 7, 8]);
        var main = await app.StartAsync();
        var asked = app.Booth.Requests.Count;

        await main.Import.AddWatchedFoldersAsync([folder]);

        Assert.Equal($"「{folder}」を監視フォルダに追加しました。中のファイル 2 件はまだ取り込んでいません。", main.Import.WatchImportText);
        Assert.Contains(folder, main.Import.Watched);
        Assert.Contains(app.Services.Settings.WatchedFolders, known => string.Equals(known, folder, StringComparison.OrdinalIgnoreCase));
        await app.SettleAsync();
        Assert.Empty(main.Import.Folders);
        Assert.False(main.Import.IsRunning);
        Assert.Equal(asked, app.Booth.Requests.Count);
    });

    [Fact]
    public Task 中に取り込む物が無ければ_足した事だけを言う() => TestApp.Run(async app =>
    {
        var folder = Path.Combine(app.Root, "empty-watch");
        Directory.CreateDirectory(folder);
        var main = await app.StartAsync();

        await main.Import.AddWatchedFoldersAsync([folder]);

        Assert.Equal($"「{folder}」を監視フォルダに追加しました。", main.Import.WatchImportText);
        Assert.True(main.Import.HasWatched);
    });

    [Fact]
    public Task 既に監視しているフォルダは_重ねて足さずにそう言う() => TestApp.Run(async app =>
    {
        var folder = Path.GetDirectoryName(app.NewFile(@"watched\keep.txt"))!;
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [folder] });
        var main = await app.StartAsync();

        await main.Import.AddWatchedFoldersAsync([folder.ToUpperInvariant()]);

        Assert.Equal($"「{folder.ToUpperInvariant()}」は既に監視フォルダです。", main.Import.WatchImportText);
        Assert.Single(main.Import.Watched);
        Assert.Single(app.Services.Settings.WatchedFolders);
    });

    [Fact]
    public Task 幾つか選ぶと_新しい物だけ足して件数で言う() => TestApp.Run(async app =>
    {
        var known = Path.GetDirectoryName(app.NewFile(@"watched\keep.txt"))!;
        var first = Path.GetDirectoryName(app.NewFile(@"a\costume.zip", [1, 2, 3, 4]))!;
        var second = Path.GetDirectoryName(app.NewFile(@"b\hair.zip", [5, 6, 7, 8]))!;
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [known] });
        var main = await app.StartAsync();

        await main.Import.AddWatchedFoldersAsync([known, first, second]);

        Assert.Equal("監視フォルダを 2 件追加しました。中のファイル 2 件はまだ取り込んでいません。", main.Import.WatchImportText);
        Assert.Equal(3, app.Services.Settings.WatchedFolders.Count);
    });
}
