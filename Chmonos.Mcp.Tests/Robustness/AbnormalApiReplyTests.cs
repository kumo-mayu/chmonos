using System.Text;
using System.Text.Json.Nodes;
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
    public async Task 制御文字の多い説明文も_JSONにした後で8000字以内に収める()
    {
        // 制御文字は JSON で \uXXXX の6字になるので、本文6,000字が JSON では3万字を超える。区切り目の両側に絵文字を置き、組の途中で切らないことも見る
        var summary = new string('\u0001', 6000) + string.Concat(Enumerable.Repeat("🐾", 3000)) + "終わり";
        await using var h = new McpHarness();
        await using var app = await StartAsync(h, async context =>
        {
            object answer = context.Request.Path.Value switch
            {
                "/v1/display-settings" => new { showAdult = true },
                "/v1/items/synthetic/description" => new { summary, sections = Array.Empty<object>() },
                _ => new { id = "synthetic", name = "safe", isHidden = false, isAdult = false },
            };
            await context.Response.WriteAsJsonAsync(answer);
        });
        var client = await h.ConnectAsync(h.Options());

        var whole = new StringBuilder();
        int? offset = 0;
        for (var round = 0; offset is not null; round++)
        {
            Assert.True(round < 100, "続きが終わらない");
            var result = await McpHarness.CallAsync(client, "get_item_description", new { id = "synthetic", max_chars = 6000, offset });
            Assert.False(result.IsError, result.Text);
            Assert.True(result.Text.Length <= AnswerShaper.MaxAnswerChars, $"JSON の答えは {result.Text.Length} 字（上限8000字）");
            var answer = JsonNode.Parse(result.Text)!;
            var text = answer["text"]!.GetValue<string>();
            Assert.Equal(offset, answer["offset"]!.GetValue<int>());
            var next = answer["nextOffset"]?.GetValue<int>();
            Assert.Equal(next ?? answer["totalChars"]!.GetValue<int>(), offset + text.Length);
            Assert.True(text.Length > 0);
            Assert.False(char.IsHighSurrogate(text[^1]), "絵文字の組の途中で切った");
            whole.Append(text);
            offset = next;
        }

        Assert.Equal(summary, whole.ToString());
    }

    [Fact]
    public void 横に広いAPIの答えも_秘密を落として8000字以内に収める()
    {
        var node = new JsonObject { ["blueprintId"] = "SECRET_BLUEPRINT_SYNTHETIC" };
        for (var i = 0; i < 1000; i++)
        {
            node["field" + i] = new string('x', 100);
        }

        var text = AnswerShaper.Fit(node);
        Assert.DoesNotContain("SECRET_BLUEPRINT_SYNTHETIC", text);
        Assert.NotNull(JsonNode.Parse(text));
        Assert.True(text.Length <= AnswerShaper.MaxAnswerChars, $"JSON の答えは {text.Length} 字（上限8000字）");
    }

    [Fact]
    public void 一覧の1件だけを縮めても_続きの印は切らず_封筒ごと8000字以内に収める()
    {
        var cursor = new string('c', 500);
        var item = new JsonObject();
        for (var i = 0; i < 300; i++)
        {
            item["field" + i] = new string('y', 25);
        }

        var answer = new JsonObject { ["total"] = 3, ["returned"] = 1, ["nextCursor"] = cursor, ["items"] = new JsonArray(item) };
        var text = AnswerShaper.FitSingle(answer);
        Assert.True(text.Length <= AnswerShaper.MaxAnswerChars, $"JSON の答えは {text.Length} 字（上限8000字）");
        Assert.Equal(cursor, JsonNode.Parse(text)!["nextCursor"]!.GetValue<string>());
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
