using System.IO;
using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// 取り込み画面の監視フォルダの「新しいファイルを取り込む」（ユーザ指示 2026-11-03）。
/// 前は起動のときに見つけた分しか取り込めず、開いたまま監視フォルダに置いた物は起動し直すしかなかった。
/// </summary>
public class ImportWatchedNowTests
{
    [Fact]
    public Task 新しいファイルが無ければ_そう言う() => TestApp.Run(async app =>
    {
        var watched = Path.GetDirectoryName(app.NewFile(@"watched\keep.txt"))!;
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var main = await app.StartAsync();

        main.Import.ImportWatchedCommand.Execute(null);

        await UiThread.Until(() => main.Import.WatchImportText == "新しいファイルはありません。", "結果を言う");
        Assert.True(main.Import.ImportWatchedCommand.CanExecute(null));
    });

    [Fact]
    public Task 開いた後に置いたファイルを_取り込みに積んで始める() => TestApp.Run(async app =>
    {
        var watched = Path.GetDirectoryName(app.NewFile(@"watched\keep.txt"))!;
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var main = await app.StartAsync();
        var added = app.NewFile(@"watched\costume.zip", [1, 2, 3, 4]);

        main.Import.ImportWatchedCommand.Execute(null);

        await UiThread.Until(() => main.Import.WatchImportText == "新しいファイル 1 件を取り込みます。", "結果を言う");
        await UiThread.Until(() => main.Import.Folders.Contains(added) || main.Import.IsRunning, "取り込みに積む");
        await app.SettleAsync();
    });

    /// <summary>
    /// 取り込み対象の一覧は取り込みが終わっても残る。前は一覧全部で始め、前に取り込んだ物までもう一度取り込んでいた
    /// （Codex の点検 2026-11-03 の2回目）。落としたときも同じ道を通る。
    /// </summary>
    [Fact]
    public Task 一覧に前から残っている物は_取り込み直さない() => TestApp.Run(async app =>
    {
        var old = app.NewFile(@"old\before.zip", [1, 2, 3]);
        var watched = Path.GetDirectoryName(app.NewFile(@"watched\keep.txt"))!;
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var main = await app.StartAsync();
        main.Import.Folders.Add(old);
        var added = app.NewFile(@"watched\costume.zip", [1, 2, 3, 4]);
        IReadOnlyList<string>? started = null;
        app.Services.Imports.Started += work => started ??= work.Accepted;

        main.Import.ImportWatchedCommand.Execute(null);

        await UiThread.Until(() => started is not null, "取り込みが始まる");
        Assert.Equal([added], started!);
        await app.SettleAsync();
    });

    /// <summary>
    /// 書き込み途中（コピー・ダウンロードの途中）の物は今回は飛ばす（ユーザ判断 2026-11-13）。
    /// 前は渡していて、開けずにその1件の取り込みが失敗した。開いている間の監視は切のまま（張っていなくても確かめる）
    /// </summary>
    [Fact]
    public Task 書き込み途中の物は渡さず_そう言う() => TestApp.Run(async app =>
    {
        var watched = Path.GetDirectoryName(app.NewFile(@"watched\keep.txt"))!;
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var main = await app.StartAsync();
        var writing = Path.Combine(watched, "costume.zip");
        using (var stream = new FileStream(writing, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            stream.Write([1, 2, 3, 4]);
            stream.Flush();

            main.Import.ImportWatchedCommand.Execute(null);

            await UiThread.Until(() => main.Import.WatchImportText == "新しいファイルはまだ書き込み中です。", "飛ばしたと言う");
            Assert.DoesNotContain(writing, main.Import.Folders);
            Assert.False(main.Import.IsRunning);
        }

        // 書き終われば、次に押したときに取り込む
        main.Import.ImportWatchedCommand.Execute(null);
        await UiThread.Until(() => main.Import.WatchImportText == "新しいファイル 1 件を取り込みます。", "書き終わった物は取り込む");
        await app.SettleAsync();
    });

    [Fact]
    public Task 監視していなければ押せない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();

        Assert.False(main.Import.ImportWatchedCommand.CanExecute(null));
    });
}
