using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 設定の「API」の節（外から使う API の設定。docs/spec/local-api.md・2026-10-12）。書くのは api-settings.json だけで、api.json は読むだけ。
/// </summary>
public sealed class SettingsApiTests
{
    private static ApiSettings Saved(TestApp app) => JsonStore.Read<ApiSettings>(app.Services.Paths.ApiSettingsFile) ?? new ApiSettings();

    [Fact]
    public Task 節は既定で畳み_開けば開いたまま() => TestApp.Run(async app =>
    {
        SettingsViewModel.ResetSectionsForTest();
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);

        // 開発者向けなので、ほかの節（既定で開く）と違って畳んでおく
        Assert.False(settings.SectionApiOpen);
        Assert.True(settings.SectionDataOpen);

        settings.SectionApiOpen = true;
        Assert.True(new SettingsViewModel(app.Services, main).SectionApiOpen);
        SettingsViewModel.ResetSectionsForTest();
    });

    [Theory]
    [InlineData(80, 1024)]
    [InlineData(70000, 65535)]
    [InlineData(40000, 40000)]
    public Task ポートは_使える範囲に丸めてAPIの設定へ保存する(int typed, int saved) => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);

        settings.ApiPort = typed;
        await app.SettleAsync();

        Assert.Equal(saved, settings.ApiPort);
        Assert.Equal(saved, Saved(app).Port);
        Assert.Equal(typed == saved ? string.Empty : $"APIのポートに入れられるのは 1024〜65535です。{saved}にしました。", settings.Notes[nameof(SettingsViewModel.ApiPort)]);
    });

    [Fact]
    public Task 許可するWebページは_ブラウザが送る形にそろえて追加し_削除できる() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);

        settings.NewApiOrigin = " http://LocalHost:5173/ ";
        settings.AddApiOriginCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal(["http://localhost:5173"], settings.ApiOrigins.Select(row => row.Origin));
        Assert.Equal(["http://localhost:5173"], Saved(app).AllowedOrigins);
        Assert.Equal(string.Empty, settings.NewApiOrigin);

        settings.NewApiOrigin = "http://localhost:5173";
        settings.AddApiOriginCommand.Execute(null);
        Assert.Equal("既にあります。", settings.ApiOriginNotice);

        // 打ち直すと、前の知らせは消える
        settings.NewApiOrigin = "http://localhost:5173/page?x=1";
        Assert.Equal(string.Empty, settings.ApiOriginNotice);
        settings.AddApiOriginCommand.Execute(null);
        Assert.Equal("http://localhost:5173 の形で入力してください。", settings.ApiOriginNotice);

        settings.ApiOrigins.Single().RemoveCommand.Execute(null);
        await app.SettleAsync();
        Assert.Empty(settings.ApiOrigins);
        Assert.Empty(Saved(app).AllowedOrigins);
    });

    [Theory]
    [InlineData("http://localhost:5173", "http://localhost:5173")]
    [InlineData("https://example.test", "https://example.test")]
    [InlineData("http://127.0.0.1:8080/", "http://127.0.0.1:8080")]
    [InlineData("localhost:5173", null)]
    [InlineData("ftp://localhost", null)]
    [InlineData("http://localhost:5173/app", null)]
    [InlineData("http://user@localhost:5173", null)]
    [InlineData("", null)]
    public void 住所の形を読む(string text, string? expected) => Assert.Equal(expected, SettingsViewModel.NormalizeOrigin(text));

    [Fact]
    public Task 手で書いた設定と_画面が触っていない項目を消さない() => TestApp.Run(async app =>
    {
        // 手で書いたファイル（許可するページが null の、欠けた配列）
        await File.WriteAllTextAsync(app.Services.Paths.ApiSettingsFile, """{ "port": 41000, "allowedOrigins": null }""");
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        Assert.Equal(41000, settings.ApiPort);
        Assert.Empty(settings.ApiOrigins);

        settings.NewApiOrigin = "http://localhost:3000";
        settings.AddApiOriginCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal(41000, Saved(app).Port);
        Assert.Equal(["http://localhost:3000"], Saved(app).AllowedOrigins);
    });

    [Fact]
    public Task APIが動いていなければ止まっていると言い_実行ファイルが無ければ起動を押せない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();

        // 落ちて残った api.json（プロセスの番号が、API ではないこのプロセス）
        JsonStore.Write(app.Services.Paths.ApiFile, new ApiEndpointRecord { Port = 39847, Token = "token", ProcessId = Environment.ProcessId });
        var settings = new SettingsViewModel(app.Services, main);

        Assert.Equal("止まっています。", settings.ApiStatusText);
        Assert.Equal(string.Empty, settings.ApiRestartNote);
        Assert.Equal(File.Exists(SettingsViewModel.ApiExecutablePath), settings.CanStartApi);
        if (!settings.CanStartApi)
        {
            Assert.Equal("Chmonos.Api.exeが見つかりません。", settings.StartApiTip);
        }
    });

    [Fact]
    public Task 動いているかは錠で見て_止まれば画面を開き直さなくても戻る() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        JsonStore.Write(app.Services.Paths.ApiFile, new ApiEndpointRecord { Port = 39847, Token = "token", ProcessId = Environment.ProcessId });

        // 引越しで運ばれた api.json：番号は動いているプロセスでも、この保存先の錠が無ければ動いていない（点検35）
        Assert.Equal("止まっています。", new SettingsViewModel(app.Services, main).ApiStatusText);

        var interval = SettingsViewModel.ApiWatchInterval;
        SettingsViewModel.ApiWatchInterval = TimeSpan.FromMilliseconds(20);
        var apiLock = SingleInstanceLock.TryAcquire(app.Services.Paths.Root, app.Services.Paths.ApiLockFile);
        var settings = new SettingsViewModel(app.Services, main);
        try
        {
            Assert.Equal("ポート 39847 で動いています。", settings.ApiStatusText);

            // 前は起動を確かめた所で見るのをやめ、止めても「動いています」のままだった（点検35）
            apiLock!.Dispose();
            for (var attempt = 0; attempt < 100 && settings.ApiStatusText != "止まっています。"; attempt++)
            {
                await Task.Delay(20);
            }

            Assert.Equal("止まっています。", settings.ApiStatusText);
        }
        finally
        {
            apiLock?.Dispose();
            settings.OnLeaving();
            SettingsViewModel.ApiWatchInterval = interval;
        }
    });
}
