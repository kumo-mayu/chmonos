using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>設定「上に出る見出しの大きさ」（ユーザ判断 2026-11-20）。保存・高さへの反映・既定に戻す。</summary>
public sealed class CompactHeaderSizeSettingTests
{
    [Fact]
    public Task 変えると保存され_見出しの高さに効き_既定に戻すと52に戻る() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");
        try
        {
            Assert.Equal(52, CompactHeader.Height);

            settings.CompactHeaderPercent = 150;
            await UiThread.Until(() => app.Services.Settings.CompactHeaderPercent == 150, "保存される");
            Assert.Equal(78, CompactHeader.Height);

            settings.ResetToDefault(nameof(SettingsViewModel.CompactHeaderPercent));
            await UiThread.Until(() => app.Services.Settings.CompactHeaderPercent == 100, "既定が書かれる");
            Assert.Equal(52, CompactHeader.Height);
        }
        finally
        {
            CompactHeader.Apply(100);
        }
    });
}
