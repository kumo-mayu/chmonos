using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 設定の「通知の保管に使う容量」（ユーザ判断 2026-11-10：MB の整数・既定 50・上限なし・0 なら保管しない・負の数は受けない）と、
/// 今の保管庫の大きさの表示。消すのは閉じるときだけ（`UiCommand.TrimNotificationArchive`）。
/// </summary>
public sealed class NotificationArchiveSettingsTests
{
    [Fact]
    public void 既定は50MBで_すべて既定に戻すと50に戻る()
    {
        Assert.Equal(50, new AppSettings().NotificationArchiveMegabytes);
        Assert.Equal(50, (new AppSettings() with { NotificationArchiveMegabytes = 3 }).ResetToDefaults().NotificationArchiveMegabytes);
        Assert.Equal(3, (new AppSettings() with { NotificationArchiveMegabytes = 3 }).ResetToDefaults(keepNotificationArchiveCapacity: true).NotificationArchiveMegabytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100_000)]
    public Task 欄に入れた0以上の数は_そのまま保存する(int typed) => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);

        settings.NotificationArchiveMegabytes = typed;
        await app.SettleAsync();

        Assert.Equal(typed, settings.NotificationArchiveMegabytes);
        Assert.Equal(typed, app.Services.Settings.NotificationArchiveMegabytes);
        Assert.Equal(string.Empty, settings.Notes[nameof(SettingsViewModel.NotificationArchiveMegabytes)]);
    });

    /// <summary>0 は「閉じるときに全部消す」なので、負の数を 0 に丸めない。前の値のまま、欄の下で言う。</summary>
    [Fact]
    public Task 負の数は受けずに_前の値のまま欄の下で言う() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        settings.NotificationArchiveMegabytes = 80;
        await app.SettleAsync();

        settings.NotificationArchiveMegabytes = -5;
        await app.SettleAsync();

        Assert.Equal(80, settings.NotificationArchiveMegabytes);
        Assert.Equal(80, app.Services.Settings.NotificationArchiveMegabytes);
        Assert.Equal("通知の保管に使う容量には、0 以上の数を入れてください。", settings.Notes[nameof(SettingsViewModel.NotificationArchiveMegabytes)]);

        // 正しい数を入れ直したら、前の知らせは消す
        settings.NotificationArchiveMegabytes = 60;
        Assert.Equal(string.Empty, settings.Notes[nameof(SettingsViewModel.NotificationArchiveMegabytes)]);
    });

    [Fact]
    public Task 保管庫の大きさと月の数を出す() => TestApp.Run(async app =>
    {
        var archive = app.Services.Store.NotificationArchive;
        Directory.CreateDirectory(archive.Directory);
        File.WriteAllBytes(archive.FileFor("2026-10"), new byte[1500]);
        File.WriteAllBytes(archive.FileFor("2026-11"), new byte[1500]);

        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");

        Assert.Equal($"保管中：{DisplayText.Size(3000)} / 2 か月分", settings.NotificationArchiveUsageText);
    });

    [Fact]
    public void 保管庫が無い_数えられないときの文()
    {
        Assert.Equal("…", SettingsViewModel.NotificationArchiveText(false, null));
        Assert.Equal("保管している通知はありません", SettingsViewModel.NotificationArchiveText(true, (0, 0)));
        Assert.Equal("読めませんでした", SettingsViewModel.NotificationArchiveText(true, null));
    }

    /// <summary>容量を小さくしてから戻しても、閉じるまでは消えない。閉じるときは、そのときの容量で見る。</summary>
    [Fact]
    public Task 容量を0にしてから戻すと_閉じても消えない() => TestApp.Run(async app =>
    {
        var archive = app.Services.Store.NotificationArchive;
        Directory.CreateDirectory(archive.Directory);
        File.WriteAllBytes(archive.FileFor("2026-11"), new byte[1500]);

        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        settings.NotificationArchiveMegabytes = 0;
        await app.SettleAsync();
        Assert.Equal(["2026-11"], archive.Months());

        settings.NotificationArchiveMegabytes = 50;
        await app.SettleAsync();
        Assert.IsType<CommandResult.Done>(await app.Services.Commands.ExecuteAsync(new UiCommand.TrimNotificationArchive()));

        Assert.Equal(["2026-11"], archive.Months());
    });

    [Fact]
    public Task 容量を0にしたまま閉じると_保管庫を全部消す() => TestApp.Run(async app =>
    {
        var archive = app.Services.Store.NotificationArchive;
        Directory.CreateDirectory(archive.Directory);
        File.WriteAllBytes(archive.FileFor("2026-10"), new byte[1500]);
        File.WriteAllBytes(archive.FileFor("2026-11"), new byte[1500]);
        await app.ChangeSettingsAsync(current => current with { NotificationArchiveMegabytes = 0 });

        Assert.IsType<CommandResult.Done>(await app.Services.Commands.ExecuteAsync(new UiCommand.TrimNotificationArchive()));

        Assert.Empty(archive.Months());
    });
}
