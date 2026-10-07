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
}
