using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Chmonos.Mcp.Tests.Robustness;

/// <summary>
/// API が変な答えを返す・答えを保留するときの通訳（2026-11-13 の Codex の attack の試験から）。
/// 相手は試験専用の 127.0.0.1 に立てた作り物で、札・トークン・保存先は台の一時フォルダだけ
/// </summary>
public sealed class AbnormalApiReplyTests
{
    private static async Task<WebApplication> StartAsync(McpHarness h, Func<HttpContext, Task> reply)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.Run(context => reply(context));
        await app.StartAsync();
        var store = Path.Combine(h.Root, "library");
        Directory.CreateDirectory(store);
        h.Announce(store, new Uri(app.Urls.Single()).Port, 42001);
        new TokenStore(h.TokenFile).Save(store, ChmonosBridge.ClientNameFrom(null, McpHarness.ClientName), "synthetic-token", DateTimeOffset.UnixEpoch);
        return app;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 隠すIDの取得が不完全なら_一覧の秘密を返さない(bool moreThanHundredPages)
    {
        await using var h = new McpHarness();
        var page = 0;
        await using var app = await StartAsync(h, async context =>
        {
            var path = context.Request.Path.Value;
            object answer = path switch
            {
                "/v1/display-settings" => new { showAdult = false },
                // 20001件目の隠した商品まで正しく続きがある応答。100頁で黙って打ち切ってはいけない。
                "/v1/search" when moreThanHundredPages => ++page <= 100
                    ? new { items = Enumerable.Range((page - 1) * 200, 200).Select(i => new { id = "hidden-" + i }).ToArray(), nextCursor = "page-" + page }
                    : (object)new { items = new[] { new { id = "hidden-synthetic" } }, nextCursor = (string?)null },
                // items が欠けた答え
                "/v1/search" => new { total = 1, nextCursor = (string?)null },
                "/v1/notifications" => new[] { new { itemId = "hidden-synthetic", message = "SECRET_SYNTHETIC" } },
                _ => new { },
            };
            await context.Response.WriteAsJsonAsync(answer);
        });
        var client = await h.ConnectAsync(h.Options());
        var result = await McpHarness.CallAsync(client, "list_notifications");
        Assert.True(result.IsError, result.Text);
        Assert.Contains("集めきれなかった", result.Text);
        Assert.DoesNotContain("SECRET_SYNTHETIC", result.Text);
        Assert.DoesNotContain("hidden-synthetic", result.Text);
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("wrong-type")]
    [InlineData("disconnect")]
    public async Task APIの壊れた応答と切断は道具の誤りになり_次は回復する(string mode)
    {
        await using var h = new McpHarness();
        var broken = true;
        await using var app = await StartAsync(h, async context =>
        {
            if (context.Request.Path == "/v1/display-settings")
            {
                if (broken && mode == "disconnect") { context.Abort(); return; }
                if (broken && mode == "truncated") { await context.Response.WriteAsync("{\"showAdult\":"); return; }
                await context.Response.WriteAsJsonAsync(new { showAdult = broken ? (object)"wrong" : true });
                return;
            }

            if (context.Request.Path == "/v1/search")
            {
                await context.Response.WriteAsJsonAsync(new { items = Array.Empty<object>(), total = 0, nextCursor = (string?)null });
                return;
            }

            await context.Response.WriteAsJsonAsync(new { id = "synthetic", name = "safe", isHidden = false, isAdult = false });
        });
        var client = await h.ConnectAsync(h.Options());
        Assert.True((await McpHarness.CallAsync(client, "get_item", new { id = "synthetic" })).IsError);
        broken = false;
        Assert.False((await McpHarness.CallAsync(client, "get_item", new { id = "synthetic" })).IsError);
        Assert.False((await McpHarness.CallAsync(client, "chmonos_status")).IsError);
    }

    [Fact]
    public async Task APIが応答を保留していても_呼び出しを中断して次へ進める()
    {
        await using var h = new McpHarness();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = true;
        await using var app = await StartAsync(h, async context =>
        {
            if (hold)
            {
                entered.TrySetResult();
                var aborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = context.RequestAborted.Register(() => aborted.TrySetCanceled(context.RequestAborted));
                await aborted.Task;
            }

            if (context.Request.Path == "/v1/search")
            {
                await context.Response.WriteAsJsonAsync(new { items = Array.Empty<object>(), total = 0, nextCursor = (string?)null });
                return;
            }

            await context.Response.WriteAsJsonAsync(new { id = "synthetic", isHidden = false, isAdult = false, showAdult = true });
        });
        var client = await h.ConnectAsync(h.Options());
        using var cancel = new CancellationTokenSource();
        var call = client.CallToolAsync("get_item", new Dictionary<string, object?> { ["id"] = "synthetic" }, cancellationToken: cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await call);
        hold = false;
        Assert.False((await McpHarness.CallAsync(client, "get_item", new { id = "synthetic" })).IsError);
    }
}
