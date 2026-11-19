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

    /// <summary>
    /// 押す前から件数を出す（ユーザ 2026-11-06：前は押して初めて出た）。画面を開いたときと、この画面のまま窓が前に戻ったときに数える。
    /// 数えるだけで取り込まない・BOOTH へは聞かない
    /// </summary>
    [Fact]
    public Task 取り込み画面を開くと_新しいファイルを数えて出し_取り込みはしない() => TestApp.Run(async app =>
    {
        var watched = Path.GetDirectoryName(app.NewFile(@"watched\keep.txt"))!;
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var main = await app.StartAsync();
        var added = app.NewFile(@"watched\costume.zip", [1, 2, 3, 4]);
        var asked = app.Booth.Requests.Count;

        main.ShowImport();

        await UiThread.Until(() => main.Import.WatchImportText == "新しいファイルが 1 件あります。", "数えて出す");
        await app.SettleAsync();
        Assert.DoesNotContain(added, main.Import.Folders);
        Assert.False(main.Import.IsRunning);
        Assert.Equal(asked, app.Booth.Requests.Count);
    });

    [Fact]
    public Task 取り込み画面のまま窓が前に戻ると_数え直し_ほかの画面では数えない() => TestApp.Run(async app =>
    {
        var watched = Path.GetDirectoryName(app.NewFile(@"watched\keep.txt"))!;
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var main = await app.StartAsync();
        main.ShowImport();
        await UiThread.Until(() => main.Import.WatchImportText == "新しいファイルはありません。", "開いたときに数える");

        // ほかの画面にいる間に置いて戻っても、取り込み画面でなければ数えない
        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();
        app.NewFile(@"watched\costume.zip", [1, 2, 3, 4]);
        main.NoteWindowActivated();
        await app.SettleAsync();
        Assert.Equal("新しいファイルはありません。", main.Import.WatchImportText);

        main.ShowImport();
        await UiThread.Until(() => main.Import.WatchImportText == "新しいファイルが 1 件あります。", "開き直すと数える");

        // 取り込み画面のまま外で置いて戻ってきた
        app.NewFile(@"watched\hair.zip", [5, 6, 7, 8]);
        main.NoteWindowActivated();
        await UiThread.Until(() => main.Import.WatchImportText == "新しいファイルが 2 件あります。", "戻ったら数え直す");
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

    /// <summary>
    /// 取り込み元の履歴には、見つけたファイルではなく監視フォルダを1行だけ載せる（ユーザ 2026-11-06。前はファイルが1つずつ並んだ）
    /// </summary>
    [Fact]
    public Task 取り込み元の履歴には_ファイルではなく監視フォルダを1行だけ載せる() => TestApp.Run(async app =>
    {
        var watched = Path.GetDirectoryName(app.NewFile(@"watched\keep.txt"))!;
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var main = await app.StartAsync();
        var costume = app.NewFile(@"watched\costume.zip", [1, 2, 3, 4]);
        var hair = app.NewFile(@"watched\hair.zip", [5, 6, 7, 8]);

        main.Import.ImportWatchedCommand.Execute(null);

        await UiThread.Until(
            () => app.Services.Settings.ImportFolders.Any(path => Core.Services.PathText.Same(path, watched)),
            "履歴に監視フォルダを載せる");
        await app.SettleAsync();
        var history = app.Services.Settings.ImportFolders;
        Assert.Single(history, path => Core.Services.PathText.Same(path, watched));
        Assert.DoesNotContain(history, path => Core.Services.PathText.Same(path, costume) || Core.Services.PathText.Same(path, hair));
    });

    [Fact]
    public void 履歴に載せる監視フォルダは_ファイルごとに一番内側で_どこにも入っていなければそのまま()
    {
        var sources = ViewModels.ImportViewModel.WatchedSources(
            [@"D:\監視\a.zip", @"D:\監視\b.zip", @"D:\監視\中\c.zip", @"D:\監視-別\d.zip", @"E:\外\e.zip"],
            [@"D:\監視", @"D:\監視\中\", @"D:\監視-別"]);

        Assert.Equal([@"D:\監視", @"D:\監視\中\", @"D:\監視-別", @"E:\外\e.zip"], sources);
    }

    /// <summary>新着と続きが混ざった対象は、新着だけを監視フォルダに替え、続きはそのまま載せる（ユーザ判断 2026-11-20）</summary>
    [Fact]
    public void 新着と続きが混ざった履歴は_新着だけを監視フォルダに替える()
    {
        var history = ViewModels.ImportViewModel.HistoryWithWatchedSources(
            [@"D:\監視\a.zip", @"D:\続き", @"D:\監視\b.zip", @"D:\監視\前から.zip"],
            [@"D:\監視\a.zip", @"D:\監視\B.zip"],
            [@"D:\監視"]);

        Assert.Equal([@"D:\監視", @"D:\続き", @"D:\監視\前から.zip"], history);
    }

    private static void AssertHistoryHasOnlyTheFolder(TestApp app, string watched, params string[] files)
    {
        var history = app.Services.Settings.ImportFolders;
        Assert.Single(history, path => Core.Services.PathText.Same(path, watched));
        Assert.DoesNotContain(history, path => files.Any(file => Core.Services.PathText.Same(path, file)));
    }

    /// <summary>開いている間の監視の自動の取り込みも、履歴には監視フォルダを1行だけ載せる（ユーザ判断 2026-11-20）</summary>
    [Fact]
    public Task 開いている間の自動の取り込みも_履歴には監視フォルダを1行だけ載せる() => TestApp.Run(async app =>
    {
        var watched = Path.GetDirectoryName(app.NewFile(@"watched\keep.txt"))!;
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var main = await app.StartAsync();
        var costume = app.NewFile(@"watched\costume.zip", [1, 2, 3, 4]);
        var hair = app.NewFile(@"watched\hair.zip", [5, 6, 7, 8]);

        Assert.True(await main.Import.StartWatchedAutoAsync([costume, hair], () => { }));

        await UiThread.Until(
            () => app.Services.Settings.ImportFolders.Any(path => Core.Services.PathText.Same(path, watched)),
            "履歴に監視フォルダを載せる");
        await app.SettleAsync();
        AssertHistoryHasOnlyTheFolder(app, watched, costume, hair);
    });

    /// <summary>起動時の自動の取り込みも、履歴には監視フォルダを1行だけ載せる（ユーザ判断 2026-11-20）</summary>
    [Fact]
    public Task 起動時の自動の取り込みも_履歴には監視フォルダを1行だけ載せる() => TestApp.Run(async app =>
    {
        var watched = Path.GetDirectoryName(app.NewFile(@"watched\keep.txt"))!;
        var costume = app.NewFile(@"watched\costume.zip", [1, 2, 3, 4]);
        var hair = app.NewFile(@"watched\hair.zip", [5, 6, 7, 8]);
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched], StartImportOnLaunch = true });

        var main = await app.StartAsync();

        await UiThread.Until(() => main.Import.Summary is not null, "起動時に取り込みが走って終わる");
        await app.SettleAsync();
        AssertHistoryHasOnlyTheFolder(app, watched, costume, hair);
    });

    /// <summary>
    /// 起動時に見つけた新着を帯から取り込み対象へ積み、後で始めたときも、履歴には監視フォルダを1行だけ載せる（ユーザ判断 2026-11-20）。
    /// 一緒に積んだほかの物は、そのまま載せる
    /// </summary>
    [Fact]
    public Task 帯の新着を積んで始めても_履歴には監視フォルダを1行だけ載せる() => TestApp.Run(async app =>
    {
        var watched = Path.GetDirectoryName(app.NewFile(@"watched\keep.txt"))!;
        var costume = app.NewFile(@"watched\costume.zip", [1, 2, 3, 4]);
        var hair = app.NewFile(@"watched\hair.zip", [5, 6, 7, 8]);
        var other = app.NewFile(@"other\dropped.zip", [9, 9, 9]);
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var main = await app.StartAsync();
        await UiThread.Until(() => main.HasWatchedNew, "起動時に新着を見つける");

        main.TakeWatchedNew();
        main.Import.AddDroppedPaths([other]);
        await UiThread.Until(() => main.Import.Folders.Count == 3, "取り込み対象に積む");
        Assert.False(main.Import.IsRunning);
        main.Import.StartCommand.Execute(null);

        await UiThread.Until(() => main.Import.Summary is not null, "取り込みが走って終わる");
        await app.SettleAsync();
        AssertHistoryHasOnlyTheFolder(app, watched, costume, hair);
        Assert.Contains(app.Services.Settings.ImportFolders, path => Core.Services.PathText.Same(path, other));
    });

    /// <summary>
    /// 取り込み対象の一覧は取り込みが終わっても残り、後の「開始」は一覧全部で始める。そのときも、新着だったファイルは1つずつ載せない（ユーザ判断 2026-11-20）
    /// </summary>
    [Fact]
    public Task 新着を取り込んだ後に開始で一覧全部を始めても_ファイルは履歴に載せない() => TestApp.Run(async app =>
    {
        var watched = Path.GetDirectoryName(app.NewFile(@"watched\keep.txt"))!;
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var main = await app.StartAsync();
        var costume = app.NewFile(@"watched\costume.zip", [1, 2, 3, 4]);
        var other = app.NewFile(@"other\dropped.zip", [9, 9, 9]);

        main.Import.ImportWatchedCommand.Execute(null);
        await UiThread.Until(() => main.Import.Summary is not null, "新着の取り込みが終わる");
        await app.SettleAsync();

        main.Import.AddDroppedPaths([other]);
        await UiThread.Until(() => main.Import.Folders.Count == 2, "取り込み対象に積む");
        main.Import.StartCommand.Execute(null);
        await UiThread.Until(
            () => app.Services.Settings.ImportFolders.Any(path => Core.Services.PathText.Same(path, other)),
            "2回目の取り込みの履歴を書く");
        await app.SettleAsync();

        AssertHistoryHasOnlyTheFolder(app, watched, costume);
    });

    [Fact]
    public Task 監視していなければ押せない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();

        Assert.False(main.Import.ImportWatchedCommand.CanExecute(null));
    });
}
