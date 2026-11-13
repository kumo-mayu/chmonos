using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Booth;

namespace Chmonos.App.Tests;

/// <summary>
/// 開いている間の監視フォルダの見張り（plan 49・ユーザ判断 2026-11-13「常時監視自体にオンオフをつけて、そのうえでオンの時は既定で帯を出し、
/// 設定から自動取り込みを許可できるようにしよう。優先順位は普通の取り込みと同じで良いと思う。」）。
/// 見張りは作り物（<see cref="TestApp.FolderEvents"/>）で、待ちは0。出来事は試験が起こす。
/// </summary>
public class LiveWatchNoticeTests
{
    private const string ItemId = "7654321";

    private static async Task<string> WatchAsync(TestApp app, bool live, bool auto = false)
    {
        var watched = Path.GetDirectoryName(app.NewFile(@"watched\keep.txt"))!;
        await app.ChangeSettingsAsync(settings => settings with
        {
            WatchedFolders = [watched],
            WatchFoldersWhileOpen = live,
            AutoImportWatchedFiles = auto,
        });
        return watched;
    }

    /// <summary>ダウンロード元（Zone.Identifier）から商品が決まるファイルを置く。取り込みが①で BOOTH に問い合わせる。</summary>
    private static string PutDownloaded(TestApp app, string name, string itemId)
    {
        var path = app.NewFile(name, [1, 2, 3, 4, 5]);
        File.WriteAllText(path + ":Zone.Identifier", $"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://booth.pm/ja/items/{itemId}\r\n");
        return path;
    }

    /// <summary>関係の無い出来事で1回探し直させ、渡し終えて画面が受け取るまで待つ（「探し直しても出さない」を、探し直しが済んでから確かめる）。</summary>
    private static async Task RescanAsync(TestApp app, MainViewModel main, string watched)
    {
        var before = main.LiveWatch!.Passes;
        app.FolderEvents.Fire(watched, Path.Combine(watched, "keep.txt"));
        await UiThread.Until(() => main.LiveWatch!.Passes > before, "探し直しが済む");
        await app.SettleAsync();
    }

    private static async Task BaselineAsync(MainViewModel main)
    {
        // 商品が無いと取り込み画面から始まる。帯は取り込み画面では出さない（同じことがその画面に出ている）ので、検索へ移っておく
        main.ShowSearch();

        await UiThread.Until(() => main.LiveWatch is { IsRunning: true }, "見張りが始まる");
        await main.LiveWatch!.BaselineTaken;
    }

    [Fact]
    public Task 切なら見張らず_帯も出ない() => TestApp.Run(async app =>
    {
        await WatchAsync(app, live: false);
        app.NewFile(@"watched\costume.zip", [1, 2, 3, 4]);
        var main = await app.StartAsync();

        Assert.Empty(app.FolderEvents.All);
        Assert.Null(main.LiveWatch);
        Assert.False(main.HasLiveWatchNotice);
    });

    [Fact]
    public Task 入なら_置かれたファイルを下の帯で知らせ_取り込みは始めない() => TestApp.Run(async app =>
    {
        var watched = await WatchAsync(app, live: true);
        var main = await app.StartAsync();
        await BaselineAsync(main);

        var added = app.NewFile(@"watched\costume.zip", [1, 2, 3, 4]);
        app.FolderEvents.Fire(watched, added);

        await UiThread.Until(() => main.HasLiveWatchNotice, "帯が出る");
        Assert.Equal("監視フォルダに新しいファイルが 1 件あります。", main.LiveWatchNoticeText);
        Assert.Null(app.Services.Imports.Running);
        Assert.Empty(main.Import.Folders);
    });

    [Fact]
    public Task 帯の取り込むで_今の新着を取り込み_帯を下げる() => TestApp.Run(async app =>
    {
        var watched = await WatchAsync(app, live: true);
        var main = await app.StartAsync();
        await BaselineAsync(main);
        IReadOnlyList<string>? started = null;
        app.Services.Imports.Started += work => started ??= work.Accepted;

        var added = app.NewFile(@"watched\costume.zip", [1, 2, 3, 4]);
        app.FolderEvents.Fire(watched, added);
        await UiThread.Until(() => main.HasLiveWatchNotice, "帯が出る");

        main.TakeLiveWatchedCommand.Execute(null);

        await UiThread.Until(() => started is not null, "取り込みが始まる");
        Assert.Equal([added], started!);
        Assert.False(main.HasLiveWatchNotice);
        await app.SettleAsync();
    });

    /// <summary>× は「知らせなくてよい」と同じ。同じ顔ぶれのままなら、関係の無い出来事で探し直しても出し直さない。</summary>
    [Fact]
    public Task 帯を閉じた顔ぶれは_探し直しても出し直さない() => TestApp.Run(async app =>
    {
        var watched = await WatchAsync(app, live: true);
        var main = await app.StartAsync();
        await BaselineAsync(main);
        var added = app.NewFile(@"watched\costume.zip", [1, 2, 3, 4]);
        app.FolderEvents.Fire(watched, added);
        await UiThread.Until(() => main.HasLiveWatchNotice, "帯が出る");

        main.DismissLiveWatchedCommand.Execute(null);
        await app.SettleAsync();
        await RescanAsync(app, main, watched);

        Assert.False(main.HasLiveWatchNotice);

        // 何か増えればまた出す
        var more = app.NewFile(@"watched\shoes.zip", [5, 6, 7]);
        app.FolderEvents.Fire(watched, more);
        await UiThread.Until(() => main.HasLiveWatchNotice, "増えたらまた出る");
        Assert.Equal(2, main.WatchedNewCount);
    });

    /// <summary>
    /// 自動で取り込むのは、落としたときと同じ取り込みの道（ユーザ判断「優先順位は普通の取り込みと同じ」）。
    /// ①の問い合わせは、人が始めた取り込みと同じ段（<see cref="BoothPriority.Metadata"/>）で出る。帯は出さない
    /// </summary>
    [Fact]
    public Task 自動で取り込みを許すと_人が始めた取り込みと同じ優先度で取り込む() => TestApp.Run(async app =>
    {
        app.Booth.HasItem(ItemId, "作り物の衣装");
        var watched = await WatchAsync(app, live: true, auto: true);
        var main = await app.StartAsync();
        await BaselineAsync(main);
        IReadOnlyList<string>? started = null;
        app.Services.Imports.Started += work => started ??= work.Accepted;

        var added = PutDownloaded(app, @"watched\costume.zip", ItemId);
        app.FolderEvents.Fire(watched, added);

        await UiThread.Until(() => started is not null, "取り込みが始まる");
        Assert.Equal([added], started!);
        await UiThread.Until(() => app.Services.Imports.Running is null && main.Import.Summary is not null, "取り込みが終わる");
        await app.SettleAsync();

        var json = app.Booth.RequestPriorities.Where(request => request.Url.EndsWith($"/items/{ItemId}.json", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(json);
        Assert.All(json, request => Assert.Equal(BoothPriority.Metadata, request.Priority));
        Assert.False(main.HasLiveWatchNotice);
        Assert.NotNull(await app.Store.Items.LoadAsync(ItemId));
    });

    /// <summary>見張り始める前から置いてあった物は、関係の無いファイルが1つ置かれたのをきっかけに自動で取り込まない。</summary>
    [Fact]
    public Task 自動で取り込むのは_見張り始めた後に置かれた物だけ() => TestApp.Run(async app =>
    {
        var before = app.NewFile(@"watched\before.zip", [9, 9, 9]);
        var watched = await WatchAsync(app, live: true, auto: true);
        var main = await app.StartAsync();
        await BaselineAsync(main);
        IReadOnlyList<string>? started = null;
        app.Services.Imports.Started += work => started ??= work.Accepted;

        var added = app.NewFile(@"watched\costume.zip", [1, 2, 3, 4]);
        app.FolderEvents.Fire(watched, added);

        await UiThread.Until(() => started is not null, "取り込みが始まる");
        Assert.Equal([added], started!);
        Assert.DoesNotContain(before, started!);
        await app.SettleAsync();
    });

    /// <summary>
    /// 取り込み中に見つけた物は、今の取り込みに黙って混ぜず、終わってから取り込む。自動で取り込むときも取り込みは1本ずつ
    /// </summary>
    [Fact]
    public Task 取り込み中に見つけた物は_終わってから取り込む() => TestApp.Run(async app =>
    {
        app.Booth.HasItem(ItemId, "作り物の衣装");
        var watched = await WatchAsync(app, live: true, auto: true);
        var main = await app.StartAsync();
        await BaselineAsync(main);
        var starts = new List<IReadOnlyList<string>>();
        app.Services.Imports.Started += work => starts.Add([.. work.Accepted]);

        // 先に別の取り込みを走らせ、BOOTH の答えを止めておく
        var first = PutDownloaded(app, @"other\first.zip", ItemId);
        app.Booth.Hold();
        try
        {
            main.Import.AddDroppedPaths([first], startImmediately: true, offerWatch: false);
            await UiThread.Until(() => app.Booth.Requests.Count > 0, "先の取り込みが問い合わせる");

            var added = app.NewFile(@"watched\costume.zip", [1, 2, 3, 4]);
            app.FolderEvents.Fire(watched, added);
            await UiThread.Until(() => main.LiveWatch!.IsDeferred, "見つけた物を終わるまで預ける");

            Assert.Single(starts);
            Assert.DoesNotContain(added, app.Services.Imports.Running!.Accepted);
        }
        finally
        {
            app.Booth.Release();
        }

        await UiThread.Until(() => starts.Count == 2, "終わってから次の取り込みが始まる");
        Assert.Equal([Path.Combine(watched, "costume.zip")], starts[1]);
        await UiThread.Until(() => app.Services.Imports.Running is null, "取り込みが終わる");
        await app.SettleAsync();
    });

    /// <summary>
    /// BOOTH の不調で取れなかった物は、取り直すまで新着に数えられる。探し直すたびに自動で始めると、不調の BOOTH へ問い合わせを重ねる。
    /// 同じ物は開いている間に2度は自動で始めない
    /// </summary>
    [Fact]
    public Task 取れなかった物を_探し直すたびに自動で取り込み直さない() => TestApp.Run(async app =>
    {
        app.Booth.IsDown(ItemId);
        var watched = await WatchAsync(app, live: true, auto: true);
        var main = await app.StartAsync();
        await BaselineAsync(main);
        var starts = 0;
        app.Services.Imports.Started += _ => starts++;

        var added = PutDownloaded(app, @"watched\costume.zip", ItemId);
        app.FolderEvents.Fire(watched, added);
        await UiThread.Until(() => starts == 1, "取り込みが始まる");
        await UiThread.Until(() => app.Services.Imports.Running is null && main.Import.Summary is not null, "取り込みが終わる");
        await app.SettleAsync();

        // 関係の無い出来事で探し直す。取れなかった物は新着のままだが、もう自動では始めない
        Assert.Contains(added, (await app.Services.Watch.FindNewAsync([watched])).NewFiles);
        await RescanAsync(app, main, watched);

        Assert.Equal(1, starts);
    });

    [Fact]
    public Task 設定を切ると見張りを止め_入れ直すとまた張る() => TestApp.Run(async app =>
    {
        await WatchAsync(app, live: true);
        var main = await app.StartAsync();
        await BaselineAsync(main);
        var events = Assert.Single(app.FolderEvents.All);

        await app.ChangeSettingsAsync(settings => settings with { WatchFoldersWhileOpen = false });
        await UiThread.Until(() => events.Disposed, "見張りを外す");
        Assert.False(main.LiveWatch?.IsRunning ?? false);

        await app.ChangeSettingsAsync(settings => settings with { WatchFoldersWhileOpen = true });
        await UiThread.Until(() => main.LiveWatch is { IsRunning: true }, "また張る");
        Assert.Equal(2, app.FolderEvents.All.Count);
    });

    [Fact]
    public Task 監視フォルダを足すと_張り直す() => TestApp.Run(async app =>
    {
        await WatchAsync(app, live: true);
        var main = await app.StartAsync();
        await BaselineAsync(main);
        var other = Path.GetDirectoryName(app.NewFile(@"other\keep.txt"))!;

        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [.. settings.WatchedFolders, other] });

        await UiThread.Until(() => main.LiveWatch!.Folders.Count == 2, "足したフォルダも見張る");
        Assert.Equal(3, app.FolderEvents.All.Count);
        Assert.Equal(2, app.FolderEvents.All.Count(events => !events.Disposed));
    });

    [Fact]
    public Task 閉じるときに見張りを止める() => TestApp.Run(async app =>
    {
        await WatchAsync(app, live: true);
        var main = await app.StartAsync();
        await BaselineAsync(main);

        main.StopBackgroundWork();

        Assert.All(app.FolderEvents.All, events => Assert.True(events.Disposed));
        Assert.Null(main.LiveWatch);
    });
}
