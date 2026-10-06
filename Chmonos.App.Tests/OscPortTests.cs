using System.Net;
using System.Net.Sockets;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 着替えで VRChat の OSC へ送るポート（ユーザ指示 2026-10-06：設定に送信先のポートの欄。既定は今の 9000。すべて既定に戻すで戻る）。
/// 送り先は手元（127.0.0.1）だけなので、試験は空いているポートで受けて、届いた中身を見る
/// </summary>
public sealed class OscPortTests
{
    [Fact]
    public void 既定は_VRChatが受ける9000()
    {
        Assert.Equal(9000, new AppSettings().OscPort);
        Assert.Equal(VrcOsc.DefaultPort, new AppSettings().OscPort);
    }

    [Fact]
    public void すべて既定に戻すと_ポートも既定に戻る()
        => Assert.Equal(9000, (new AppSettings() with { OscPort = 9123 }).ResetToDefaults().OscPort);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(70000, 65535)]
    [InlineData(9001, 9001)]
    public Task 設定の画面の欄は_ポートとして使える範囲に丸めて保存する(int typed, int saved) => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);

        settings.OscPort = typed;
        await app.SettleAsync();

        Assert.Equal(saved, settings.OscPort);
        Assert.Equal(saved, app.Services.Settings.OscPort);
        Assert.Equal(typed == saved ? string.Empty : "OSC の送信先のポートに入れられるのは 1〜65535です。" + $"{saved}にしました。", settings.Notes[nameof(SettingsViewModel.OscPort)]);
    });

    [Fact]
    public Task 着替えは_設定のポートへ送る() => TestApp.Run(async app =>
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;
        await app.ChangeSettingsAsync(settings => settings with { OscPort = port });
        await app.AddItemAsync(Make.Item("9900001", "作り物のアバター"));
        var main = await app.StartAsync();
        var created = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("9900001", "作り物の改変")));
        var modification = new ModificationViewModel(created.Record, app.Services, main, main.Thumbnails)
        {
            BlueprintInput = "avtr_00000000-0000-0000-0000-000000000001",
        };

        var receiving = receiver.ReceiveAsync();
        modification.ChangeAvatarCommand.Execute(null);
        var got = await receiving.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(VrcOsc.StringMessage(VrcOsc.AvatarChangeAddress, "avtr_00000000-0000-0000-0000-000000000001"), got.Buffer);
    });
}
