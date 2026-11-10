using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 設定の「すべての設定を既定に戻す」（メモ29）。環境に特有の物（取り込み元・監視するフォルダ）は残し、ほかを既定に戻す。
/// 窓の文と、書かれる物（settings.json）と、画面が持つ値の3つを確かめる
/// </summary>
public class SettingsResetAllTests
{
    private static async Task<SettingsViewModel> OpenWithChangedSettingsAsync(TestApp app)
    {
        await app.ChangeSettingsAsync(current => current with
        {
            ImportFolders = [@"D:\作り物\取り込み元"],
            WatchedFolders = [@"D:\作り物\監視"],
            ShowAdult = false,
            NotificationArchiveMegabytes = 500,
            ReturnToSearchWhenEditDone = false,
            RefreshIntervalDays = 30,
            FetchIntervalMs = 9000,
            ImageMaxEdgePixels = 1024,
            StartImportOnLaunch = true,
            Shortcuts = new ShortcutSettings { SaveAndNext = "Ctrl+S" },
        });
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");
        return settings;
    }

    [Fact]
    public Task 押して答えがOKなら_環境の物だけ残して既定に戻し_画面の値も合わせる() => TestApp.Run(async app =>
    {
        var settings = await OpenWithChangedSettingsAsync(app);
        Assert.False(settings.ShowAdult);
        app.AnswerResetSettings = _ => true;

        settings.ResetAllSettingsCommand.Execute(null);
        await UiThread.Until(() => settings.ResetNote.Length > 0, "既定に戻し終わる");
        await app.SettleAsync();

        var saved = app.Services.Settings;
        Assert.Equal([@"D:\作り物\取り込み元"], saved.ImportFolders);
        Assert.Equal([@"D:\作り物\監視"], saved.WatchedFolders);
        Assert.True(saved.ShowAdult);
        Assert.True(saved.ReturnToSearchWhenEditDone);
        Assert.Equal(7, saved.RefreshIntervalDays);
        Assert.Equal(AppSettings.MinFetchIntervalMs, saved.FetchIntervalMs);
        Assert.Equal(new AppSettings().ImageMaxEdgePixels, saved.ImageMaxEdgePixels);
        Assert.False(saved.StartImportOnLaunch);
        Assert.Equal(new ShortcutSettings(), saved.Shortcuts);

        // 画面が持つ値も戻る（戻らないと、次に別の項目を変えたとき、前の値が書き戻される）
        Assert.True(settings.ShowAdult);
        Assert.Equal(7, settings.RefreshIntervalDays);
        Assert.Equal(AppSettings.MinFetchIntervalMs, settings.FetchIntervalMs);
        Assert.Equal("Ctrl+Enter", settings.ShortcutRows.First(row => row.Action == Services.ShortcutAction.SaveAndNext).Gesture);

        // 戻した後に別の項目を変えても、戻した値は崩れない
        settings.ShowSubTagsInList = true;
        await app.SettleAsync();
        Assert.True(app.Services.Settings.ShowAdult);
        Assert.Equal(7, app.Services.Settings.RefreshIntervalDays);
        Assert.Equal("設定を既定に戻しました。", settings.ResetNote);
    });

    [Fact]
    public Task 窓でキャンセルすると_何も変えない() => TestApp.Run(async app =>
    {
        var settings = await OpenWithChangedSettingsAsync(app);
        app.AnswerResetSettings = _ => false;

        settings.ResetAllSettingsCommand.Execute(null);
        await app.SettleAsync();

        Assert.False(app.Services.Settings.ShowAdult);
        Assert.Equal(30, app.Services.Settings.RefreshIntervalDays);
        Assert.False(settings.ShowAdult);
        Assert.Equal(string.Empty, settings.ResetNote);
    });

    [Fact]
    public Task 確かめの窓は_何が戻り何が戻らないかと_元に戻せないことを言う() => TestApp.Run(async app =>
    {
        var settings = await OpenWithChangedSettingsAsync(app);

        settings.ResetAllSettingsCommand.Execute(null);

        // 標準の知らせの窓ではなく専用の窓（チェックを持つ）
        Assert.Empty(app.Notices);
        var dialog = Assert.Single(app.ResetSettingsDialogs);
        Assert.Contains("すべて既定に戻します", ResetSettingsDialogViewModel.Body);
        Assert.Contains("取り込み元・監視するフォルダ", ResetSettingsDialogViewModel.Body);
        Assert.Contains("画面にない細かな調整", ResetSettingsDialogViewModel.Body);
        Assert.Contains("保管している通知も消えません。", ResetSettingsDialogViewModel.Body);
        Assert.EndsWith("\n\nこの操作は元に戻せません。", ResetSettingsDialogViewModel.Body);
        Assert.Equal("通知の保管に使う容量はそのままにする", ResetSettingsDialogViewModel.KeepArchiveCapacityText);
        Assert.True(dialog.KeepArchiveCapacity, "窓の既定はオン");
    });

    [Fact]
    public Task チェックがオンで戻すと_保管の容量は残り_ほかは既定になる() => TestApp.Run(async app =>
    {
        var settings = await OpenWithChangedSettingsAsync(app);
        app.AnswerResetSettings = _ => true; // 窓の既定（オン）のまま「既定に戻す」

        settings.ResetAllSettingsCommand.Execute(null);
        await UiThread.Until(() => settings.ResetNote.Length > 0, "既定に戻し終わる");
        await app.SettleAsync();

        Assert.Equal(500, app.Services.Settings.NotificationArchiveMegabytes);
        Assert.Equal(500, settings.NotificationArchiveMegabytes);
        Assert.True(app.Services.Settings.ShowAdult);
        Assert.Equal(7, app.Services.Settings.RefreshIntervalDays);
    });

    [Fact]
    public Task チェックをオフにして戻すと_保管の容量も50に戻る() => TestApp.Run(async app =>
    {
        var settings = await OpenWithChangedSettingsAsync(app);
        app.AnswerResetSettings = model =>
        {
            model.KeepArchiveCapacity = false;
            return true;
        };

        settings.ResetAllSettingsCommand.Execute(null);
        await UiThread.Until(() => settings.ResetNote.Length > 0, "既定に戻し終わる");
        await app.SettleAsync();

        Assert.Equal(50, app.Services.Settings.NotificationArchiveMegabytes);
        Assert.Equal(50, settings.NotificationArchiveMegabytes);
        Assert.True(app.Services.Settings.ShowAdult);
    });

    [Fact]
    public Task キャンセルなら_チェックがオフでも保管の容量を変えない() => TestApp.Run(async app =>
    {
        var settings = await OpenWithChangedSettingsAsync(app);
        app.AnswerResetSettings = model =>
        {
            model.KeepArchiveCapacity = false;
            return false;
        };

        settings.ResetAllSettingsCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal(500, app.Services.Settings.NotificationArchiveMegabytes);
        Assert.False(app.Services.Settings.ShowAdult);
    });
}
