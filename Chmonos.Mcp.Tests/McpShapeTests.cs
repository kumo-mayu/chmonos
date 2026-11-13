using System.IO.Pipelines;
using System.Text;
using System.Text.Json.Nodes;

namespace Chmonos.Mcp.Tests;

/// <summary>答えの切り詰め・名乗る名前・引数の読み方・stdout に MCP の通信だけが流れること。</summary>
public sealed class McpShapeTests
{
    /// <summary>
    /// 省いたときの引き直し方は道具ごと（ユーザ判断 2026-11-13）。前はどの道具でも「件数（limit）を減らして」と言い、
    /// limit の無い道具で AI が無い引数を付けて呼び直しかねなかった
    /// </summary>
    [Fact]
    public void 省いた答えは_渡された引き直し方だけを言い_limitを勧めない()
    {
        var wide = new JsonObject();
        for (var i = 0; i < 1000; i++)
        {
            wide[$"field{i}"] = new string('x', 100);
        }

        var hinted = JsonNode.Parse(AnswerShaper.Fit(wide, retryHint: AnswerShaper.ItemRetryHint))!;
        var plain = JsonNode.Parse(AnswerShaper.Fit(wide))!;

        Assert.True(hinted["omitted"]!.GetValue<bool>());
        Assert.EndsWith(AnswerShaper.ItemRetryHint, hinted["message"]!.GetValue<string>());
        Assert.EndsWith("省きました。", plain["message"]!.GetValue<string>());
        Assert.DoesNotContain("limit", plain["message"]!.GetValue<string>());
    }

    [Fact]
    public void 長い文字列と入れ子の一覧を切り詰め_絵の場所とHTMLは落とす()
    {
        var node = new JsonObject
        {
            ["memo"] = new string('あ', 1000),
            ["thumbnail"] = new JsonObject { ["path"] = @"C:\作り物\絵.webp" },
            ["html"] = "<p>作り物</p>",
            ["avatarIds"] = new JsonArray(Enumerable.Range(0, 100).Select(i => (JsonNode?)JsonValue.Create(i.ToString())).ToArray()),
        };

        var slim = AnswerShaper.Slim(node)!.AsObject();

        Assert.False(slim.ContainsKey("thumbnail"));
        Assert.False(slim.ContainsKey("html"));
        Assert.StartsWith(new string('あ', AnswerShaper.MaxStringChars) + "…（1000字", slim["memo"]!.GetValue<string>());
        var ids = slim["avatarIds"]!.AsArray();
        Assert.Equal(AnswerShaper.MaxNestedItems + 1, ids.Count);
        Assert.Contains("ほか 70 件", ids[^1]!.GetValue<string>());

        // 元は変えない
        Assert.True(node.ContainsKey("thumbnail"));
    }

    [Fact]
    public void 一覧は上限の文字数に収まるまで件数を減らし_続きの位置を示す()
    {
        var items = Enumerable.Range(0, 200)
            .Select(i => (JsonNode?)new JsonObject { ["id"] = i.ToString(), ["name"] = "作り物の名前" + new string('い', 250) })
            .ToList();

        var page = JsonNode.Parse(AnswerShaper.Page(items, offset: 10, limit: 100))!;

        var raw = AnswerShaper.Page(items, offset: 10, limit: 100);
        Assert.True(raw.Length <= AnswerShaper.MaxAnswerChars);

        // 日本語を \uXXXX に逃がさない（AI が読む字数が増える）
        Assert.Contains("作り物の名前", raw);
        Assert.Equal(200, page["total"]!.GetValue<int>());
        var returned = page["returned"]!.GetValue<int>();
        Assert.InRange(returned, 1, 99);
        Assert.Equal(10 + returned, page["nextOffset"]!.GetValue<int>());
        Assert.Equal("10", page["items"]![0]!["id"]!.GetValue<string>());

        var last = JsonNode.Parse(AnswerShaper.Page(items, offset: 195, limit: 30))!;
        Assert.Null(last["nextOffset"]);
        Assert.Equal(5, last["returned"]!.GetValue<int>());
    }

    [Fact]
    public void 大きな1件も_JSONのまま上限に収める()
    {
        var big = new JsonObject
        {
            ["files"] = new JsonArray(Enumerable.Range(0, 30).Select(i => (JsonNode?)new JsonObject
            {
                ["paths"] = new JsonArray(Enumerable.Range(0, 30).Select(j => (JsonNode?)JsonValue.Create(@"D:\作り物\" + new string('う', 200) + j)).ToArray()),
            }).ToArray()),
        };

        var text = AnswerShaper.Fit(big);

        Assert.True(text.Length <= AnswerShaper.MaxAnswerChars);
        Assert.NotNull(JsonNode.Parse(text));
    }

    [Fact]
    public void 全角空白と絵文字は逃がさず_引用符とバックスラッシュと制御文字は逃がして_JSONとして読み直せる()
    {
        var original = "全角　空白と絵文字🐾と\"引用符\"と\\と\u0001と<b>&'";
        var text = new JsonObject { ["text"] = original }.ToJsonString(AnswerShaper.Json);

        Assert.DoesNotContain("\\u3000", text);
        Assert.DoesNotContain("\\uD83D", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("　", text);
        Assert.Contains("🐾", text);
        Assert.Contains("\\u0001", text);
        Assert.Equal(original, JsonNode.Parse(text)!["text"]!.GetValue<string>());

        // 対にならないサロゲートは、書き手が置き換え文字にする。壊れた JSON にも例外にもならない
        var lone = new JsonObject { ["t"] = "a\uD83Db" }.ToJsonString(AnswerShaper.Json);
        Assert.NotNull(JsonNode.Parse(lone));
    }

    [Fact]
    public void 逃がさなくなった字数で_上限の判定と切り詰めは狂わない()
    {
        // 全角空白と絵文字ばかり。逃がしていれば1字が6〜12字になって上限を超えていた
        var items = Enumerable.Range(0, 100)
            .Select(i => (JsonNode?)new JsonObject { ["id"] = i.ToString(), ["name"] = string.Concat(Enumerable.Repeat("　🐾", 80)) })
            .ToList();

        var raw = AnswerShaper.Page(items, offset: 0, limit: 100);

        Assert.True(raw.Length <= AnswerShaper.MaxAnswerChars);
        Assert.DoesNotContain("\\u", raw);
        var page = JsonNode.Parse(raw)!;
        // 逃がしたときの6字より多く載る（件数は字数で決まる）
        Assert.True(page["returned"]!.GetValue<int>() >= 20);

        // 切り詰めは文字列の字数（UTF-16）で行い、結果は JSON のまま
        var cut = AnswerShaper.Fit(new JsonObject { ["memo"] = new string('　', 1000) });
        Assert.True(cut.Length <= AnswerShaper.MaxAnswerChars);
        Assert.Contains("先頭" + AnswerShaper.MaxStringChars + "字", cut);
        Assert.NotNull(JsonNode.Parse(cut));
    }

    [Theory]
    [InlineData(null, "claude-code", "claude-code（MCP）")]
    [InlineData("Claude Code", "claude-code", "Claude Code（MCP）")]
    [InlineData(null, null, "AIの道具（MCP）")]
    [InlineData(" \u0007 ", null, "AIの道具（MCP）")]
    [InlineData("作り物\n道具\u202E", null, "作り物道具（MCP）")]
    public void 名乗る名前は制御文字を除き_MCPを添える(string? title, string? name, string expected)
        => Assert.Equal(expected, ChmonosBridge.ClientNameFrom(title, name));

    [Fact]
    public void 名乗る名前は64字までに切り詰める()
    {
        var name = ChmonosBridge.ClientNameFrom(new string('え', 100), null);
        Assert.Equal(64, name.Length);
        Assert.EndsWith("（MCP）", name);
    }

    [Theory]
    [InlineData(new[] { "--mcp", "--store", @"D:\作り物" }, @"D:\作り物")]
    [InlineData(new[] { "--mcp", @"--store=D:\作り物" }, @"D:\作り物")]
    [InlineData(new[] { "--mcp" }, null)]
    [InlineData(new[] { "--mcp", "--store" }, null)]
    public void 保存先の引数を読む(string[] args, string? expected)
    {
        Assert.True(McpProgram.IsRequested(args));
        Assert.Equal(expected, McpOptions.StoreOf(args));
    }

    [Fact]
    public void 引数なしや別の引数では通訳に分かれない()
    {
        Assert.False(McpProgram.IsRequested([]));
        Assert.False(McpProgram.IsRequested(["--headless"]));
        Assert.True(McpProgram.IsRequested(["--MCP"]));
    }

    [Fact]
    public async Task 出力の流れにはMCPの通信だけが流れ_知らせはstderrへ行く()
    {
        var root = Path.Combine(Path.GetTempPath(), "chmonos-mcp-tests", Guid.NewGuid().ToString("N"));
        var options = new McpOptions(null, Path.Combine(root, "running"), Path.Combine(root, "mcp-tokens.json"), TimeSpan.FromSeconds(1));
        var input = new Pipe();
        var output = new Pipe();
        var log = new StringWriter();
        var stray = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(stray);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var transport = new ModelContextProtocol.Server.StreamServerTransport(input.Reader.AsStream(), output.Writer.AsStream(), "chmonos");
            var run = McpProgram.RunAsync(transport, options, log, cancel.Token);
            var writer = new StreamWriter(input.Writer.AsStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            var reader = new StreamReader(output.Reader.AsStream(), Encoding.UTF8);

            await writer.WriteLineAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"作り物の道具","version":"1.0"}}}""");
            var initialized = JsonNode.Parse((await reader.ReadLineAsync(cancel.Token))!)!;
            Assert.Equal("chmonos", initialized["result"]!["serverInfo"]!["name"]!.GetValue<string>());

            await writer.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await writer.WriteLineAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"search_items","arguments":{"query":"作り物"}}}""");
            var answer = JsonNode.Parse((await reader.ReadLineAsync(cancel.Token))!)!;
            Assert.Equal("2.0", answer["jsonrpc"]!.GetValue<string>());
            Assert.Equal(2, answer["id"]!.GetValue<int>());
            Assert.True(answer["result"]!["isError"]!.GetValue<bool>());

            input.Writer.Complete();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            Console.SetOut(previous);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        Assert.Equal(string.Empty, stray.ToString());
    }
}
