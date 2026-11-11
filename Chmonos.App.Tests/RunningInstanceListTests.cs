using System.IO;
using System.Net;
using Chmonos.App.Tests.Support;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 動いている Chmonos の一覧の札を、アプリの一式が API の状態に合わせて書き直す（docs/spec/running-instances.md）。
/// 画面ありと画面無し（<c>--headless</c>）で同じ道を通る。置き場は一時フォルダで、本物の置き場には書かない。
/// </summary>
public sealed class RunningInstanceListTests
{
    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task APIを始めるとポートを書き_止めると無しに戻し_閉じると消える(bool headless) => TestApp.Run(async app =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "chmonos-running-test-" + Guid.NewGuid().ToString("N"));
        var file = RunningInstances.FileFor(directory, Environment.ProcessId);
        try
        {
            // API がオフのまま起動：ポートは null
            var recorder = app.Services.RecordRunning(headless, directory)!;
            var record = Assert.Single(RunningInstances.ReadAll(directory));
            Assert.Null(record.Port);
            Assert.Equal(headless, record.Headless);
            Assert.Equal(app.Services.Paths.Root, record.StoreRoot);
            Assert.Equal(app.Services.Paths.ApiFile, record.ApiFile);
            Assert.True(RunningInstances.IsAlive(record.ProcessId, record.StartedAt));

            var port = FreePort();
            JsonStore.Write(app.Services.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, Port = port });
            await app.Services.Api.ApplyAsync();
            try
            {
                Assert.Equal(Services.ApiRunState.Running, app.Services.Api.State);
                Assert.Equal(port, Assert.Single(RunningInstances.ReadAll(directory)).Port);

                // トークンは api.json にだけある。札には値も欄も無い
                var token = JsonStore.Read<ApiEndpointRecord>(app.Services.Paths.ApiFile)!.Token;
                Assert.False(string.IsNullOrEmpty(token));
                var text = File.ReadAllText(file);
                Assert.DoesNotContain(token, text, StringComparison.Ordinal);
                Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);

                // ポートを変えたら書き直す
                var moved = FreePort();
                JsonStore.Write(app.Services.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, Port = moved });
                await app.Services.Api.ApplyAsync();
                Assert.Equal(moved, Assert.Single(RunningInstances.ReadAll(directory)).Port);

                // オフにしたら null に戻す（札は残る：アプリは動いている）
                JsonStore.Write(app.Services.Paths.ApiSettingsFile, new ApiSettings { Enabled = false, Port = moved });
                await app.Services.Api.ApplyAsync();
                Assert.Null(Assert.Single(RunningInstances.ReadAll(directory)).Port);
            }
            finally
            {
                await app.Services.Api.StopAsync();
            }

            recorder.Dispose();
            Assert.False(File.Exists(file));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    });

    /// <summary>ポートが使われていて始められなかったときは、待とうとした番号を書かない（外の道具がつながらない番号を読まないように）。</summary>
    [Fact]
    public Task 始められなかったときはポートを書かない() => TestApp.Run(async app =>
    {
        // 始められなかったことはログに残る（それが正しい動き）
        app.AllowLoggedFailures = true;
        var directory = Path.Combine(Path.GetTempPath(), "chmonos-running-test-" + Guid.NewGuid().ToString("N"));
        var busy =new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        busy.Start();
        try
        {
            using var recorder = app.Services.RecordRunning(headless: true, directory)!;
            var taken = ((IPEndPoint)busy.LocalEndpoint).Port;
            JsonStore.Write(app.Services.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, Port = taken });
            await app.Services.Api.ApplyAsync();

            Assert.Equal(Services.ApiRunState.Failed, app.Services.Api.State);
            Assert.Null(Assert.Single(RunningInstances.ReadAll(directory)).Port);
        }
        finally
        {
            busy.Stop();
            await app.Services.Api.StopAsync();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    });
}
