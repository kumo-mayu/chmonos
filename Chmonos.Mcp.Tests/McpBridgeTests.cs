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

    [Theory]
    [InlineData(0, true)]
    [InlineData(200, false)]
    public async Task 検索は上限の30件でも_続きの印を辿って全件を1度ずつ取れる(int padding, bool fitsAtOnce)
    {
        // limit の上限は30（ユーザ判断 2026-11-13。前の50は本物の要約では収まらず、ほぼ25件に減って返っていた）。
        // 名前が短ければ30件が1回で収まり、最上位の一覧を切らずに返すこと（2026-11-13 の点検 M4）を見る。
        // 名前を長くすると30件が収まらないので、件数を半分にして引き直す道でも漏れないことを見る
        const int limit = AnswerShaper.SearchMaxLimit;
        const int added = 70;
        var (storeRoot, port) = await _harness.StartApiAsync("store-a", fill: store =>
        {
            for (var i = 0; i < added; i++)
            {
                store.Items.SaveAsync(new Core.Models.ItemRecord { Id = (9710000 + i).ToString(), Booth = new Core.Models.BoothBlock { Name = "作" + i + new string('名', padding) } })
                    .GetAwaiter().GetResult();
            }
        });
        _harness.Announce(storeRoot, port, 101);
        var client = await _harness.ConnectAsync(_harness.Options());
        _ = _harness.Approvals.ApproveNextAsync();

        var seen = new List<string>();
        string? cursor = null;
        var first = true;
        for (var round = 0; round < 20 && (first || cursor is not null); round++, first = false)
        {
            var (isError, text) = await McpHarness.CallAsync(client, "search_items", new { sort = "id:asc", limit, cursor });
            Assert.False(isError, text);
            var page = Parse(text);
            var items = page["items"]!.AsArray();
            Assert.All(items, item => Assert.IsType<JsonObject>(item));
            Assert.Equal(items.Count, page["returned"]!.GetValue<int>());
            if (round == 0 && fitsAtOnce)
            {
                Assert.True(items.Count == limit, $"1回目は {items.Count} 件（上限の件数を1回で返す道を確かめられていない）");
            }

            seen.AddRange(items.Select(item => item!["id"]!.GetValue<string>()));
            cursor = page["nextCursor"]?.GetValue<string>();
        }

        // 見せてよい物は、足した70件と、台が最初に入れる帽子と R-18 の服（非表示の靴は出ない）
        var expected = Enumerable.Range(0, added).Select(i => (9710000 + i).ToString()).Append("9700001").Append("9700003").Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expected, seen.Order(StringComparer.Ordinal).ToList());
        Assert.Null(cursor);
    }

    [Fact]
    public async Task 説明文は全角空白と絵文字を逃がさず_改行は_nにそろえて渡す()
    {
        var (storeRoot, port) = await _harness.StartApiAsync("store-a", fill: store =>
            store.Items.SaveAsync(new Core.Models.ItemRecord
            {
                Id = "9700001",
                Booth = new Core.Models.BoothBlock { Name = "作り物の帽子", Description = "一行目　全角🐾\r\n二行目\r三行目\n四行目" },
            }).GetAwaiter().GetResult());
        _harness.Announce(storeRoot, port, 101);
        var client = await _harness.ConnectAsync(_harness.Options());
        _ = _harness.Approvals.ApproveNextAsync();

        var (isError, text) = await McpHarness.CallAsync(client, "get_item_description", new { id = "9700001" });

        Assert.False(isError, text);
        Assert.DoesNotContain("\\u3000", text);
        Assert.DoesNotContain("\\uD83D", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\r", text);
        var body = Parse(text)["text"]!.GetValue<string>();
        Assert.DoesNotContain('\r', body);
        Assert.Contains("一行目　全角🐾\n二行目\n三行目\n四行目", body);
        Assert.Equal(body.Length, Parse(text)["totalChars"]!.GetValue<int>());
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
        Assert.Contains("1〜30", text);

        // 通訳は動き続ける
        (isError, _) = await McpHarness.CallAsync(client, "get_search_conditions");
        Assert.False(isError);
    }

    /// <summary>R-18 を隠す保存先に、アバター・改変・通知を足す（隠した商品を指す行も入れる）。</summary>
    private static void FillCatalogs(Core.Storage.DataStore store)
    {
        Core.Storage.JsonStore.Write(store.Paths.AvatarRegistryFile, new Core.Models.AvatarRegistry
        {
            Entries =
            [
                new Core.Models.AvatarRegistryEntry { ItemId = "9700001", BoothName = "作り物の帽子" },
                new Core.Models.AvatarRegistryEntry { ItemId = "9700002", BoothName = "作り物の隠した靴" },
            ],
        });
        store.Modifications.SaveAsync(new Core.Models.ModificationRecord
        {
            Id = "mod-0001", AvatarItemId = "9700001", Name = "作り物の改変", BlueprintId = "avtr_00000000-0000-0000-0000-000000000001",
        }).GetAwaiter().GetResult();
        var now = new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.FromHours(9));
        Core.Storage.JsonStore.Write(store.Paths.NotificationsFile, new List<Core.Models.NotificationRecord>
        {
            new() { Id = "n-1", Kind = Core.Models.NotificationKind.ItemUpdated, ItemId = "9700001", Title = "作り物の帽子", Detail = "作り物の変化", CreatedAt = now },
            new() { Id = "n-2", Kind = Core.Models.NotificationKind.ItemUpdated, ItemId = "9700002", Title = "作り物の隠した靴", Detail = "作り物の変化", CreatedAt = now },
        });
    }

    public static TheoryData<string, object> EveryTool => new()
    {
        { "chmonos_status", new { } },
        { "search_items", new { query = "作り物" } },
        { "get_search_conditions", new { } },
        { "get_item", new { id = "9700001", include = new[] { "avatarLinks", "allFiles", "contents", "meta" } } },
        { "get_item_description", new { id = "9700001" } },
        { "list_avatars", new { } },
        { "get_avatar", new { id = "9700001" } },
        { "list_avatar_bases", new { } },
        { "lookup_file", new { path = @"D:\作り物\無いファイル.zip" } },
        { "list_unitypackages", new { } },
        { "find_unitypackage_assets", new { item_id = "9700001" } },
        { "find_unitypackage_assets", new { guids = new[] { "0123456789abcdef0123456789abcdef" } } },
        { "list_modifications", new { } },
        { "list_modifications", new { id = "mod-0001" } },
        { "get_unity_scans", new { } },
        { "list_notifications", new { } },
        { "list_classifications", new { kind = "user_tags" } },
        { "list_classifications", new { kind = "attributes" } },
        { "list_classifications", new { kind = "categories" } },
        { "list_classifications", new { kind = "booth_tags" } },
        { "list_classifications", new { kind = "shops", query = "作り物" } },
    };

    [Theory]
    [MemberData(nameof(EveryTool))]
    public async Task どの道具も_表示の設定で隠す保存先で答えを返す(string tool, object args)
    {
        var (storeRoot, port) = await _harness.StartApiAsync("store-a", showAdult: false, fill: FillCatalogs);
        _harness.Announce(storeRoot, port, 101);
        var client = await _harness.ConnectAsync(_harness.Options());
        _ = _harness.Approvals.ApproveNextAsync();

        var (isError, text) = await McpHarness.CallAsync(client, tool, args);

        Assert.False(isError, text);
        Assert.NotNull(JsonNode.Parse(text));
        Assert.True(text.Length <= AnswerShaper.MaxAnswerChars);
        Assert.DoesNotContain("avtr_", text);
    }

    [Fact]
    public async Task 隠した商品を指す行は一覧から落とし_落とした数を添える()
    {
        var (storeRoot, port) = await _harness.StartApiAsync("store-a", showAdult: false, fill: FillCatalogs);
        _harness.Announce(storeRoot, port, 101);
        var client = await _harness.ConnectAsync(_harness.Options());
        _ = _harness.Approvals.ApproveNextAsync();

        var (_, text) = await McpHarness.CallAsync(client, "list_notifications");
        var answer = Parse(text);
        Assert.Equal(["n-1"], answer["items"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()));
        Assert.Equal(1, answer["omittedByDisplaySettings"]!.GetValue<int>());

        (_, text) = await McpHarness.CallAsync(client, "list_avatars");
        answer = Parse(text);
        Assert.Equal(["9700001"], answer["items"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()));
        Assert.Equal(1, answer["omittedByDisplaySettings"]!.GetValue<int>());

        var (isError, refusal) = await McpHarness.CallAsync(client, "get_avatar", new { id = "9700002" });
        Assert.True(isError);
        Assert.Contains("隠している", refusal);

        // 一覧の絞り込みは答えの欄の名前に当てる（外れると誤りにならずに0件になる）
        (_, text) = await McpHarness.CallAsync(client, "list_avatars", new { query = "帽子" });
        Assert.Equal(1, Parse(text)["total"]!.GetValue<int>());
        (_, text) = await McpHarness.CallAsync(client, "list_classifications", new { kind = "shops", query = "存在しない名前" });
        Assert.Equal(0, Parse(text)["total"]!.GetValue<int>());
        (_, text) = await McpHarness.CallAsync(client, "list_modifications", new { avatar = "9700001", query = "改変" });
        Assert.Equal(1, Parse(text)["total"]!.GetValue<int>());
    }

    /// <summary>
    /// 隠したアバター（9700002 は非表示・9700003 は R-18）を、表示する商品・改変・共通素体から指す保存先。
    /// 見せてよいアバター 9700001 も並べて、落としすぎ（行ごと消える）も見る
    /// </summary>
    private static void FillHiddenAvatarRefs(Core.Storage.DataStore store)
    {
        Core.Storage.JsonStore.Write(store.Paths.AvatarRegistryFile, new Core.Models.AvatarRegistry
        {
            Entries =
            [
                new Core.Models.AvatarRegistryEntry { ItemId = "9700001", BoothName = "作り物の帽子", BaseName = "作り物の素体" },
                new Core.Models.AvatarRegistryEntry { ItemId = "9700002", BoothName = "作り物の隠した靴", BaseName = "作り物の素体" },
                new Core.Models.AvatarRegistryEntry { ItemId = "9700003", BoothName = "作り物の大人の服", BaseName = "作り物の大人の素体" },
            ],
            BaseGroups =
            [
                new Core.Models.AvatarBaseGroup { Name = "作り物の素体", ItemId = "9700002" },
                new Core.Models.AvatarBaseGroup { Name = "作り物の大人の素体" },
            ],
        });
        store.Items.SaveAsync(new Core.Models.ItemRecord
        {
            Id = "9700004",
            Booth = new Core.Models.BoothBlock { Name = "作り物の対応服" },
            Local = new Core.Models.LocalBlock
            {
                Avatars =
                [
                    new Core.Models.AvatarLink { AvatarItemId = "9700001", Confirmed = true },
                    new Core.Models.AvatarLink { AvatarItemId = "9700002", Confirmed = true },
                ],
            },
        }).GetAwaiter().GetResult();
        store.Modifications.SaveAsync(new Core.Models.ModificationRecord { Id = "mod-0001", AvatarItemId = "9700001", Name = "作り物の改変" }).GetAwaiter().GetResult();
        store.Modifications.SaveAsync(new Core.Models.ModificationRecord { Id = "mod-0002", AvatarItemId = "9700002", Name = "作り物の隠した改変" }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task 表示する商品と改変と共通素体から_隠したアバターの参照を落とし_落とした数を添える()
    {
        var (storeRoot, port) = await _harness.StartApiAsync("store-a", showAdult: false, fill: FillHiddenAvatarRefs);
        _harness.Announce(storeRoot, port, 101);
        var client = await _harness.ConnectAsync(_harness.Options());
        _ = _harness.Approvals.ApproveNextAsync();

        void AssertNoHidden(string text)
        {
            Assert.DoesNotContain("9700002", text);
            Assert.DoesNotContain("隠した", text);
            Assert.DoesNotContain("9700003", text);
            Assert.DoesNotContain("大人", text);
        }

        // 検索の要約の avatarIds（ID の文字列の配列）
        var (isError, text) = await McpHarness.CallAsync(client, "search_items", new { query = "対応服" });
        Assert.False(isError, text);
        AssertNoHidden(text);
        var item = Parse(text)["items"]!.AsArray().Single()!;
        Assert.Equal(["9700001"], item["avatarIds"]!.AsArray().Select(id => id!.GetValue<string>()));
        Assert.Equal(1, Parse(text)["omittedByDisplaySettings"]!.GetValue<int>());

        // 商品1件の compatibleAvatars と avatarLinks.avatars（ID と名前の組）
        (isError, text) = await McpHarness.CallAsync(client, "get_item", new { id = "9700004", include = new[] { "avatarLinks" } });
        Assert.False(isError, text);
        AssertNoHidden(text);
        var detail = Parse(text);
        Assert.Equal(["9700001"], detail["compatibleAvatars"]!.AsArray().Select(avatar => avatar!["avatarId"]!.GetValue<string>()));
        Assert.Equal(["9700001"], detail["avatarLinks"]!["avatars"]!.AsArray().Select(avatar => avatar!["avatarId"]!.GetValue<string>()));
        Assert.Equal(2, detail["omittedByDisplaySettings"]!.GetValue<int>());

        // 改変の avatarId・avatarName：一覧は行ごと、1件は断る
        (isError, text) = await McpHarness.CallAsync(client, "list_modifications");
        Assert.False(isError, text);
        AssertNoHidden(text);
        Assert.Equal(["mod-0001"], Parse(text)["items"]!.AsArray().Select(modification => modification!["id"]!.GetValue<string>()));
        Assert.Equal(1, Parse(text)["omittedByDisplaySettings"]!.GetValue<int>());
        (isError, text) = await McpHarness.CallAsync(client, "list_modifications", new { id = "mod-0002" });
        Assert.True(isError);
        Assert.Contains("隠している", text);
        Assert.DoesNotContain("9700002", text);

        // 共通素体の members と itemId：見せてよい仲間が残る素体は参照だけ、残らない素体は行ごと
        (isError, text) = await McpHarness.CallAsync(client, "list_avatar_bases");
        Assert.False(isError, text);
        AssertNoHidden(text);
        var bases = Parse(text);
        var entry = bases["items"]!.AsArray().Single()!;
        Assert.Equal("作り物の素体", entry["name"]!.GetValue<string>());
        Assert.Equal(["9700001"], entry["members"]!.AsArray().Select(id => id!.GetValue<string>()));
        Assert.Null(entry["itemId"]);
        Assert.Equal(3, bases["omittedByDisplaySettings"]!.GetValue<int>());
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
