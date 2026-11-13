using System.Text.Json;
using System.Text.Json.Nodes;

namespace Chmonos.Mcp.Tests;

/// <summary>
/// 通訳 → API の対応（docs/spec/mcp.md）：道具の一覧・Chmonos が無い／オフ／2つのときの答え・探し直し・許しの流れ（求める→待つ→控える→次は聞かない→401 で求め直す）・
/// 表示の設定に従うこと。API は一時フォルダの保存先で 127.0.0.1 に立てた物だけ
/// </summary>
public sealed class McpBridgeTests : IAsyncLifetime
{
    private readonly McpHarness _harness = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    private static JsonNode Parse(string text) => JsonNode.Parse(text)!;

    [Fact]
    public async Task 道具は15個で_どれも読むだけの印を付ける()
    {
        var client = await _harness.ConnectAsync(_harness.Options());
        var tools = await client.ListToolsAsync();

        Assert.Equal(15, tools.Count);
        Assert.All(tools, tool =>
        {
            Assert.Matches("^[a-z]+(_[a-z]+)*$", tool.Name);
            Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint);
            Assert.False(tool.ProtocolTool.Annotations?.OpenWorldHint);
        });

        // 書き込みの道具が無いので、許しで書き込みの種類を求めない
        Assert.All(ChmonosTools.All, tool => Assert.Null(tool.WriteScope));
    }

    [Fact]
    public async Task Chmonosが無ければ落ちずに起動の仕方を返し_後から起動したら次の呼び出しで見つける()
    {
        var client = await _harness.ConnectAsync(_harness.Options());

        var (isError, text) = await McpHarness.CallAsync(client, "search_items", new { query = "帽子" });
        Assert.True(isError);
        Assert.Contains("Chmonos が動いていません", text);
        Assert.Contains("APIを使う", text);

        var (storeRoot, port) = await _harness.StartApiAsync("store-a");
        _harness.Announce(storeRoot, port, 101);
        _ = _harness.Approvals.ApproveNextAsync();

        (isError, text) = await McpHarness.CallAsync(client, "search_items", new { query = "帽子" });
        Assert.False(isError, text);
        Assert.Equal("9700001", Parse(text)["items"]![0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task APIがオフなら_オンにするよう言う()
    {
        var (storeRoot, _) = await _harness.StartApiAsync("store-a");
        _harness.Announce(storeRoot, port: null, 101);
        var client = await _harness.ConnectAsync(_harness.Options());

        var (isError, text) = await McpHarness.CallAsync(client, "get_item", new { id = "9700001" });
        Assert.True(isError);
        Assert.Contains("API がオフ", text);
    }

    [Fact]
    public async Task 落ちた札は数えない()
    {
        var (storeRoot, port) = await _harness.StartApiAsync("store-a");
        _harness.Announce(storeRoot, port, 101);
        _harness.Announce(Path.Combine(_harness.Root, "落ちた保存先"), 1, 102);
        var client = await _harness.ConnectAsync(_harness.Options(), dead: 102);
        _ = _harness.Approvals.ApproveNextAsync();

        var (isError, text) = await McpHarness.CallAsync(client, "get_item", new { id = "9700001" });
        Assert.False(isError, text);
    }

    [Fact]
    public async Task 二つ動いていれば_storeの付け方を言い_storeで選べる()
    {
        var (storeA, portA) = await _harness.StartApiAsync("store-a");
        var (storeB, portB) = await _harness.StartApiAsync("store-b", fill: store =>
            store.Items.SaveAsync(new Core.Models.ItemRecord { Id = "9700009", Booth = new Core.Models.BoothBlock { Name = "作り物のBだけの髪" } }).GetAwaiter().GetResult());
        _harness.Announce(storeA, portA, 101);
        _harness.Announce(storeB, portB, 102);

        var both = await _harness.ConnectAsync(_harness.Options());
        var (isError, text) = await McpHarness.CallAsync(both, "search_items", new { query = "髪" });
        Assert.True(isError);
        Assert.Contains("2 つ動いています", text);
        Assert.Contains("--store", text);
        Assert.Contains(storeA, text);
        Assert.Contains(storeB, text);

        // 区切りの向きと末尾の区切りが違っても、同じ保存先として選ぶ
        var chosen = await _harness.ConnectAsync(_harness.Options(store: storeB.Replace('\\', '/') + "/"));
        _ = _harness.Approvals.ApproveNextAsync();
        (isError, text) = await McpHarness.CallAsync(chosen, "search_items", new { query = "髪" });
        Assert.False(isError, text);
        Assert.Equal("9700009", Parse(text)["items"]![0]!["id"]!.GetValue<string>());

        var missing = await _harness.ConnectAsync(_harness.Options(store: Path.Combine(_harness.Root, "無い保存先")));
        (isError, text) = await McpHarness.CallAsync(missing, "search_items", new { query = "髪" });
        Assert.True(isError);
        Assert.Contains("無い保存先", text);
    }

    [Fact]
    public async Task 初めては許しを求め_名前にMCPを添えて_受け取ったトークンを控え_次からは聞かない()
    {
        var (storeRoot, port) = await _harness.StartApiAsync("store-a");
        _harness.Announce(storeRoot, port, 101);
        var client = await _harness.ConnectAsync(_harness.Options());

        var approve = Task.Run(async () =>
        {
            var asked = await _harness.Approvals.NextAsync();
            Assert.Equal("作り物の道具（MCP）", asked.Info.Name);
            Assert.Empty(asked.Info.Scopes);
            asked.Decision.SetResult(new Api.ApiClientDecision(true, []));
        });
        var (isError, text) = await McpHarness.CallAsync(client, "get_item", new { id = "9700001" });
        await approve;
        Assert.False(isError, text);
        Assert.Equal("作り物の帽子", Parse(text)["name"]!.GetValue<string>());

        // 控えは差し替えた一時フォルダに、保存先と名前ごとに1行
        var saved = JsonNode.Parse(File.ReadAllText(_harness.TokenFile))!["tokens"]!.AsArray();
        Assert.Single(saved);
        Assert.Equal("作り物の道具（MCP）", saved[0]!["client"]!.GetValue<string>());
        Assert.DoesNotContain(saved[0]!["token"]!.GetValue<string>(), _harness.Log.ToString());

        (isError, _) = await McpHarness.CallAsync(client, "get_item", new { id = "9700001" });
        Assert.False(isError);

        // 別のセッションの通訳も、控えを読んで聞かない
        var another = await _harness.ConnectAsync(_harness.Options());
        (isError, _) = await McpHarness.CallAsync(another, "list_classifications", new { kind = "categories" });
        Assert.False(isError);
        Assert.Equal(1, _harness.Approvals.AskedCount);
    }

    [Fact]
    public async Task 待ちきれなければ押してからもう一度と言い_次の呼び出しで同じ求めの続きを待つ()
    {
        var (storeRoot, port) = await _harness.StartApiAsync("store-a");
        _harness.Announce(storeRoot, port, 101);
        var client = await _harness.ConnectAsync(_harness.Options(wait: TimeSpan.FromSeconds(1)));

        var (isError, text) = await McpHarness.CallAsync(client, "get_item", new { id = "9700001" });
        Assert.True(isError);
        Assert.Contains("［許可］を押してから、もう一度", text);

        var asked = await _harness.Approvals.NextAsync();
        asked.Decision.SetResult(new Api.ApiClientDecision(true, []));

        (isError, text) = await McpHarness.CallAsync(client, "get_item", new { id = "9700001" });
        Assert.False(isError, text);
        Assert.Equal(1, _harness.Approvals.AskedCount);
    }

    [Fact]
    public async Task 拒否と_窓を出せないときは_それぞれの文で返す()
    {
        var (storeRoot, port) = await _harness.StartApiAsync("store-a");
        _harness.Announce(storeRoot, port, 101);
        var client = await _harness.ConnectAsync(_harness.Options());

        var deny = Task.Run(async () => (await _harness.Approvals.NextAsync()).Decision.SetResult(Api.ApiClientDecision.Refusal));
        var (isError, text) = await McpHarness.CallAsync(client, "get_item", new { id = "9700001" });
        await deny;
        Assert.True(isError);
        Assert.Contains("許可されませんでした", text);

        _harness.Approvals.CanAsk = false;
        (isError, text) = await McpHarness.CallAsync(client, "get_item", new { id = "9700001" });
        Assert.True(isError);
        Assert.Contains("Chmonos の画面を開いてから", text);
    }

    [Fact]
    public async Task 控えのトークンが効かなければ_捨てて求め直す()
    {
        var (storeRoot, port) = await _harness.StartApiAsync("store-a");
        _harness.Announce(storeRoot, port, 101);
        Directory.CreateDirectory(Path.GetDirectoryName(_harness.TokenFile)!);
        File.WriteAllText(_harness.TokenFile, JsonSerializer.Serialize(new
        {
            version = 1,
            tokens = new[] { new { store = storeRoot, client = "作り物の道具（MCP）", token = "revoked-fake-token-000000", savedAt = DateTimeOffset.Now } },
        }));
        var client = await _harness.ConnectAsync(_harness.Options());
        _ = _harness.Approvals.ApproveNextAsync();

        var (isError, text) = await McpHarness.CallAsync(client, "get_item", new { id = "9700001" });
        Assert.False(isError, text);
        Assert.Equal(1, _harness.Approvals.AskedCount);
        Assert.DoesNotContain("revoked-fake-token-000000", File.ReadAllText(_harness.TokenFile));
    }

    [Fact]
    public async Task 同じ名前の通訳が2本で同時に求めても_後の方は控えを読んで使う()
    {
        var (storeRoot, port) = await _harness.StartApiAsync("store-a");
        _harness.Announce(storeRoot, port, 101);
        var first = await _harness.ConnectAsync(_harness.Options(wait: TimeSpan.FromSeconds(5)));
        var second = await _harness.ConnectAsync(_harness.Options(wait: TimeSpan.FromSeconds(5)));

        var a = McpHarness.CallAsync(first, "get_item", new { id = "9700001" });
        var b = McpHarness.CallAsync(second, "get_item", new { id = "9700001" });

        // 同じソフト（名前・exe・オリジン）の重ねた求めは、Chmonos が1つの窓にまとめる
        var asked = await _harness.Approvals.NextAsync();
        await Task.Delay(500);
        asked.Decision.SetResult(new Api.ApiClientDecision(true, []));

        var results = await Task.WhenAll(a, b);
        Assert.All(results, result => Assert.False(result.IsError, result.Text));
        Assert.Equal(1, _harness.Approvals.AskedCount);
    }

    [Fact]
    public async Task 非表示はいつも_R18は設定で隠すときだけ_AIに渡さない()
    {
        var (storeRoot, port) = await _harness.StartApiAsync("store-a", showAdult: false);
        _harness.Announce(storeRoot, port, 101);
        var client = await _harness.ConnectAsync(_harness.Options());
        _ = _harness.Approvals.ApproveNextAsync();

        var (isError, text) = await McpHarness.CallAsync(client, "search_items");
        Assert.False(isError, text);
        var ids = Parse(text)["items"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).ToList();
        Assert.Equal(["9700001"], ids);

        (isError, text) = await McpHarness.CallAsync(client, "get_item", new { id = "9700002" });
        Assert.True(isError);
        Assert.Contains("非表示", text);

        (isError, text) = await McpHarness.CallAsync(client, "get_item_description", new { id = "9700003" });
        Assert.True(isError);
        Assert.Contains("R-18", text);
    }

    [Fact]
    public async Task R18を表示する設定なら_R18は渡し_非表示は渡さない()
    {
        var (storeRoot, port) = await _harness.StartApiAsync("store-a", showAdult: true);
        _harness.Announce(storeRoot, port, 101);
        var client = await _harness.ConnectAsync(_harness.Options());
        _ = _harness.Approvals.ApproveNextAsync();

        var (isError, text) = await McpHarness.CallAsync(client, "search_items", new { sort = "id:asc" });
        Assert.False(isError, text);
        var ids = Parse(text)["items"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).ToList();
        Assert.Equal(["9700001", "9700003"], ids);

        (isError, _) = await McpHarness.CallAsync(client, "get_item", new { id = "9700003" });
        Assert.False(isError);
    }

    [Fact]
    public async Task 引数の誤りとAPIの誤りは_文で返して落ちない()
    {
        var (storeRoot, port) = await _harness.StartApiAsync("store-a");
        _harness.Announce(storeRoot, port, 101);
        var client = await _harness.ConnectAsync(_harness.Options());
        _ = _harness.Approvals.ApproveNextAsync();

        var (isError, text) = await McpHarness.CallAsync(client, "lookup_file");
        Assert.True(isError);
        Assert.Contains("path か hash", text);

        (isError, text) = await McpHarness.CallAsync(client, "get_item", new { id = "9799999" });
        Assert.True(isError);
        Assert.Contains("404", text);

        (isError, text) = await McpHarness.CallAsync(client, "search_items", new { limit = 999 });
        Assert.True(isError);
        Assert.Contains("1〜50", text);

        // 通訳は動き続ける
        (isError, _) = await McpHarness.CallAsync(client, "get_search_conditions");
        Assert.False(isError);
    }

    [Fact]
    public async Task 状況の道具は_Chmonosに問い合わせずに_つないでいる先とほかの札を返す()
    {
        var (storeRoot, port) = await _harness.StartApiAsync("store-a");
        _harness.Announce(storeRoot, port, 101);
        var client = await _harness.ConnectAsync(_harness.Options());

        var (isError, text) = await McpHarness.CallAsync(client, "chmonos_status");
        Assert.False(isError, text);
        var status = Parse(text);
        Assert.Equal(storeRoot, status["connected"]!["storeRoot"]!.GetValue<string>());
        Assert.Equal("作り物の道具（MCP）", status["clientName"]!.GetValue<string>());
        Assert.Equal(0, _harness.Approvals.AskedCount);
    }
}
