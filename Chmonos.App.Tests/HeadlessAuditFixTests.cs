using System.Net;
using System.Net.Http;
using System.Text.Json;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>点検36・37の直し（取り込みの係と API の係）。名前・パスは作り物。</summary>
public sealed class HeadlessAuditFixTests
{
    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<int> EnableApiAsync(TestApp app)
    {
        var port = FreePort();
        JsonStore.Write(app.Services.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, Port = port });
        await app.Services.Api.ApplyAsync();
        return port;
    }

    [Fact]
    public Task 取り込みの係が自分で取り込み中を伝え_終われば外す() => TestApp.Run(async app =>
    {
        await app.StartAsync();
        var file = app.NewFile("作り物の帽子.unitypackage");
        string? during = null;
        app.Services.Imports.Started += _ => during = app.Services.Api.BusyKind;

        await app.Services.Imports.StartOrStackAsync(new ImportRequest(() => [file], [file]));

        Assert.Equal("import", during);
        Assert.Null(app.Services.Api.BusyKind);
    });

    [Fact]
    public Task 取り込みの途中にAPIを立て直しても_取り込み中が伝わる() => TestApp.Run(async app =>
    {
        await app.StartAsync();
        app.Services.Api.NoteBusy("import", "import");
        var port = await EnableApiAsync(app);
        try
        {
            using var http = new HttpClient();
            var version = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/v1/version")).RootElement;
            Assert.Equal("import", version.GetProperty("busy").GetString());
        }
        finally
        {
            app.Services.Api.NoteBusy("import", null);
            await app.Services.Api.StopAsync();
        }
    });

    [Fact]
    public Task 引越しの間は_設定が変わってもAPIを始めず_運べなければ戻す() => TestApp.Run(async app =>
    {
        await app.StartAsync();
        await EnableApiAsync(app);
        try
        {
            await app.Services.Api.SuspendAsync();
            Assert.Equal(ApiRunState.Stopped, app.Services.Api.State);

            await app.Services.Api.ApplyAsync();
            Assert.Equal(ApiRunState.Stopped, app.Services.Api.State);

            await app.Services.Api.ResumeAsync();
            Assert.Equal(ApiRunState.Running, app.Services.Api.State);
        }
        finally
        {
            await app.Services.Api.StopAsync();
        }
    });

    [Fact]
    public Task 立ち上げ中に預かった頼みの履歴も残る() => TestApp.Run(async app =>
    {
        await app.StartAsync();
        var first = app.NewFile("一つ目.unitypackage");
        var later = app.NewFile("二つ目.unitypackage");
        Task<int?>? stacked = null;

        // 1つ目の対象を決めている最中（立ち上げの途中）に、2つ目の頼みが来る
        await app.Services.Imports.StartOrStackAsync(new ImportRequest(
            () =>
            {
                stacked = app.Services.Imports.StartOrStackAsync(new ImportRequest(() => [later], [later]));
                return [first];
            },
            [first]));
        await stacked!;

        var history = app.Services.Settings.ImportFolders;
        Assert.Contains(history, path => Core.Services.PathText.Same(path, first));
        Assert.Contains(history, path => Core.Services.PathText.Same(path, later));
    });

    [Fact]
    public Task 取り込みが走っている間に作った取り込み画面は_走っていると映す() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var file = app.NewFile("作り物の帽子.unitypackage");
        ImportViewModel? opened = null;
        var runningWhenOpened = false;

        // 画面の無い起動で走っている取り込みの最中に、窓（取り込み画面）を作ったのと同じ
        app.Services.Imports.Started += _ =>
        {
            opened ??= new ImportViewModel(app.Services, main);
            runningWhenOpened = opened.IsRunning;
        };
        await app.Services.Imports.StartOrStackAsync(new ImportRequest(() => [file], [file]));

        Assert.NotNull(opened);
        Assert.True(runningWhenOpened);
        Assert.False(opened.IsRunning);
    });
}
