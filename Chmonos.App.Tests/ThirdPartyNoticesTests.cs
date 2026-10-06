using System.Diagnostics;
using System.IO;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 設定の「このアプリについて」から、同梱した第三者のライブラリの許諾文を開ける（外部の点検 2026-10-07）。
/// 外のアプリを起こす所を差し替えて、何が渡されたかを見る（本物のメモ帳は開かない）
/// </summary>
public sealed class ThirdPartyNoticesTests
{
    [Fact]
    public Task ライセンスを開くは_アプリの置き場所の許諾文をメモ帳で開く() => TestApp.Run(async app =>
    {
        var started = new List<ProcessStartInfo>();
        Shell.StartOverride.Value = started.Add;
        try
        {
            var main = await app.StartAsync();
            main.ShowSettingsCommand.Execute(null);
            var settings = Assert.IsType<SettingsViewModel>(main.CurrentViewModel);

            settings.OpenThirdPartyNoticesCommand.Execute(null);

            var start = Assert.Single(started);
            Assert.Equal("notepad.exe", start.FileName);
            Assert.False(start.UseShellExecute);
            Assert.Equal(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt"), Assert.Single(start.ArgumentList));
            Assert.Equal(string.Empty, settings.LicenseNoteText);
        }
        finally
        {
            Shell.StartOverride.Value = null;
        }
    });

    [Fact]
    public void 同梱の文書の名前に場所を混ぜても_アプリの置き場所の外は開かない()
    {
        var started = new List<ProcessStartInfo>();
        Shell.StartOverride.Value = started.Add;
        try
        {
            Assert.False(Shell.OpenBundledText(@"..\..\Windows\win.ini"));
            Assert.Empty(started);
        }
        finally
        {
            Shell.StartOverride.Value = null;
        }
    }
}
