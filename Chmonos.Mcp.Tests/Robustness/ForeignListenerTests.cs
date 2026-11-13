using System.Collections.Concurrent;
using System.Diagnostics;
using Chmonos.Core.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Chmonos.Mcp.Tests.Robustness;

/// <summary>
/// 札の PID と開始時刻は生きている本物のプロセスと合うのに、ポートだけ別のプロセスが待ち受けているとき、トークンを送らない（2026-11-13 の点検 M5）。
/// 生きているかとポートの持ち主は、差し替えずに本物（プロセスの一覧と Windows の TCP の表）で確かめる。
/// 待ち受けは試験のプロセスの中の 127.0.0.1、札の本人は試験が起こした子のプロセス
/// </summary>
public sealed class ForeignListenerTests
{
    private static async Task<(WebApplication App, int Port, ConcurrentQueue<string?> Seen)> ListenAsync()
    {
        var seen = new ConcurrentQueue<string?>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.Run(async context =>
        {
            seen.Enqueue(context.Request.Headers.Authorization.ToString());
            await context.Response.WriteAsJsonAsync(new { showAdult = true });
        });
        await app.StartAsync();
        return (app, new Uri(app.Urls.Single()).Port, seen);
    }

    private static ChmonosBridge Bridge(McpHarness h, Process owner, int port)
    {
        var store = Path.Combine(h.Root, "library");
        Directory.CreateDirectory(store);
        RunningInstances.Write(h.RunningDirectory, new RunningInstanceRecord
        {
            ProcessId = owner.Id,
            StartedAt = new DateTimeOffset(owner.StartTime),
            StoreRoot = store,
            Port = port,
            ApiFile = Path.Combine(store, "api.json"),
            Version = "1.2.0",
        });
        var client = ChmonosBridge.ClientNameFrom(null, McpHarness.ClientName);
        new TokenStore(h.TokenFile).Save(store, client, "synthetic-token", DateTimeOffset.UnixEpoch);
        return new ChmonosBridge(h.Options(), h.Log) { ClientName = client };
    }

    [Fact]
    public async Task 札のポートを別のプロセスが待ち受けていれば_トークンも許しの求めも送らない()
    {
        await using var h = new McpHarness();
        var (app, port, seen) = await ListenAsync();
        await using var _ = app;

        // 札の本人にする、生きている別のプロセス（入力を待って止まっている cmd）
        using var child = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        })!;
        try
        {
            using var bridge = Bridge(h, child, port);
            Assert.True(RunningInstances.IsAlive(child.Id, new DateTimeOffset(child.StartTime)), "札の本人が生きていない（試験の前提が崩れた）");

            var answer = await bridge.GetAsync("/v1/display-settings", CancellationToken.None);

            Assert.True(answer.Failed);
            Assert.Contains("別のプログラム", answer.Problem);
            Assert.Empty(seen);
        }
        finally
        {
            child.Kill();
            await child.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task 札の本人が待ち受けていれば_トークンを付けて問い合わせる()
    {
        // 上の試験が「何も送らない」だけで通らないよう、本物の確かめで本人なら送ることも見る
        await using var h = new McpHarness();
        var (app, port, seen) = await ListenAsync();
        await using var _ = app;
        using var self = Process.GetCurrentProcess();
        using var bridge = Bridge(h, self, port);

        var answer = await bridge.GetAsync("/v1/display-settings", CancellationToken.None);

        Assert.False(answer.Failed, answer.Problem);
        Assert.Equal(["Bearer synthetic-token"], seen);
    }
}
