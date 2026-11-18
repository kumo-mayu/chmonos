using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// 取り込み対象に積んだまま始めていない物を、対象の欄の上に数と［開始］で知らせる（open 122・ユーザ判断 2026-11-19）。
/// 取り込み対象の一覧は取り込みが終わっても残るので、「始めていない」は一覧の有無ではなく積んだ時の印で決める
/// </summary>
public class ImportNotStartedTests
{
    [Fact]
    public Task 積んだだけの対象は数を言い_開始でその対象だけ取り込む() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var done = app.NewFile(@"done\before.zip", [1, 2, 3]);
        var fresh = app.NewFile(@"fresh\costume.zip", [1, 2, 3, 4]);
        var started = new List<IReadOnlyList<string>>();
        app.Services.Imports.Started += work => started.Add(work.Accepted);

        // 1本目は積んですぐ始める（終わっても一覧には残る）
        main.Import.Folders.Add(done);
        Assert.True(main.Import.ShowsNotStarted);
        main.Import.StartCommand.Execute(null);
        await UiThread.Until(() => started.Count == 1, "1本目が始まる");
        await UiThread.Until(() => !main.Import.IsRunning, "1本目が終わる");
        await app.SettleAsync();
        Assert.False(main.Import.ShowsNotStarted);

        // ほかの画面で対象に入れて移ってきた形
        main.Import.TakeFromHistoryCommand.Execute(fresh);

        Assert.True(main.Import.ShowsNotStarted);
        Assert.Equal("まだ始めていない対象が 1 件あります。", main.Import.NotStartedText);

        main.Import.StartNotStartedCommand.Execute(null);
        await UiThread.Until(() => started.Count == 2, "2本目が始まる");
        Assert.Equal([fresh], started[1]);
        Assert.False(main.Import.ShowsNotStarted);
        await UiThread.Until(() => !main.Import.IsRunning, "2本目が終わる");
        await app.SettleAsync();
        Assert.False(main.Import.ShowsNotStarted);
    });

    [Fact]
    public Task 取り込み中は出さず_外した対象は数えない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var first = app.NewFile(@"a\one.zip", [1, 2, 3]);
        var second = app.NewFile(@"b\two.zip", [1, 2, 3, 4]);

        main.Import.TakeFromHistoryCommand.Execute(first);
        main.Import.TakeFromHistoryCommand.Execute(second);
        Assert.Equal(2, main.Import.NotStartedCount);

        main.Import.Folders.Remove(first);
        Assert.Equal(1, main.Import.NotStartedCount);

        main.Import.Folders.Clear();
        Assert.Equal(0, main.Import.NotStartedCount);
        Assert.False(main.Import.ShowsNotStarted);
        await app.SettleAsync();
    });
}
