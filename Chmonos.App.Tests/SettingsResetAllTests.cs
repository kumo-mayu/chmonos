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
        app.Answer = _ => MessageBoxResult.OK;

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
        app.Answer = _ => MessageBoxResult.Cancel;

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
        app.Answer = _ => MessageBoxResult.Cancel;

        settings.ResetAllSettingsCommand.Execute(null);

        var notice = Assert.Single(app.Notices);
        Assert.Equal(MessageBoxButton.OKCancel, notice.Button);
        Assert.Equal(MessageBoxResult.Cancel, notice.DefaultResult);
        Assert.Contains("すべて既定に戻します", notice.Text);
        Assert.Contains("取り込み元・監視するフォルダ", notice.Text);
        Assert.Contains("そのままです。\n\nこの操作は元に戻せません。", notice.Text);
    });
}
