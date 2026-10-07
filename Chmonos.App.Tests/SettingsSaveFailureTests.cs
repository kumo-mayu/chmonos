using System.IO;
using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 設定を保存できなかったら、窓で知らせる（ユーザ判断 2026-10-07。USB メモリを抜く実機の確かめで）。
/// 前は設定の画面の下の帯に出すだけで、見落とすと、変えたつもりの設定が保存されていないことに気付かなかった
/// </summary>
public sealed class SettingsSaveFailureTests
{
    [Fact]
    public Task 設定を保存できなかったら_窓で知らせ_変える前のままだと言う() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        main.ShowSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
        await app.SettleAsync();

        // 設定のファイルを読むだけにして、保存を失敗させる（失敗はログにも残るのが正しいので、ログの失敗は許す）
        app.AllowLoggedFailures = true;
        var file = app.Services.Paths.SettingsFile;
        if (!File.Exists(file))
        {
            await app.ChangeSettingsAsync(current => current);
        }

        File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
        try
        {
            var before = settings.ShowAdult;
            settings.ShowAdult = !before;
            await app.SettleAsync();

            var notice = Assert.Single(app.Notices, request => request.Caption == "設定を保存できませんでした");
            Assert.StartsWith("設定を保存できなかったので、変える前のままです。", notice.Text);
            Assert.EndsWith("もう一度変えると保存し直します。", notice.Text);
            Assert.Equal(MessageBoxImage.Warning, notice.Icon);
            Assert.Equal(string.Empty, settings.Status);
            Assert.Equal(before, settings.ShowAdult);
            Assert.Equal(before, app.Services.Settings.ShowAdult);
        }
        finally
        {
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        }
    });

    /// <summary>設定のファイルを読むだけにして、保存を失敗させる。終わったら戻す。</summary>
    private static async Task WithReadOnlySettings(TestApp app, Func<Task> body)
    {
        app.AllowLoggedFailures = true;
        var file = app.Services.Paths.SettingsFile;
        if (!File.Exists(file))
        {
            await app.ChangeSettingsAsync(current => current);
        }

        File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
        try
        {
            await body();
        }
        finally
        {
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        }
    }

    /// <summary>
    /// ショートカットの保存に失敗したら、行も保存してある割り当てへ戻す（外部の点検 2026-10-07）。
    /// 戻さないと、次に別の設定を保存したとき、失敗した割り当てまで一緒に書かれる（保存のたびに行から組み直すため）
    /// </summary>
    [Fact]
    public Task ショートカットの保存に失敗したら_行も保存してある割り当てへ戻る() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        main.ShowSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
        await app.SettleAsync();
        var row = settings.ShortcutRows[0];
        var before = row.Gesture;

        await WithReadOnlySettings(app, async () =>
        {
            settings.AssignShortcut(row, "Ctrl+Shift+F12");
            await app.SettleAsync();
        });

        Assert.Equal(before, row.Gesture);
        Assert.DoesNotContain(settings.ShortcutRows, entry => entry.Gesture == "Ctrl+Shift+F12");

        // 保存できるようになってから別の設定を変えても、失敗した割り当ては書かれない
        settings.ShowAdult = !settings.ShowAdult;
        await app.SettleAsync();
        Assert.DoesNotContain("Ctrl+Shift+F12", File.ReadAllText(app.Services.Paths.SettingsFile));
    });

    /// <summary>「すべての設定を既定に戻す」の保存に失敗したら、既定に戻したとは言わない（外部の点検 2026-10-07）。</summary>
    [Fact]
    public Task 既定に戻すの保存に失敗したら_戻したとは言わない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        main.ShowSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
        await app.SettleAsync();

        await WithReadOnlySettings(app, async () =>
        {
            await settings.ResetAllSettingsAsync();
            await app.SettleAsync();
        });

        Assert.Contains(app.Notices, request => request.Caption == "設定を保存できませんでした");
        Assert.NotEqual("設定を既定に戻しました。", settings.ResetNote);
    });

    /// <summary>
    /// 取り込み元を外す保存に失敗したら、行は戻り、「外しました」は出さない（外部の点検 2026-10-07）。
    /// 前は行が消えたまま、保存されていない外しの知らせと「取り込み元に戻す」が出ていた
    /// </summary>
    [Fact]
    public Task 取り込み元を外す保存に失敗したら_行が戻り_外したとは言わない() => TestApp.Run(async app =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "chmonos-import-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            await app.ChangeSettingsAsync(current => Core.Services.FolderListChange.AddImportFolders(current, [folder]));
            var main = await app.StartAsync();
            main.ShowSettingsCommand.Execute(null);
            var settings = Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
            await app.SettleAsync();
            var row = Assert.Single(settings.Folders, entry => entry.Path == folder);

            await WithReadOnlySettings(app, async () =>
            {
                row.RemoveCommand!.Execute(null);
                await app.SettleAsync();
            });

            Assert.Contains(settings.Folders, entry => entry.Path == folder);
            Assert.False(main.HasFolderRemovedNotice);
            Assert.Contains(folder, app.Services.Settings.ImportFolders);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });
}
