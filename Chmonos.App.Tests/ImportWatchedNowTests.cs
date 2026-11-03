using System.IO;
using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// 取り込み画面の監視対象の「新しいファイルを取り込む」（ユーザ指示 2026-11-03）。
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

    [Fact]
    public Task 監視していなければ押せない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();

        Assert.False(main.Import.ImportWatchedCommand.CanExecute(null));
    });
}
