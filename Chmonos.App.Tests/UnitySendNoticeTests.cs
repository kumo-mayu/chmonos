using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 設定「Unityへ送るとき、確認を表示せずに送り、結果をWindowsの通知で知らせる」（ユーザ判断 2026-11-03）。
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
    public Task 設定がオンなら_確認の窓を出さずに送る() => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(settings => settings with { QuietUnitySend = true });
        await app.StartAsync();

        Assert.True(UnitySendNotice.Confirm(app.Services, "送ります。", "Unityへ送る"));
        Assert.Empty(app.Notices);
    });

    [Fact]
    public Task 既定では_結果を窓で言い_通知は出さない() => TestApp.Run(async app =>
    {
        await app.StartAsync();

        UnitySendNotice.Result(app.Services, "2 件をUnityへ順に送りました。", "Unityへ順に送る");

        var notice = Assert.Single(app.Notices);
        Assert.Equal("2 件をUnityへ順に送りました。", notice.Text);
        Assert.Equal(MessageBoxImage.Information, notice.Icon);
        Assert.Empty(app.Toasts);
    });

    [Fact]
    public Task 設定がオンなら_結果は通知で言い_窓を出さない() => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(settings => settings with { QuietUnitySend = true });
        await app.StartAsync();

        UnitySendNotice.Result(app.Services, "2 件をUnityへ順に送りました。", "Unityへ順に送る");

        var toast = Assert.Single(app.Toasts);
        Assert.Equal("Unityへ順に送る", toast.Title);
        Assert.Equal("2 件をUnityへ順に送りました。", toast.Text);
        Assert.Empty(app.Notices);
    });

    [Fact]
    public Task 設定画面で切り替えると_保存される() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");
        Assert.False(settings.QuietUnitySend);

        settings.QuietUnitySend = true;
        await UiThread.Until(() => app.Services.Settings.QuietUnitySend, "設定が書かれる");
        await app.SettleAsync();

        Assert.True(app.Services.Settings.QuietUnitySend);
    });
}
