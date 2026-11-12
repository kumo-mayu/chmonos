using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Chmonos.Query;
using Microsoft.AspNetCore.Builder;

namespace Chmonos.Api.Tests;

/// <summary>
/// ソフトが足す項目（plan 44 の②・docs/spec/plugins-menu.md）：宣言の検証・控え・変わったら聞き直し（許す・拒否・同じなら聞かない・消えるだけは聞かない）・
/// 押せる条件・応答していない・押したら出来事と needs の中身・結果を返す口・取り消したソフト・止めたソフト。名前・ID は作り物
/// </summary>
public sealed class PluginMenuTests : IAsyncLifetime
{
    private const string LegacyToken = "test-token-menu-0123456789";
    private const string ToolToken = "tool-token-menu-0123456789";
    private const string OtherToken = "other-token-menu-0123456789";
    private const string WithPackage = "9600301";
    private const string WithoutFiles = "9600302";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-api-tests", Guid.NewGuid().ToString("N"));
    private readonly PluginEventHub _hub;
    private readonly MenuApprovals _approvals = new();
    private WebApplication? _app;
    private DataStore _store = null!;
    private HttpClient _client = null!;
    private DateTimeOffset _now = new(2026, 11, 12, 9, 0, 0, TimeSpan.FromHours(9));

    public PluginMenuTests() => _hub = new PluginEventHub(() => _now);

    public async Task InitializeAsync()
    {
        _store = new DataStore(new AppPaths(_root));
        JsonStore.Write(_store.Paths.ApiSettingsFile, new ApiSettings { Enabled = true });
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = WithPackage,
            Booth = new BoothBlock { Name = "作り物の上着" },
            Local = new LocalBlock
            {
                LocalFiles = [new LocalFileRecord { Hash = new string('A', 64), Paths = [@"D:\作り物\coat.unitypackage"], SizeBytes = 10 }],
                UserTags = [new UserTagAssignment { Top = "服", Subs = ["上着"] }],
            },
        });
        await _store.Items.SaveAsync(new ItemRecord { Id = WithoutFiles, Booth = new BoothBlock { Name = "作り物の帽子" } });
        JsonStore.Write(_store.Paths.ApiClientsFile, new ApiClientList
        {
            Clients =
            [
                new ApiClientRecord { Id = "tool", Name = "作り物の道具", ExePath = @"C:\作り物\Tool.exe", TokenHash = ApiClientBook.HashOf(ToolToken), GrantedAt = _now },
                new ApiClientRecord { Id = "other", Name = "別の道具", TokenHash = ApiClientBook.HashOf(OtherToken), GrantedAt = _now },
            ],
        });

        var writes = new ApiWriteHooks((_, _) => Task.FromResult<Core.Commands.CommandResult>(new Core.Commands.CommandResult.Done()), Approvals: _approvals);
        (_app, _, _) = await ApiHost.BuildAsync(new ApiHostOptions(
            _root, 0, LegacyToken, [], Clock: () => _now, Watch: false, Store: _store, Writes: writes, Plugins: _hub));
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{new Uri(_app.Urls.Single()).Port}") };
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, string? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PutMenuAsync(object body, string token = ToolToken)
        => SendAsync(HttpMethod.Put, "/v1/plugin/menu", token, JsonSerializer.Serialize(body));

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> CodeOf(HttpResponseMessage response) => (await JsonOf(response)).GetProperty("code").GetString();

    private static readonly object Preview = new
    {
        id = "preview", label = "衣装をプレビュー", where = new[] { "selection", "item" },
        when = new { hasUnityPackage = true }, needs = new[] { "name", "itemId", "files", "unityPackages", "avatars", "tags" },
    };

    private static readonly object Export = new
    {
        id = "export", label = "書き出す",
        children = new[] { new { id = "export-csv", label = "CSV", where = new[] { "selection" }, needs = new[] { "itemId", "name" } } },
    };

    /// <summary>最初の宣言を許して控えに書かせる。</summary>
    private async Task ApproveFirstAsync(params object[] items)
    {
        var put = PutMenuAsync(new { items });
        var asked = await _approvals.NextAsync();
        asked.Answer(new ApiClientDecision(true, []));
        Assert.Equal(HttpStatusCode.Accepted, (await put).StatusCode);
        await WaitUntilAsync(() => _hub.MenusFor(PluginMenus.WhereItem, []).Count + _hub.MenusFor(PluginMenus.WhereSelection, []).Count > 0);
    }

    private static async Task WaitUntilAsync(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++)
        {
            await Task.Delay(25);
        }

        Assert.True(done());
    }

    /// <summary>ソフトが取りに来た印（待たずに1回聞く）。</summary>
    private async Task PollAsync(string token = ToolToken)
        => Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/v1/plugin/events?after=0&wait=0", token)).StatusCode);

    [Theory]
    [InlineData("""{"items":[{"id":"a","label":"A","where":["file"]}]}""", "unknownWhere")]
    [InlineData("""{"items":[{"id":"a","label":"A","where":["item"],"needs":["modification"]}]}""", "unknownNeed")]
    [InlineData("""{"items":[{"id":"a","label":"A","where":["item"],"when":{"isAdult":true}}]}""", "unknownWhen")]
    [InlineData("""{"items":[{"id":"a","label":"A","where":["item"],"when":{"owned":"yes"}}]}""", "invalidWhen")]
    [InlineData("""{"items":[{"id":"a","label":"A"}]}""", "invalidWhere")]
    [InlineData("""{"items":[{"id":"a b","label":"A","where":["item"]}]}""", "invalidId")]
    [InlineData("""{"items":[{"id":"a","label":"改行\nあり","where":["item"]}]}""", "invalidLabel")]
    [InlineData("""{"items":[{"id":"a","label":"A","where":["item"]},{"id":"a","label":"B","where":["item"]}]}""", "duplicateId")]
    [InlineData("""{"items":[{"id":"a","label":"A","children":[{"id":"b","label":"B","children":[{"id":"c","label":"C","where":["item"]}]}]}]}""", "tooDeep")]
    [InlineData("""{"items":[{"id":"a","label":"A","needs":["name"],"children":[{"id":"b","label":"B","where":["item"]}]}]}""", "invalidItem")]
    [InlineData("""{"menu":[]}""", "invalidRequest")]
    public async Task 合わない宣言は400で断り_控えに書かない(string body, string code)
    {
        var response = await SendAsync(HttpMethod.Put, "/v1/plugin/menu", ToolToken, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await CodeOf(response));
        Assert.Equal(0, _approvals.AskedCount);
    }

    [Fact]
    public async Task 項目の数とラベルの長さに上限がある()
    {
        var many = Enumerable.Range(0, PluginMenus.MaxTopItems + 1).Select(i => new { id = $"a{i}", label = "A", where = new[] { "item" } }).ToArray();
        Assert.Equal("tooManyItems", await CodeOf(await PutMenuAsync(new { items = many })));

        var deep = Enumerable.Range(0, 3).Select(i => new
        {
            id = $"p{i}", label = "P",
            children = Enumerable.Range(0, 11).Select(j => new { id = $"c{i}-{j}", label = "C", where = new[] { "item" } }).ToArray(),
        }).ToArray();
        Assert.Equal("tooManyItems", await CodeOf(await PutMenuAsync(new { items = deep })));

        var longLabel = new { id = "a", label = new string('長', PluginMenus.MaxLabelLength + 1), where = new[] { "item" } };
        Assert.Equal("invalidLabel", await CodeOf(await PutMenuAsync(new { items = new[] { longLabel } })));
    }

    [Fact]
    public async Task 古いトークンでは宣言できない()
    {
        var response = await PutMenuAsync(new { items = new[] { Preview } }, LegacyToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("clientTokenRequired", await CodeOf(response));
    }

    [Fact]
    public async Task 最初の宣言は人に聞き_許すまでは出さず_許したら控えに書いて出す()
    {
        var put = PutMenuAsync(new { items = new[] { Preview, Export } });
        var asked = await _approvals.NextAsync();

        // 聞いている間は出さない。GET は pending と聞いている宣言を返す
        Assert.Empty(_hub.MenusFor(PluginMenus.WhereItem, []));
        var pending = await JsonOf(await SendAsync(HttpMethod.Get, "/v1/plugin/menu", ToolToken));
        Assert.Equal("pending", pending.GetProperty("state").GetString());
        Assert.Equal(2, pending.GetProperty("pending").GetArrayLength());
        Assert.True(asked.Info.IsFirst);
        Assert.Equal(["preview", "export-csv"], asked.Info.Change.Added.Select(line => line.Id));
        Assert.Equal("書き出す", asked.Info.Change.Added[1].Parent);
        Assert.Equal(@"C:\作り物\Tool.exe", asked.Info.ExePath);

        var answer = await put;
        Assert.Equal(HttpStatusCode.Accepted, answer.StatusCode);
        Assert.Equal("pending", (await JsonOf(answer)).GetProperty("state").GetString());

        asked.Answer(new ApiClientDecision(true, []));
        await WaitUntilAsync(() => _hub.MenusFor(PluginMenus.WhereItem, []).Count == 1);

        var stored = _store.ApiClients.Load().Clients.Single(client => client.Id == "tool").Menu!;
        Assert.Equal(["preview", "export"], stored.Select(item => item.Id));

        // where・needs は決まった順に整えて控える（順の違いで「変わった」にしない）
        Assert.Equal(["item", "selection"], stored[0].Where);
        Assert.Equal(["itemId", "name", "files", "unityPackages", "avatars", "tags"], stored[0].Needs);
        var approved = await JsonOf(await SendAsync(HttpMethod.Get, "/v1/plugin/menu", ToolToken));
        Assert.Equal("approved", approved.GetProperty("state").GetString());

        // 出す場所ごとに分ける（選択にだけ出す項目は、商品の右クリックに出さない）
        var item = Assert.Single(_hub.MenusFor(PluginMenus.WhereItem, []));
        Assert.Equal(["preview"], item.Items.Select(entry => entry.Id));
        var selection = Assert.Single(_hub.MenusFor(PluginMenus.WhereSelection, []));
        Assert.Equal(["preview", "export"], selection.Items.Select(entry => entry.Id));
        Assert.Equal(["export-csv"], selection.Items[1].Children.Select(entry => entry.Id));
    }

    [Fact]
    public async Task 同じ宣言を送り直しても聞かない_順が違うだけでも同じ()
    {
        await ApproveFirstAsync(Preview);

        var reordered = new
        {
            id = "preview", label = "衣装をプレビュー", where = new[] { "item", "selection" },
            when = new { hasUnityPackage = true }, needs = new[] { "tags", "avatars", "unityPackages", "files", "itemId", "name" },
        };
        var response = await PutMenuAsync(new { items = new[] { reordered } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("approved", (await JsonOf(response)).GetProperty("state").GetString());
        Assert.Equal(1, _approvals.AskedCount);
    }

    [Fact]
    public async Task 変えたら聞き直し_拒否なら前の宣言のまま_回数を数える()
    {
        await ApproveFirstAsync(Preview);

        var changed = new { id = "preview", label = "プレビューする", where = new[] { "item" }, needs = new[] { "itemId" } };
        var put = PutMenuAsync(new { items = new object[] { changed, new { id = "info", label = "情報", where = new[] { "item" } } } });
        var asked = await _approvals.NextAsync();
        Assert.False(asked.Info.IsFirst);
        Assert.Equal(["info"], asked.Info.Change.Added.Select(line => line.Id));
        Assert.Equal(["preview"], asked.Info.Change.Changed.Select(line => line.Id));
        Assert.Equal(HttpStatusCode.Accepted, (await put).StatusCode);

        asked.Answer(ApiClientDecision.Refusal);
        await WaitUntilAsync(() => _store.ApiClients.Load().Refusals.Count == 1);

        Assert.Equal("衣装をプレビュー", Assert.Single(Assert.Single(_hub.MenusFor(PluginMenus.WhereItem, [])).Items).Label);
        Assert.Equal(1, _store.ApiClients.Load().Refusals.Single().Count);
        var state = await JsonOf(await SendAsync(HttpMethod.Get, "/v1/plugin/menu", ToolToken));
        Assert.Equal("denied", state.GetProperty("state").GetString());
    }

    [Fact]
    public async Task 閉じただけでは回数を数えず_拒否が続いて止めたら窓を出さずに断り項目も出さない()
    {
        await ApproveFirstAsync(Preview);
        var changed = new { id = "preview", label = "プレビューする", where = new[] { "item" } };

        var put = PutMenuAsync(new { items = new[] { changed } });
        (await _approvals.NextAsync()).Answer(ApiClientDecision.Dismissed);
        await put;
        await WaitUntilAsync(() => _approvals.Open == 0);
        Assert.Empty(_store.ApiClients.Load().Refusals);

        for (var i = 0; i < ApiClientList.DefaultRefusalLimit; i++)
        {
            var again = PutMenuAsync(new { items = new[] { changed } });
            (await _approvals.NextAsync()).Answer(ApiClientDecision.Refusal);
            await again;
            await WaitUntilAsync(() => _store.ApiClients.Load().Refusals.SingleOrDefault()?.Count == i + 1);
        }

        await WaitUntilAsync(() => _hub.MenusFor(PluginMenus.WhereItem, []).Count == 0);
        var blocked = await PutMenuAsync(new { items = new[] { changed } });
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("requestBlocked", await CodeOf(blocked));
    }

    [Fact]
    public async Task 窓を待っている間に控えと同じ宣言へ戻したら_取り下げて後で聞かない()
    {
        await ApproveFirstAsync(Preview);

        // 別のソフトの窓が開いている間に、変えた宣言を送ってから、控えと同じ宣言を送り直す
        var otherPut = PutMenuAsync(new { items = new[] { Export } }, OtherToken);
        var otherAsked = await _approvals.NextAsync();
        var changed = new { id = "preview", label = "プレビューする", where = new[] { "item" } };
        Assert.Equal(HttpStatusCode.Accepted, (await PutMenuAsync(new { items = new[] { changed } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PutMenuAsync(new { items = new[] { Preview } })).StatusCode);

        otherAsked.Answer(new ApiClientDecision(true, []));
        await otherPut;
        await WaitUntilAsync(() => _hub.MenusFor(PluginMenus.WhereSelection, []).Count == 2);
        await Task.Delay(300);

        Assert.Equal(2, _approvals.AskedCount);
        Assert.Equal("衣装をプレビュー", _hub.MenusFor(PluginMenus.WhereItem, [])[0].Items[0].Label);
        var state = await JsonOf(await SendAsync(HttpMethod.Get, "/v1/plugin/menu", ToolToken));
        Assert.Equal("approved", state.GetProperty("state").GetString());
    }

    [Fact]
    public async Task 消えるだけなら聞かずに効かせる()
    {
        await ApproveFirstAsync(Preview, Export);

        var response = await PutMenuAsync(new { items = new[] { Export } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _approvals.AskedCount);
        Assert.Empty(_hub.MenusFor(PluginMenus.WhereItem, []));

        Assert.Equal(HttpStatusCode.OK, (await PutMenuAsync(new { items = Array.Empty<object>() })).StatusCode);
        Assert.Null(_store.ApiClients.Load().Clients.Single(client => client.Id == "tool").Menu);
    }

    [Fact]
    public async Task 取りに来ていなければ応答していない_待っている間と取りに来た後の時間内は押せる()
    {
        await ApproveFirstAsync(Preview);

        Assert.Equal(PluginMenuBlock.NotResponding, Assert.Single(_hub.MenusFor(PluginMenus.WhereItem, [WithPackage])).Reason.Block);

        await PollAsync();
        var software = Assert.Single(_hub.MenusFor(PluginMenus.WhereItem, [WithPackage]));
        Assert.True(software.Reason.IsOk);
        Assert.True(software.Items[0].Reason.IsOk);

        _now += PluginEventHub.RespondWindow + TimeSpan.FromSeconds(1);
        Assert.Equal(PluginMenuBlock.NotResponding, Assert.Single(_hub.MenusFor(PluginMenus.WhereItem, [WithPackage])).Items[0].Reason.Block);
        Assert.False((await _hub.SendMenuAsync("tool", "preview", PluginMenus.WhereItem, [WithPackage])).Sent);

        // 長く待っている間は、時間が過ぎても応答中
        var waiting = SendAsync(HttpMethod.Get, "/v1/plugin/events?after=0&wait=5", ToolToken);
        await WaitUntilAsync(() => _hub.IsResponding("tool"));
        await waiting;
    }

    [Fact]
    public async Task 条件に合わない商品では押せず_選択は全部が合うときだけ押せる()
    {
        await ApproveFirstAsync(Preview);
        await PollAsync();

        Assert.Equal(PluginMenuBlock.NoUnityPackage, _hub.MenusFor(PluginMenus.WhereItem, [WithoutFiles])[0].Items[0].Reason.Block);
        Assert.Equal(PluginMenuBlock.NoUnityPackage, _hub.MenusFor(PluginMenus.WhereSelection, [WithPackage, WithoutFiles])[0].Items[0].Reason.Block);
        Assert.True(_hub.MenusFor(PluginMenus.WhereSelection, [WithPackage])[0].Items[0].Reason.IsOk);
        Assert.Equal(PluginMenuBlock.NotLoaded, _hub.MenusFor(PluginMenus.WhereItem, ["9699999"])[0].Items[0].Reason.Block);
        var tooMany = Enumerable.Repeat(WithPackage, PluginMenus.MaxTargets + 1).ToList();
        Assert.Equal(PluginMenuBlock.TooMany, _hub.MenusFor(PluginMenus.WhereSelection, tooMany)[0].Items[0].Reason.Block);
        Assert.False((await _hub.SendMenuAsync("tool", "preview", PluginMenus.WhereItem, [WithoutFiles])).Sent);
    }

    [Theory]
    [InlineData("owned", true, WithoutFiles, PluginMenuBlock.NotOwned)]
    [InlineData("owned", false, WithPackage, PluginMenuBlock.Owned)]
    [InlineData("hasAvatar", true, WithPackage, PluginMenuBlock.NoAvatar)]
    [InlineData("hasUnityPackage", false, WithPackage, PluginMenuBlock.HasUnityPackage)]
    public void 条件ごとに理由を分ける(string key, bool value, string itemId, PluginMenuBlock expected)
    {
        var snapshot = QuerySnapshot.LoadAsync(_store).GetAwaiter().GetResult();
        var when = key switch
        {
            "owned" => new PluginMenuWhenRef(null, value, null, null),
            "hasAvatar" => new PluginMenuWhenRef(null, null, value, null),
            _ => new PluginMenuWhenRef(value, null, null, null),
        };

        Assert.Equal(expected, PluginMenus.Check(when, [itemId], snapshot).Block);
        Assert.Equal(PluginMenuBlock.OtherCategory, PluginMenus.Check(new PluginMenuWhenRef(null, null, null, "3Dモデル"), [itemId], snapshot).Block);
    }

    [Fact]
    public async Task 押したら購読していなくても出来事を積み_needs_の欄だけ読み取りと同じ形で詰める()
    {
        await ApproveFirstAsync(Preview, Export);
        await PollAsync();

        var sent = await _hub.SendMenuAsync("tool", "preview", PluginMenus.WhereItem, [WithPackage]);
        Assert.True(sent.Sent);

        var answer = await JsonOf(await SendAsync(HttpMethod.Get, "/v1/plugin/events?after=0&wait=0", ToolToken));
        var pressed = Assert.Single(answer.GetProperty("events").EnumerateArray());
        Assert.Equal("menu", pressed.GetProperty("kind").GetString());
        Assert.Equal(sent.Seq, pressed.GetProperty("seq").GetInt64());
        Assert.Equal(JsonValueKind.Null, pressed.GetProperty("itemOpened").ValueKind);
        var menu = pressed.GetProperty("menu");
        Assert.Equal("preview", menu.GetProperty("id").GetString());
        Assert.Equal("item", menu.GetProperty("where").GetString());
        var target = Assert.Single(menu.GetProperty("items").EnumerateArray());
        Assert.Equal(WithPackage, target.GetProperty("itemId").GetString());
        Assert.Equal("作り物の上着", target.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, target.GetProperty("shop").ValueKind);
        Assert.Equal("coat.unitypackage", target.GetProperty("unityPackages")[0].GetProperty("entry").GetString());
        Assert.Equal("服", target.GetProperty("tags")[0].GetProperty("top").GetString());

        // files は /v1/items/{id} の files と同じ JSON
        var detail = await JsonOf(await SendAsync(HttpMethod.Get, $"/v1/items/{WithPackage}", ToolToken));
        Assert.Equal(detail.GetProperty("files").GetRawText(), target.GetProperty("files").GetRawText());
        Assert.Equal(detail.GetProperty("folders").GetRawText(), target.GetProperty("folders").GetRawText());

        // 選択の項目：needs が itemId・name だけなら、ほかの欄は null
        var csv = await _hub.SendMenuAsync("tool", "export-csv", PluginMenus.WhereSelection, [WithoutFiles, WithPackage]);
        var next = await JsonOf(await SendAsync(HttpMethod.Get, $"/v1/plugin/events?after={sent.Seq}&wait=0", ToolToken));
        var items = Assert.Single(next.GetProperty("events").EnumerateArray()).GetProperty("menu").GetProperty("items");
        Assert.Equal([WithoutFiles, WithPackage], items.EnumerateArray().Select(item => item.GetProperty("itemId").GetString()));
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("files").ValueKind);
        Assert.True(csv.Sent);

        // 別のソフトの列には積まない。宣言に無い項目・場所の違う項目は送らない
        var other = await JsonOf(await SendAsync(HttpMethod.Get, "/v1/plugin/events?after=0&wait=0", OtherToken));
        Assert.Empty(other.GetProperty("events").EnumerateArray());
        Assert.Equal(PluginMenuBlock.Gone, (await _hub.SendMenuAsync("tool", "export-csv", PluginMenus.WhereItem, [WithPackage])).Reason.Block);
    }

    [Fact]
    public async Task 結果は自分の押した出来事にだけ受け_文を切って知らせる()
    {
        await ApproveFirstAsync(Preview);
        await PollAsync();
        await SendAsync(HttpMethod.Get, "/v1/plugin/events?after=0&wait=0", OtherToken);
        var sent = await _hub.SendMenuAsync("tool", "preview", PluginMenus.WhereItem, [WithPackage]);
        var results = new List<PluginMenuResult>();
        _hub.MenuResult += results.Add;

        var longText = new string('済', PluginEventHub.MaxResultLength + 20);
        var ok = await SendAsync(HttpMethod.Post, $"/v1/plugin/events/{sent.Seq}/result", ToolToken, JsonSerializer.Serialize(new { ok = true, message = longText, epoch = _hub.Epoch }));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var result = Assert.Single(results);
        Assert.Equal(PluginEventHub.MaxResultLength, result.Message.Length);
        Assert.EndsWith("…", result.Message);
        Assert.Equal("作り物の道具", result.Name);

        // 制御文字・ok の無い本文は断る
        Assert.Equal("invalidMessage", await CodeOf(await SendAsync(HttpMethod.Post, $"/v1/plugin/events/{sent.Seq}/result", ToolToken, """{"ok":true,"message":"改行\nあり"}""")));
        Assert.Equal("invalidRequest", await CodeOf(await SendAsync(HttpMethod.Post, $"/v1/plugin/events/{sent.Seq}/result", ToolToken, """{"message":"済み"}""")));

        // 別のソフト・無い通番・前の起動の名前は 404
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, $"/v1/plugin/events/{sent.Seq}/result", OtherToken, """{"ok":true,"message":"済み"}""")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, $"/v1/plugin/events/{sent.Seq + 5}/result", ToolToken, """{"ok":true,"message":"済み"}""")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, $"/v1/plugin/events/{sent.Seq}/result", ToolToken, """{"ok":true,"message":"済み","epoch":"0000"}""")).StatusCode);

        // 時間が過ぎた結果は受けない
        _now += PluginEventHub.KeepSendsFor + TimeSpan.FromSeconds(1);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, $"/v1/plugin/events/{sent.Seq}/result", ToolToken, """{"ok":false,"message":"遅い"}""")).StatusCode);
        Assert.Single(results);
    }

    [Fact]
    public async Task 取り消したソフトの項目は出さず_送れない()
    {
        await ApproveFirstAsync(Preview);
        await PollAsync();

        await _store.ApiClients.UpdateAsync(list => list with { Clients = list.Clients.Where(client => client.Id != "tool").ToList() });
        ((ApiState)_app!.Services.GetService(typeof(ApiState))!).Clients.Reload();

        Assert.Empty(_hub.MenusFor(PluginMenus.WhereItem, [WithPackage]));
        Assert.False((await _hub.SendMenuAsync("tool", "preview", PluginMenus.WhereItem, [WithPackage])).Sent);
    }

    [Fact]
    public void 控えを手で書き換えた合わない宣言は出さない()
    {
        var records = new[] { new PluginMenuItemRecord { Id = "x", Label = "X", Where = ["file"] } };
        Assert.Empty(PluginMenus.FromRecords(records));
        Assert.Single(PluginMenus.FromRecords([new PluginMenuItemRecord { Id = "x", Label = "X", Where = ["item"] }]));
    }

    /// <summary>作り物の聞く口：宣言の聞き直しを並べ、試験が答えるまで待つ。</summary>
    private sealed class MenuApprovals : IApiClientApprovals
    {
        private readonly System.Threading.Channels.Channel<Asked> _asked = System.Threading.Channels.Channel.CreateUnbounded<Asked>();

        public bool CanAsk => true;

        public int AskedCount;

        public int Open;

        public Task<ApiClientDecision> AskAsync(ApiClientRequestInfo request, CancellationToken cancellationToken)
            => Task.FromResult(ApiClientDecision.Dismissed);

        public async Task<ApiClientDecision> AskMenuAsync(PluginMenuRequestInfo request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref AskedCount);
            Interlocked.Increment(ref Open);
            try
            {
                var asked = new Asked(request);
                await _asked.Writer.WriteAsync(asked, cancellationToken);
                return await asked.Decision.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref Open);
            }
        }

        public async Task<Asked> NextAsync() => await _asked.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class Asked(PluginMenuRequestInfo info)
    {
        public PluginMenuRequestInfo Info { get; } = info;

        public TaskCompletionSource<ApiClientDecision> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Answer(ApiClientDecision decision) => Decision.SetResult(decision);
    }
}
