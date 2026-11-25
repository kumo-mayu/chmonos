using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 設定「Unityへ送る前に確認する」「Unityへ送った結果を知らせる」（親）と「知らせ方：ウィンドウ｜Windowsの通知」（子）
/// （ユーザ判断 2026-11-03。2026-11-19 に1つのチェックを3つに分けた・open.md 167）。
/// 送る道（商品ページ・カードの右クリック・まとめて送る・改変）はどれも <see cref="UnitySendNotice"/> を通るので、ここで出し分けを確かめる。
/// 送る道そのものは Unity の窓が要るので、この一式では通さない
/// </summary>
public class UnitySendNoticeTests
{
    [Fact]
    public Task 既定では_送る前に確認の窓を出し_やめれば送らない() => TestApp.Run(async app =>
    {
        await app.StartAsync();

        var go = UnitySendNotice.Confirm(app.Services, "「作り物.unitypackage」を、Unityの「作り物」に送ります。", "Unityへ送る");

        Assert.False(go);
        var notice = Assert.Single(app.Notices);
        Assert.Equal(MessageBoxButton.OKCancel, notice.Button);
        Assert.Equal(MessageBoxResult.OK, notice.DefaultResult);
    });

    [Fact]
    public Task 確認を切れば_確認の窓を出さずに送る() => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(settings => settings with { ConfirmUnitySend = false });
        await app.StartAsync();

        Assert.True(UnitySendNotice.Confirm(app.Services, "送ります。", "Unityへ送る"));
        Assert.Empty(app.Notices);
    });

    /// <summary>親（知らせるか）・子（通知か）の組み合わせごとに、結果がどこに出るか。確認の設定は結果に関わらない。</summary>
    [Theory]
    [InlineData(true, false, true, "window")]
    [InlineData(true, true, true, "toast")]
    [InlineData(true, true, false, "toast")]
    [InlineData(false, false, true, "none")]
    [InlineData(false, true, true, "none")]
    [InlineData(true, false, false, "window")]
    public Task 結果は_親と知らせ方の組み合わせで出る所が決まる(bool notify, bool toast, bool confirm, string expected) => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(settings => settings with
        {
            NotifyUnitySendResult = notify,
            UnitySendResultToast = toast,
            ConfirmUnitySend = confirm,
        });
        await app.StartAsync();

        UnitySendNotice.Result(app.Services, "2 件をUnityへ順に送りました。", "Unityへ順に送る");

        var actual = (app.Notices.Count, app.Toasts.Count) switch
        {
            (1, 0) => "window",
            (0, 1) => "toast",
            (0, 0) => "none",
            var other => $"窓 {other.Item1}・通知 {other.Item2}",
        };
        Assert.Equal(expected, actual);
        if (expected == "toast")
        {
            var shown = Assert.Single(app.Toasts);
            Assert.Equal("Unityへ順に送る", shown.Title);
            Assert.Equal("2 件をUnityへ順に送りました。", shown.Text);
        }
        else if (expected == "window")
        {
            Assert.Equal(MessageBoxImage.Information, Assert.Single(app.Notices).Icon);
        }
    });

    /// <summary>
    /// 前の版の settings.json（3つの欄が無く、古い欄だけ）を読み替える。入れていた人は「確認しない」「Windowsの通知」、
    /// 入れていなかった人は「確認する」「ウィンドウ」。どちらも結果は知らせる（使う人の今の動きを変えない）。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 前の版の設定は_古い欄から読み替える(bool quiet)
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            $$"""{ "quietUnitySend": {{(quiet ? "true" : "false")}} }""",
            Core.Storage.JsonStore.Options)!;

        Assert.True(settings.NotifiesUnitySendResult);
        Assert.Equal(quiet, settings.UnitySendResultByToast);
        Assert.Equal(!quiet, settings.ConfirmsUnitySend);
    }

    [Fact]
    public Task 前の版で静かにしていた人は_設定の画面でも確認が切れ_通知が選ばれている() => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(settings => settings with { QuietUnitySend = true });
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");

        Assert.False(settings.ConfirmUnitySend);
        Assert.True(settings.NotifyUnitySendResult);
        Assert.True(settings.UnitySendResultToast);
        Assert.False(settings.UnitySendResultWindow);

        // ほかの欄を変えて保存しても、読み替えた値のまま書く（前の動きを変えない）
        settings.ReturnToSearchWhenEditDone = !settings.ReturnToSearchWhenEditDone;
        await UiThread.Until(() => app.Services.Settings.UnitySendResultToast is not null, "設定が書かれる");
        await app.SettleAsync();

        Assert.False(app.Services.Settings.ConfirmsUnitySend);
        Assert.True(app.Services.Settings.UnitySendResultByToast);
        Assert.True(app.Services.Settings.NotifiesUnitySendResult);
    });

    [Fact]
    public Task 設定画面で切り替えると_その欄が保存され_親を切っても知らせ方は残る() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");
        Assert.True(settings.ConfirmUnitySend);
        Assert.True(settings.NotifyUnitySendResult);
        Assert.True(settings.UnitySendResultWindow);

        settings.UnitySendResultToast = true;
        await UiThread.Until(() => app.Services.Settings.UnitySendResultByToast, "知らせ方が書かれる");
        settings.NotifyUnitySendResult = false;
        await UiThread.Until(() => !app.Services.Settings.NotifiesUnitySendResult, "親が書かれる");
        settings.ConfirmUnitySend = false;
        await UiThread.Until(() => !app.Services.Settings.ConfirmsUnitySend, "確認が書かれる");
        await app.SettleAsync();

        Assert.True(app.Services.Settings.UnitySendResultByToast);
        Assert.False(settings.UnitySendResultWindow);
    });

    [Fact]
    public Task 入ったフォルダを選択する設定は_既定で切で_入れると保存される() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");
        Assert.False(settings.SelectAfterUnitySend);
        Assert.False(app.Services.Settings.SelectAfterUnitySend);

        settings.SelectAfterUnitySend = true;
        await UiThread.Until(() => app.Services.Settings.SelectAfterUnitySend, "選択する設定が書かれる");
        await app.SettleAsync();

        // ほかの Unity の欄は変わらない
        Assert.True(app.Services.Settings.ConfirmsUnitySend);
        Assert.True(app.Services.Settings.NotifiesUnitySendResult);
    });
}
