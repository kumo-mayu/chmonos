using System.IO;
using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 新しい版の知らせの帯と、設定の「データ」の控えの行（ユーザ判断 2026-10-08・`docs/spec/data-format.md`）。
/// 通信はしない（<see cref="TestApp.LatestVersion"/> が GitHub の答えの代わり）。
/// </summary>
public class UpdateAndFormatBackupTests
{
    // ---- 新しい版の知らせ ----

    [Fact]
    public Task 新しい版があれば帯を出し_ページを開ける() => TestApp.Run(async app =>
    {
        app.LatestVersion = "v99.0.0";
        var main = await app.StartAsync();

        await main.CheckForUpdatesAsync();
        await UiThread.Until(() => main.HasUpdateNotice, "帯が出る");

        Assert.Equal("新しいバージョン（v99.0.0）があります。", main.UpdateNoticeText);
        main.OpenUpdatePageCommand.Execute(null);
        Assert.Equal([UpdateCheck.DownloadPage], app.OpenedUrls);
    });

    [Fact]
    public Task 同じか古い版なら帯を出さない() => TestApp.Run(async app =>
    {
        app.LatestVersion = "v0.1.0";
        var main = await app.StartAsync();

        await main.CheckForUpdatesAsync();

        Assert.False(main.HasUpdateNotice);
        Assert.NotNull(app.Services.Store.UpdateCheck.Load().CheckedAt);
    });

    /// <summary>× で閉じた版は、次に確かめても出さない。次の版が出たらまた出す</summary>
    [Fact]
    public Task 閉じた版は出さず_次の版でまた出す() => TestApp.Run(async app =>
    {
        app.LatestVersion = "v99.0.0";
        var main = await app.StartAsync();
        await main.CheckForUpdatesAsync();
        await UiThread.Until(() => main.HasUpdateNotice, "帯が出る");

        main.DismissUpdateCommand.Execute(null);
        await UiThread.Until(() => app.Services.Store.UpdateCheck.Load().DismissedVersion == "99.0.0", "閉じた版を覚える");
        Assert.False(main.HasUpdateNotice);

        await main.CheckForUpdatesAsync();
        Assert.False(main.HasUpdateNotice);

        // 1日たって次の版が出た
        await app.Services.Store.UpdateCheck.UpdateAsync(record => record with { CheckedAt = DateTimeOffset.Now.AddDays(-2) });
        app.LatestVersion = "v99.1.0";
        await main.CheckForUpdatesAsync();
        await UiThread.Until(() => main.HasUpdateNotice, "次の版の帯が出る");
        Assert.Equal("新しいバージョン（v99.1.0）があります。", main.UpdateNoticeText);
    });

    /// <summary>1日たっていなければ聞きに行かず、前に聞けた版で決める</summary>
    [Fact]
    public Task 前に確かめてから1日たっていなければ聞かない() => TestApp.Run(async app =>
    {
        await app.Services.Store.UpdateCheck.SaveAsync(new UpdateCheckRecord { CheckedAt = DateTimeOffset.Now, LatestVersion = "v1.0.0" });
        app.LatestVersion = "v99.0.0";
        var main = await app.StartAsync();

        await main.CheckForUpdatesAsync();

        Assert.False(main.HasUpdateNotice);
        Assert.Equal("v1.0.0", app.Services.Store.UpdateCheck.Load().LatestVersion);
    });

    [Fact]
    public Task 設定で切っていれば確かめない() => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(current => current with { CheckForUpdates = false });
        app.LatestVersion = "v99.0.0";
        var main = await app.StartAsync();

        await main.CheckForUpdatesAsync();

        Assert.False(main.HasUpdateNotice);
        Assert.Null(app.Services.Store.UpdateCheck.Load().CheckedAt);
    });

    // ---- 控え ----

    [Fact]
    public Task 控えが無ければ無いと出し_削除は押せない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");

        Assert.Equal("控えはありません", settings.FormatBackupUsageText);
        Assert.False(settings.DeleteFormatBackupsCommand.CanExecute(null));
    });

    [Fact]
    public Task 控えの容量とファイル数を出し_確かめてから消す() => TestApp.Run(async app =>
    {
        var root = app.Services.Paths.Root;
        StoreFormat.Backup(root, 1, DateTimeOffset.Now);
        var (files, bytes) = StoreFormat.BackupUsage(root);
        Assert.True(files > 0);

        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");

        Assert.Equal(SettingsViewModel.UsageText(true, bytes, files), settings.FormatBackupUsageText);
        Assert.True(settings.DeleteFormatBackupsCommand.CanExecute(null));

        // 窓で「やめる」と消さない（既定の答え）
        settings.DeleteFormatBackupsCommand.Execute(null);
        Assert.Contains("元に戻せません", app.Notices.Last().Text);
        Assert.True(Directory.Exists(Path.Combine(root, StoreFormat.BackupsDirName)));

        app.Answer = _ => MessageBoxResult.OK;
        settings.DeleteFormatBackupsCommand.Execute(null);
        await UiThread.Until(() => settings.FormatBackupUsageText == "控えはありません", "消える");
        Assert.False(Directory.Exists(Path.Combine(root, StoreFormat.BackupsDirName)));
    });
}
