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
    public Task 許可するオリジンは_ブラウザが送る形にそろえて追加し_削除できる() => TestApp.Run(async app =>
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
    public void オリジンの形を読む(string text, string? expected) => Assert.Equal(expected, SettingsViewModel.NormalizeOrigin(text));

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
    public Task APIは既定でオフで_止まっていると言う() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);

        Assert.False(settings.ApiEnabled);
        Assert.Equal("止まっています。", settings.ApiStatusText);
    });

    /// <summary>空いているポート（試験どうしでぶつからないよう、OS に選ばせる）。</summary>
    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// API はアプリと同じプロセスで動く（docs/history/headless-core-2026-10-13.md 段2）。オンにしたらすぐ待ち受け、
    /// 外の道具が読む api.json にこのプロセスの番号とポートを書く。オフにしたらすぐ止め、api.json を「止まった」にする（ユーザ判断 2026-10-13）
    /// </summary>
    [Fact]
    public Task オンにするとすぐ待ち受けて答え_オフにするとすぐ止まる() => TestApp.Run(async app =>
    {
        var port = FreePort();
        JsonStore.Write(app.Services.Paths.ApiSettingsFile, new ApiSettings { Port = port });
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        try
        {
            settings.ApiEnabled = true;
            await UiThread.Until(() => app.Services.Api.State == Services.ApiRunState.Running, "API が待ち受ける");

            Assert.Equal($"ポート {port} で動いています。", settings.ApiStatusText);
            var endpoint = JsonStore.Read<ApiEndpointRecord>(app.Services.Paths.ApiFile)!;
            Assert.Equal((port, (int?)Environment.ProcessId), (endpoint.Port, endpoint.ProcessId));
            using (var http = new System.Net.Http.HttpClient())
            {
                var health = await http.GetAsync($"http://127.0.0.1:{port}/v1/health");
                Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
            }

            settings.ApiEnabled = false;
            await UiThread.Until(() => app.Services.Api.State == Services.ApiRunState.Stopped, "API が止まる");

            Assert.Equal("止まっています。", settings.ApiStatusText);
            Assert.Null(JsonStore.Read<ApiEndpointRecord>(app.Services.Paths.ApiFile)!.ProcessId);
            Assert.False(Saved(app).Enabled);
        }
        finally
        {
            await app.Services.Api.StopAsync();
            settings.OnLeaving();
        }
    });

    [Fact]
    public Task ポートがほかに使われていれば_待ち受けられないと言い_変えれば待ち受ける() => TestApp.Run(async app =>
    {
        // 待ち受けられなかったことはログに残す（アプリの動き）。この試験はそれをわざと起こす
        app.AllowLoggedFailures = true;
        var busy = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        busy.Start();
        var taken = ((System.Net.IPEndPoint)busy.LocalEndpoint).Port;
        JsonStore.Write(app.Services.Paths.ApiSettingsFile, new ApiSettings { Port = taken });
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        try
        {
            settings.ApiEnabled = true;
            await UiThread.Until(() => app.Services.Api.State == Services.ApiRunState.Failed, "待ち受けられない");
            Assert.Equal(
                $"ポート {taken} で待ち受けられませんでした。ほかのアプリが使っているときは、ポートを変えてください。",
                settings.ApiStatusText);

            // ポートを変えたら、その場で待ち受け直す（起動し直さなくてよい）
            var free = FreePort();
            settings.ApiPort = free;
            await UiThread.Until(() => app.Services.Api.State == Services.ApiRunState.Running, "変えたポートで待ち受ける");
            Assert.Equal($"ポート {free} で動いています。", settings.ApiStatusText);
        }
        finally
        {
            busy.Stop();
            await app.Services.Api.StopAsync();
            settings.OnLeaving();
        }
    });

    [Fact]
    public Task 画面を出さない起動の自動の取り込みは_APIの設定へ保存し_ほかの欄は残す() => TestApp.Run(async app =>
    {
        JsonStore.Write(app.Services.Paths.ApiSettingsFile, new ApiSettings { Port = 40001, AllowedOrigins = ["http://localhost:5173"] });
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        Assert.False(settings.ApiImportOnHeadlessLaunch);

        settings.ApiImportOnHeadlessLaunch = true;
        await app.SettleAsync();

        var saved = Saved(app);
        Assert.True(saved.ImportOnHeadlessLaunch);
        Assert.Equal(40001, saved.Port);
        Assert.Equal(["http://localhost:5173"], saved.AllowedOrigins);
        Assert.True(new SettingsViewModel(app.Services, main).ApiImportOnHeadlessLaunch);
    });

    [Fact]
    public Task トークンを作り直すと_止まっていればトークンだけ変わり_動いていれば新しいトークンで待ち受け直す() => TestApp.Run(async app =>
    {
        var port = FreePort();
        JsonStore.Write(app.Services.Paths.ApiSettingsFile, new ApiSettings { Port = port });
        JsonStore.Write(app.Services.Paths.ApiFile, new ApiEndpointRecord { Port = port, Token = "old-token" });
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        try
        {
            await settings.RenewApiTokenAsync();
            var renewed = JsonStore.Read<ApiEndpointRecord>(app.Services.Paths.ApiFile)!;
            Assert.NotEqual("old-token", renewed.Token);
            Assert.Null(renewed.ProcessId);
            Assert.Equal(Services.ApiRunState.Stopped, app.Services.Api.State);
            Assert.Equal("トークンを作り直しました。ツールには、保存先の api.json から読み直させてください。", settings.ApiTokenNotice);

            settings.ApiEnabled = true;
            await UiThread.Until(() => app.Services.Api.State == Services.ApiRunState.Running, "API が待ち受ける");
            var before = JsonStore.Read<ApiEndpointRecord>(app.Services.Paths.ApiFile)!.Token;

            await settings.RenewApiTokenAsync();
            Assert.Equal(Services.ApiRunState.Running, app.Services.Api.State);
            var after = JsonStore.Read<ApiEndpointRecord>(app.Services.Paths.ApiFile)!.Token;
            Assert.NotEqual(before, after);

            using var http = new System.Net.Http.HttpClient();
            async Task<System.Net.HttpStatusCode> ItemsWith(string token)
            {
                using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"http://127.0.0.1:{port}/v1/items?limit=1");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                return (await http.SendAsync(request)).StatusCode;
            }

            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, await ItemsWith(before!));
            Assert.Equal(System.Net.HttpStatusCode.OK, await ItemsWith(after!));
        }
        finally
        {
            await app.Services.Api.StopAsync();
            settings.OnLeaving();
        }
    });

    [Fact]
    public Task ほかのアプリからの変更を許すは_APIの設定へ保存し_ほかの欄は残す() => TestApp.Run(async app =>
    {
        JsonStore.Write(app.Services.Paths.ApiSettingsFile, new ApiSettings { Port = 40002, AllowedOrigins = ["http://localhost:5173"] });
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        Assert.False(settings.ApiAllowWrites);

        settings.ApiAllowWrites = true;
        await app.SettleAsync();

        var saved = Saved(app);
        Assert.True(saved.AllowWrites);
        Assert.Equal(40002, saved.Port);
        Assert.Equal(["http://localhost:5173"], saved.AllowedOrigins);
        Assert.False(saved.Enabled);
        settings.OnLeaving();
    });

    private static ApiClientRecord FakeClient(string id, string name, params string[] scopes) => new()
    {
        Id = id,
        Name = name,
        ExePath = @"C:\作り物\" + name + ".exe",
        Scopes = scopes,
        TokenHash = new string('0', 63) + id[^1],
        GrantedAt = new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.FromHours(9)),
    };

    [Fact]
    public Task つないでいるソフトは_権限のチェックで書き換え_ほかのソフトとほかの欄は残す() => TestApp.Run(async app =>
    {
        JsonStore.Write(app.Services.Paths.ApiClientsFile, new ApiClientList
        {
            Clients = [FakeClient("a1", "作り物の道具", ApiScopes.Marks), FakeClient("b2", "別の作り物", ApiScopes.Unity)],
        });
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);

        Assert.True(settings.HasApiClients);
        var row = settings.ApiClients.First();
        Assert.Equal("作り物の道具", row.Name);
        Assert.Equal("まだ使っていません", row.LastUsedText);

        // 並びはいつも全部の種類。持っている物だけにチェック
        Assert.Equal(ApiScopes.All, row.Scopes.Select(choice => choice.Scope));
        Assert.Equal([ApiScopes.Marks], row.Scopes.Where(choice => choice.IsChecked).Select(choice => choice.Scope));

        row.Scopes.Single(choice => choice.Scope == ApiScopes.Tags).IsChecked = true;
        row.Scopes.Single(choice => choice.Scope == ApiScopes.Marks).IsChecked = false;
        await app.SettleAsync();

        var saved = app.Services.Store.ApiClients.Load().Clients;
        Assert.Equal([ApiScopes.Tags], saved.Single(client => client.Id == "a1").Scopes);
        Assert.Equal([ApiScopes.Unity], saved.Single(client => client.Id == "b2").Scopes);
        Assert.Equal(new string('0', 63) + "1", saved.Single(client => client.Id == "a1").TokenHash);
        settings.OnLeaving();
    });

    [Fact]
    public Task つないでいるソフトの取り消しは_確かめてから控えから消す() => TestApp.Run(async app =>
    {
        JsonStore.Write(app.Services.Paths.ApiClientsFile, new ApiClientList
        {
            Clients = [FakeClient("a1", "作り物の道具", ApiScopes.Marks), FakeClient("b2", "別の作り物")],
        });
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        var answer = false;
        settings.ConfirmRevokeApiClient = _ => answer;

        settings.ApiClients.First().RevokeCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal(2, app.Services.Store.ApiClients.Load().Clients.Count);

        answer = true;
        settings.ApiClients.First().RevokeCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal(["b2"], app.Services.Store.ApiClients.Load().Clients.Select(client => client.Id));
        Assert.Equal(["別の作り物"], settings.ApiClients.Select(row => row.Name));
        settings.OnLeaving();
    });

    [Fact]
    public void 許しの窓は_求められた種類を既定でチェックし_外した物は許さない()
    {
        var model = new ApiClientApprovalViewModel(new Chmonos.Api.ApiClientRequestInfo(
            "作り物の道具", null, "http://localhost:5173", [ApiScopes.Marks, ApiScopes.Unity], Replaces: true));

        Assert.Equal("「作り物の道具」がAPIの利用を求めています", model.Heading);
        Assert.Equal("確かめられませんでした", model.ExePathText);
        Assert.False(model.HasExePath);
        Assert.True(model.HasOrigin);
        Assert.All(model.Scopes, choice => Assert.True(choice.IsChecked));

        model.Scopes.Single(choice => choice.Scope == ApiScopes.Unity).IsChecked = false;
        var decision = model.Approve();
        Assert.True(decision.Approved);
        Assert.Equal([ApiScopes.Marks], decision.Scopes);
    }

    [Fact]
    public Task 許したソフトが無ければ_空の文を出す() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        Assert.False(settings.HasApiClients);
        Assert.Empty(settings.ApiClients);
        settings.OnLeaving();
    });
}
