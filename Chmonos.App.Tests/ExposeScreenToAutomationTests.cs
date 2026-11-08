using System.Windows.Automation.Peers;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 設定「読み上げソフトや自動操作に画面の内容を伝える」（既定はオン・ユーザ判断 2026-11-08）。
/// 切ると主の窓が中身を伝えない。効くのは起動し直してからなので、切り替えた後は再起動の案内を出す。
/// </summary>
public class ExposeScreenToAutomationTests
{
    [Fact]
    public void 既定はオン() => Assert.True(new AppSettings().ExposeScreenToAutomation);

    [Fact]
    public Task 切り替えると保存し_再起動するまで案内を出す_戻すと消える() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");
        Assert.True(settings.ExposeScreenToAutomation);
        Assert.Equal(string.Empty, settings.AutomationRestartNote);

        settings.ExposeScreenToAutomation = false;
        await app.SettleAsync();

        Assert.False(app.Services.Settings.ExposeScreenToAutomation);
        Assert.Contains("再起動", settings.AutomationRestartNote);
        Assert.Contains("読み上げソフトで画面を読めません", settings.AutomationRestartNote);

        settings.ExposeScreenToAutomation = true;
        await app.SettleAsync();

        Assert.True(app.Services.Settings.ExposeScreenToAutomation);
        Assert.Equal(string.Empty, settings.AutomationRestartNote);
    });

    [Fact]
    public Task 切った窓は_中身の部品を伝えず_オンの窓は伝える() => UiThread.Run(() =>
    {
        try
        {
            MainWindow.ExposesContent = false;
            var quiet = new MainWindow();
            Assert.NotEqual(typeof(WindowAutomationPeer), UIElementAutomationPeer.CreatePeerForElement(quiet).GetType());
            Assert.Empty(UIElementAutomationPeer.CreatePeerForElement(quiet).GetChildren() ?? []);
            quiet.Close();

            MainWindow.ExposesContent = true;
            // 出していない窓は中身の部品がまだ組まれていないので、子の数では見分けられない。ふつうの窓の窓口になることを見る
            var shown = new MainWindow();
            Assert.Equal(typeof(WindowAutomationPeer), UIElementAutomationPeer.CreatePeerForElement(shown).GetType());
            shown.Close();
        }
        finally
        {
            MainWindow.ExposesContent = true;
        }
    });
}
