using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Xml.Linq;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>設定の入力欄の右クリック「既定に戻す（既定：…）」（open.md 170）。</summary>
public class SettingDefaultTests
{
    private static readonly AppSettings Defaults = new();

    [Fact]
    public void 画面で右クリックを付けた欄と_既定の表が一対一で合う()
    {
        var keys = XDocument.Load(Path.Combine(ViewsFolder(), "SettingsView.xaml"))
            .Descendants()
            .Select(e => (string?)e.Attributes().FirstOrDefault(a => a.Name.LocalName == "SettingDefault.Key"))
            .OfType<string>()
            .ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Equal(
            SettingsViewModel.DefaultEntries.Keys.Order(StringComparer.Ordinal),
            keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public Task 既定に戻すのは_その1項目だけで_画面を開いた後にほかで書かれた値を巻き戻さない() => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(s => s with { RefreshIntervalDays = Defaults.RefreshIntervalDays + 20, ShowAdult = true });
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");
        Assert.True(settings.ShowAdult);

        // 画面を開いた後に、ほかの所（別の画面・API）が書いた
        await app.ChangeSettingsAsync(s => s with { ShowAdult = false });

        settings.ResetToDefault(nameof(SettingsViewModel.RefreshIntervalDays));
        await UiThread.Until(() => app.Services.Settings.RefreshIntervalDays == Defaults.RefreshIntervalDays, "既定が書かれる");
        await app.SettleAsync();

        Assert.Equal(Defaults.RefreshIntervalDays, settings.RefreshIntervalDays);
        Assert.False(app.Services.Settings.ShowAdult);
    });

    [Fact]
    public Task 今が既定なら押せず_既定でなければ既定の値を括弧に添える() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");
        var key = nameof(SettingsViewModel.RefreshIntervalDays);

        Assert.True(settings.IsAtDefault(key));
        Assert.False(ResetItem(SettingDefault.Build(new TextBox(), settings, key)).IsEnabled);

        settings.RefreshIntervalDays = Defaults.RefreshIntervalDays + 1;
        var item = ResetItem(SettingDefault.Build(new TextBox(), settings, key));
        Assert.True(item.IsEnabled);
        Assert.Equal($"既定に戻す（既定：{Defaults.RefreshIntervalDays} 日）", item.Header);

        Assert.Equal($"既定に戻す（既定：{(Defaults.ShowAdult ? "オン" : "オフ")}）", settings.DefaultMenuHeader(nameof(SettingsViewModel.ShowAdult)));
    });

    [Fact]
    public Task 数の欄は切り取り_コピー_貼り付けの下に置き_チェックには既定に戻すだけを出す() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");

        var text = SettingDefault.Build(new TextBox(), settings, nameof(SettingsViewModel.RefreshIntervalDays)).Items.Cast<object>().ToList();
        Assert.Equal(["切り取り", "コピー", "貼り付け"], text.Take(3).Cast<MenuItem>().Select(i => (string)i.Header));
        Assert.IsType<Separator>(text[3]);
        Assert.StartsWith("既定に戻す", (string)Assert.IsType<MenuItem>(text[4]).Header);
        Assert.Equal(5, text.Count);

        var check = SettingDefault.Build(new CheckBox(), settings, nameof(SettingsViewModel.ShowAdult)).Items.Cast<object>().ToList();
        Assert.StartsWith("既定に戻す", (string)((MenuItem)Assert.Single(check)).Header);
    });

    [Fact]
    public Task 範囲の外で丸めた知らせは_既定に戻すと消える() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");
        var key = nameof(SettingsViewModel.RefreshIntervalDays);

        settings.RefreshIntervalDays = 5000;
        Assert.NotEmpty(settings.Notes[key]);

        settings.ResetToDefault(key);
        await UiThread.Until(() => app.Services.Settings.RefreshIntervalDays == Defaults.RefreshIntervalDays, "既定が書かれる");
        await app.SettleAsync();

        Assert.Empty(settings.Notes[key]);
    });

    [Fact]
    public Task Unityの知らせ方を戻すと_前の版の読み替えに落ちず_既定の値がはっきり書かれる() => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(s => s with { QuietUnitySend = true });
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");
        Assert.True(settings.UnitySendResultToast);

        settings.ResetToDefault(nameof(SettingsViewModel.UnitySendResultToast));
        await UiThread.Until(() => app.Services.Settings.UnitySendResultToast is not null, "知らせ方が書かれる");
        await app.SettleAsync();

        Assert.False(app.Services.Settings.UnitySendResultByToast);
        Assert.True(settings.UnitySendResultWindow);
        // 戻したのは知らせ方だけ。確認は前の版の読み替え（切れている）のまま
        Assert.False(app.Services.Settings.ConfirmsUnitySend);
    });

    private static MenuItem ResetItem(ContextMenu menu) => menu.Items.OfType<MenuItem>().Last();

    private static string ViewsFolder([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here))!, "Chmonos.App", "Views");
}
