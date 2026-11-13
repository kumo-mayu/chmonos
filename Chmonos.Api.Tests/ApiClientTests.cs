using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Microsoft.AspNetCore.Builder;

namespace Chmonos.Api.Tests;

/// <summary>
/// ソフトごとの許し（plan 31・docs/spec/api-clients.md）：求める・断る・許す・権限ごとに書ける／書けない・取り消し・古いトークン・
/// 画面が無いときは断る・exe のパスの引き方。人に聞く口と exe を引く口は作り物。名前・パスは作り物
/// </summary>
public sealed class ApiClientTests : IAsyncLifetime
{
    private const string LegacyToken = "test-token-clients-0123456789";
    private const string ItemId = "9600101";
    private const string FakeExe = @"C:\作り物\道具\Tool.exe";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-api-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeApprovals _approvals = new();
    private readonly List<string?> _callerLabels = [];
    private WebApplication? _app;
    private ApiState _state = null!;
    private StoreSync _sync = null!;
    private DataStore _store = null!;
    private HttpClient _client = null!;
    private DateTimeOffset _now = new(2026, 11, 11, 9, 0, 0, TimeSpan.FromHours(9));

    public Task InitializeAsync() => Task.CompletedTask;

    private async Task StartAsync(bool allowWrites = false, bool approvals = true, string[]? origins = null, IConnectionOwners? owners = null)
    {
        _store = new DataStore(new AppPaths(_root));
        await _store.Items.SaveAsync(new ItemRecord { Id = ItemId, Booth = new BoothBlock { Name = "作り物の帽子" } });
        JsonStore.Write(_store.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, AllowWrites = allowWrites, AllowedOrigins = origins ?? [] });

        var writes = new ApiWriteHooks(ExecuteAsync, ClaimItem: _ => new CancellationTokenSource(), Approvals: approvals ? _approvals : null);
        (_app, _state, _sync) = await ApiHost.BuildAsync(new ApiHostOptions(
            _root, 0, LegacyToken, origins ?? [], Clock: () => _now, Watch: false, Store: _store, Writes: writes, Owners: owners ?? new FakeOwners(FakeExe)));
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{new Uri(_app.Urls.Single()).Port}") };
    }

    private async Task<CommandResult> ExecuteAsync(UiCommand command, CancellationToken cancellationToken)
    {
        lock (_callerLabels)
        {
            // 命令の口の中で、どのソフトの要求かが見えるか（ログの1行に添える名前）
            _callerLabels.Add(ApiCaller.Label);
        }

        if (command is UiCommand.SaveItemLocal save)
        {
            await _store.Items.SaveLocalAsync(save.ItemId, save.Local, save.Owns, cancellationToken: cancellationToken);
            return new CommandResult.Done();
        }

        // 一覧に足す命令などは、守りを通ったかだけを見る（中身は Core の試験が見る）
        return new CommandResult.Done();
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

    private static StringContent Json(string text) => new(text, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> CodeOf(HttpResponseMessage response) => (await BodyOf(response)).GetProperty("code").GetString();

    private Task<HttpResponseMessage> RequestAsync(string name, params string[] scopes)
        => _client.PostAsync("/v1/clients/request", Json(JsonSerializer.Serialize(new { name, scopes })));

    private async Task<string> RequestIdAsync(string name, params string[] scopes)
    {
        var response = await RequestAsync(name, scopes);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await BodyOf(response)).GetProperty("requestId").GetString()!;
    }

    private async Task<JsonElement> ResultAsync(string requestId, int wait = 5)
    {
        var response = await _client.PostAsync("/v1/clients/request/result", Json(JsonSerializer.Serialize(new { requestId, wait })));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return await BodyOf(response);
    }

    /// <summary>許しを求め、人が <paramref name="granted"/> を選んで許し、トークンを受け取る。</summary>
    private async Task<string> GrantAsync(string name, string[] requested, string[] granted)
    {
        var requestId = await RequestIdAsync(name, requested);
        (await _approvals.NextAsync()).Answer(new ApiClientDecision(true, granted));
        var result = await ResultAsync(requestId);
        Assert.Equal("approved", result.GetProperty("state").GetString());
        return result.GetProperty("token").GetString()!;
    }

    private HttpRequestMessage With(HttpMethod method, string path, string token, string? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = Json(body);
        }

        return request;
    }

    private Task<HttpResponseMessage> PutFavoriteAsync(string token)
        => _client.SendAsync(With(HttpMethod.Put, $"/v1/items/{ItemId}/favorite", token, """{"value":true}"""));

    private Task<HttpResponseMessage> AddUserTagAsync(string token)
        => _client.SendAsync(With(HttpMethod.Post, "/v1/user-tags", token, """{"top":"作り物の分類"}"""));

    [Fact]
    public async Task 許すと_そのソフトだけのトークンを1回だけ返し_控えにはハッシュだけ書く()
    {
        await StartAsync();
        var requestId = await RequestIdAsync("作り物の道具", "marks");

        var asked = await _approvals.NextAsync();
        Assert.Equal(("作り物の道具", FakeExe, (string?)null, false), (asked.Info.Name, asked.Info.ExePath, asked.Info.Origin, asked.Info.Replaces));
        Assert.Equal([ApiScopes.Marks], asked.Info.Scopes);

        // 決まる前は pending（待たずに聞くと、すぐ答える）
        Assert.Equal("pending", (await ResultAsync(requestId, wait: 0)).GetProperty("state").GetString());
        asked.Answer(new ApiClientDecision(true, [ApiScopes.Marks]));

        var first = await ResultAsync(requestId);
        var token = first.GetProperty("token").GetString()!;
        Assert.Equal("approved", first.GetProperty("state").GetString());
        Assert.False(string.IsNullOrEmpty(token));

        var second = await ResultAsync(requestId);
        Assert.Equal("delivered", second.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("token").ValueKind);

        var text = await File.ReadAllTextAsync(_store.Paths.ApiClientsFile);
        Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        var saved = Assert.Single(_store.ApiClients.Load().Clients);
        Assert.Equal(ApiClientBook.HashOf(token), saved.TokenHash);
        Assert.Equal(("作り物の道具", FakeExe), (saved.Name, saved.ExePath));
        Assert.Equal([ApiScopes.Marks], saved.Scopes);

        // 読むのはいつもできる
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(With(HttpMethod.Get, $"/v1/items/{ItemId}", token))).StatusCode);
    }

    [Fact]
    public async Task 拒否すると_トークンを返さず_控えにも書かない()
    {
        await StartAsync();
        var requestId = await RequestIdAsync("作り物の道具", "marks");
        (await _approvals.NextAsync()).Answer(ApiClientDecision.Refusal);

        var result = await ResultAsync(requestId);
        Assert.Equal("denied", result.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("token").ValueKind);
        Assert.Empty(_store.ApiClients.Load().Clients);
    }

    [Fact]
    public async Task ソフトのトークンは_許した種類だけ書け_allowWritesに左右されない()
    {
        await StartAsync(allowWrites: false);
        var token = await GrantAsync("作り物の道具", ["marks", "tags"], [ApiScopes.Marks]);

        Assert.Equal(HttpStatusCode.OK, (await PutFavoriteAsync(token)).StatusCode);
        Assert.Contains("ソフト「作り物の道具」", _callerLabels);

        // 求めたが人が外した種類は書けない
        var tags = await AddUserTagAsync(token);
        Assert.Equal(HttpStatusCode.Forbidden, tags.StatusCode);
        Assert.Equal("scopeNotGranted", await CodeOf(tags));

        var me = await BodyOf(await _client.SendAsync(With(HttpMethod.Get, "/v1/clients/me", token)));
        Assert.Equal("client", me.GetProperty("kind").GetString());
        Assert.Equal(["read", "marks"], me.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()));
    }

    [Fact]
    public async Task 読むだけのソフトは_どの書き込みも断られる()
    {
        await StartAsync(allowWrites: true);
        var token = await GrantAsync("読むだけの道具", [], []);

        var favorite = await PutFavoriteAsync(token);
        Assert.Equal(HttpStatusCode.Forbidden, favorite.StatusCode);
        Assert.Equal("scopeNotGranted", await CodeOf(favorite));
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(With(HttpMethod.Post, $"/v1/items/{ItemId}/show", token))).StatusCode);
        Assert.False((await _store.Items.LoadAsync(ItemId))!.Local.IsFavorite);
    }

    [Fact]
    public async Task 古いトークンは今までどおり_allowWritesで決まる()
    {
        await StartAsync(allowWrites: false);
        var off = await PutFavoriteAsync(LegacyToken);
        Assert.Equal(HttpStatusCode.Forbidden, off.StatusCode);
        Assert.Equal("writesDisabled", await CodeOf(off));
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(With(HttpMethod.Get, $"/v1/items/{ItemId}", LegacyToken))).StatusCode);

        JsonStore.Write(_store.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, AllowWrites = true });
        _state.ApplySettings();
        Assert.Equal(HttpStatusCode.OK, (await PutFavoriteAsync(LegacyToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AddUserTagAsync(LegacyToken)).StatusCode);
        Assert.Contains("api.json のトークン", _callerLabels);

        var me = await BodyOf(await _client.SendAsync(With(HttpMethod.Get, "/v1/clients/me", LegacyToken)));
        Assert.Equal("legacy", me.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task 取り消したら_そのトークンは使えない()
    {
        await StartAsync();
        var token = await GrantAsync("作り物の道具", ["marks"], [ApiScopes.Marks]);
        Assert.Equal(HttpStatusCode.OK, (await PutFavoriteAsync(token)).StatusCode);

        // 設定の画面の取り消しと同じ書き方（命令の口は Core の試験が見る。ここは控えから消えたら効くか）
        await _store.ApiClients.UpdateAsync(list => list with { Clients = [] });
        _state.Clients.Reload();

        var revoked = await _client.SendAsync(With(HttpMethod.Get, $"/v1/items/{ItemId}", token));
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Equal("unauthorized", await CodeOf(revoked));
    }

    [Fact]
    public async Task 控えのファイルが変わったら_見張りの道で読み直す()
    {
        await StartAsync();
        var token = await GrantAsync("作り物の道具", [], []);

        // 手でファイルを直した・ほかの道で書いた：見張りが api-clients.json を拾ったときと同じ道（設定の部分の読み直し）
        await _store.ApiClients.UpdateAsync(list => list with { Clients = [] });
        _sync.Note(_store.Paths.ApiClientsFile);
        await _sync.ApplyPendingAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(With(HttpMethod.Get, $"/v1/items/{ItemId}", token))).StatusCode);
    }

    [Fact]
    public async Task 権限を外したら_その種類はすぐ書けなくなる()
    {
        await StartAsync();
        var token = await GrantAsync("作り物の道具", ["marks"], [ApiScopes.Marks]);
        await _store.ApiClients.UpdateAsync(list => list with { Clients = list.Clients.Select(client => client with { Scopes = [] }).ToList() });
        _state.Clients.Reload();

        Assert.Equal(HttpStatusCode.Forbidden, (await PutFavoriteAsync(token)).StatusCode);
    }

    [Fact]
    public async Task 画面を出さない起動では_待たせずに断る()
    {
        await StartAsync();
        _approvals.CanAsk = false;

        var response = await RequestAsync("作り物の道具", "marks");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("approvalUnavailable", await CodeOf(response));
        Assert.Equal(0, _approvals.AskedCount);
    }

    [Fact]
    public async Task 聞く口が無ければ_断る()
    {
        await StartAsync(approvals: false);
        var response = await RequestAsync("作り物の道具");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("approvalUnavailable", await CodeOf(response));
    }

    [Fact]
    public async Task 同じソフトの重ねた求めは1つにまとめ_窓は1つずつ出す()
    {
        await StartAsync();
        var first = await RequestIdAsync("作り物の道具", "marks");
        var again = await RequestIdAsync("作り物の道具", "marks");
        var other = await RequestIdAsync("別の作り物", "tags");
        Assert.Equal(first, again);
        Assert.NotEqual(first, other);

        var asked = await _approvals.NextAsync();
        Assert.Equal("作り物の道具", asked.Info.Name);

        // 1つ目に答えるまで、2つ目の窓は出ない
        await Task.Delay(200);
        Assert.Equal(1, _approvals.AskedCount);
        asked.Answer(ApiClientDecision.Refusal);

        var next = await _approvals.NextAsync();
        Assert.Equal("別の作り物", next.Info.Name);
        Assert.Equal(1, _approvals.MaxConcurrent);
        next.Answer(ApiClientDecision.Refusal);

        // 拒否の回数を書き終えるまで待つ（片付けで保存先を消すのと重ならないように）
        await ResultAsync(other);
    }

    [Fact]
    public async Task 答えていない求めが溢れたら_429で断る()
    {
        await StartAsync();
        for (var index = 0; index < ApiClientRequests.MaxPending; index++)
        {
            await RequestIdAsync($"作り物{index}");
        }

        var over = await RequestAsync("溢れた作り物");
        Assert.Equal((HttpStatusCode)429, over.StatusCode);
        Assert.Equal("tooManyRequests", await CodeOf(over));
    }

    [Fact]
    public async Task 名前と種類を確かめ_読めなければ400()
    {
        await StartAsync();
        Assert.Equal("invalidName", await CodeOf(await RequestAsync("")));
        Assert.Equal("invalidName", await CodeOf(await RequestAsync(new string('名', ApiClientEndpoints.MaxNameLength + 1))));
        Assert.Equal("invalidName", await CodeOf(await RequestAsync("改行\nあり")));
        Assert.Equal("unknownScope", await CodeOf(await RequestAsync("作り物", "everything")));

        // read は書いても書かなくてもよい（窓には出さない）
        await RequestIdAsync("作り物", "read", "unity");
        Assert.Equal([ApiScopes.Unity], (await _approvals.NextAsync()).Info.Scopes);
    }

    [Fact]
    public async Task 止めるときは_結果を待っている問い合わせを解いて_すぐ止まる()
    {
        await StartAsync();
        var requestId = await RequestIdAsync("作り物の道具", "marks");
        await _approvals.NextAsync();

        // 人が答えないまま、道具が30秒待つ問い合わせを出している
        var waiting = _client.PostAsync("/v1/clients/request/result", Json(JsonSerializer.Serialize(new { requestId, wait = 30 })));
        await Task.Delay(300);

        var watch = Stopwatch.StartNew();
        await _app!.StopAsync();
        var answer = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"止まるのに {watch.Elapsed.TotalSeconds:0.0} 秒かかりました。");
        Assert.Equal("pending", (await BodyOf(answer)).GetProperty("state").GetString());

        // API が止まって窓が閉じたのは、人の拒否ではないので数えない
        Assert.Empty(_store.ApiClients.Load().Refusals);

        await _app.DisposeAsync();
        _app = null;
    }

    private IReadOnlyList<ApiClientRefusal> Refusals => _store.ApiClients.Load().Refusals;

    /// <summary>求めて、窓に <paramref name="decision"/> で答え、結果（denied など）まで取る。窓に出た物を返す。</summary>
    private async Task<ApiClientRequestInfo> AnswerAsync(string name, ApiClientDecision decision)
    {
        var requestId = await RequestIdAsync(name, "marks");
        var asked = await _approvals.NextAsync();
        asked.Answer(decision);
        await ResultAsync(requestId);
        return asked.Info;
    }

    [Fact]
    public async Task 拒否を押すたびに数え_上限に届いたら窓を出さずに403で断る()
    {
        await StartAsync();

        // 窓が「あと何回」を知る（3回目の窓が「あと1回」）
        Assert.Equal(3, (await AnswerAsync("作り物の道具", ApiClientDecision.Refusal)).RefusalsLeft);
        Assert.Equal(1, Assert.Single(Refusals).Count);
        Assert.Null(Refusals.Single().BlockedAt);
        Assert.Equal(2, (await AnswerAsync("作り物の道具", ApiClientDecision.Refusal)).RefusalsLeft);
        _now = _now.AddMinutes(5);
        Assert.Equal(1, (await AnswerAsync("作り物の道具", ApiClientDecision.Refusal)).RefusalsLeft);

        var saved = Assert.Single(Refusals);
        Assert.Equal((3, _now, (DateTimeOffset?)_now, FakeExe), (saved.Count, saved.LastRefusedAt, saved.BlockedAt, saved.ExePath));

        var response = await RequestAsync("作り物の道具", "marks");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("requestBlocked", await CodeOf(response));
        Assert.Equal(3, _approvals.AskedCount);

        // 画面を出していないときも、止めているほうを答える（画面を開いても窓は出ないので）
        _approvals.CanAsk = false;
        Assert.Equal("requestBlocked", await CodeOf(await RequestAsync("作り物の道具", "marks")));
        _approvals.CanAsk = true;

        // 別のソフト（名前が違う）は止めない
        Assert.Equal(3, (await AnswerAsync("別の作り物", ApiClientDecision.Refusal)).RefusalsLeft);

        // 控えには名前・場所・回数・日時だけ（トークンに当たる物は無い）
        var text = await File.ReadAllTextAsync(_store.Paths.ApiClientsFile);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 許可したら回数を0に戻す()
    {
        await StartAsync();
        await AnswerAsync("作り物の道具", ApiClientDecision.Refusal);
        await AnswerAsync("作り物の道具", ApiClientDecision.Refusal);
        Assert.Equal(2, Assert.Single(Refusals).Count);

        // トークンを受け取りに来なくても、人が許した時点で0に戻す
        var requestId = await RequestIdAsync("作り物の道具", "marks");
        var asked = await _approvals.NextAsync();
        Assert.Equal(1, asked.Info.RefusalsLeft);
        asked.Answer(new ApiClientDecision(true, [ApiScopes.Marks]));
        await WaitForAsync(() => Refusals.Count == 0);

        await ResultAsync(requestId);
        Assert.Equal(3, (await AnswerAsync("作り物の道具", ApiClientDecision.Refusal)).RefusalsLeft);
        Assert.Equal(1, Assert.Single(Refusals).Count);

        // 求め直しを拒否しても、前に許したトークンの行はそのまま（止めるのは新しい求めだけ）
        Assert.Single(_store.ApiClients.Load().Clients);
    }

    [Fact]
    public async Task 止めを外すと_また窓が出る()
    {
        await StartAsync();
        for (var index = 0; index < 3; index++)
        {
            await AnswerAsync("作り物の道具", ApiClientDecision.Refusal);
        }

        Assert.Equal("requestBlocked", await CodeOf(await RequestAsync("作り物の道具", "marks")));

        // 設定の画面と同じ道：控えから記録を消して、API に読み直させる
        await _store.ApiClients.UpdateAsync(list => list with { Refusals = [] });
        _state.Clients.Reload();

        Assert.Equal(3, (await AnswerAsync("作り物の道具", ApiClientDecision.Refusal)).RefusalsLeft);
        Assert.Equal(1, Assert.Single(Refusals).Count);
    }

    [Fact]
    public async Task 上限が0なら_拒否を覚えず毎回窓を出す()
    {
        Directory.CreateDirectory(_root);
        JsonStore.Write(new AppPaths(_root).ApiClientsFile, new ApiClientList { RefusalLimit = 0 });
        await StartAsync();

        for (var index = 0; index < 4; index++)
        {
            Assert.Null((await AnswerAsync("作り物の道具", ApiClientDecision.Refusal)).RefusalsLeft);
        }

        Assert.Empty(Refusals);
        Assert.Equal(4, _approvals.AskedCount);

        // 手で書いた止めの記録があっても、0 なら止めない
        await _store.ApiClients.UpdateAsync(list => list with
        {
            Refusals = [new ApiClientRefusal { Name = "作り物の道具", ExePath = FakeExe, Count = 3, LastRefusedAt = _now, BlockedAt = _now }],
        });
        _state.Clients.Reload();
        Assert.Equal(HttpStatusCode.Accepted, (await RequestAsync("作り物の道具", "marks")).StatusCode);
    }

    [Fact]
    public async Task 窓を閉じた_聞けなかったは拒否に数えない()
    {
        await StartAsync();
        for (var index = 0; index < 4; index++)
        {
            Assert.Equal(3, (await AnswerAsync("作り物の道具", ApiClientDecision.Dismissed)).RefusalsLeft);
        }

        Assert.Empty(Refusals);
    }

    [Fact]
    public void 今ある控え_拒否の欄が無い物は_上限3で記録なしとして読む()
    {
        Directory.CreateDirectory(_root);
        var paths = new AppPaths(_root);
        File.WriteAllText(paths.ApiClientsFile, """{ "version": 1, "clients": [] }""");
        var list = new DataStore(paths).ApiClients.Load();
        Assert.Equal(3, list.RefusalLimit);
        Assert.Empty(list.Refusals ?? []);
    }

    [Fact]
    public async Task 求めが重なったとき_同じソフトの求めは1回の拒否に数え_列に残った止めたソフトの求めは窓を出さずに断る()
    {
        await StartAsync();

        // 同じソフトから答える前に重ねて来た求めは1つにまとめるので、1回の拒否は1回に数える
        var first = await RequestIdAsync("作り物の道具", "marks");
        Assert.Equal(first, await RequestIdAsync("作り物の道具", "marks"));
        (await _approvals.NextAsync()).Answer(ApiClientDecision.Refusal);
        await ResultAsync(first);
        Assert.Equal(1, Assert.Single(Refusals).Count);

        // 別のソフトの窓を開いている間に、作り物の道具の求めが列に並ぶ。その間に止まった（設定や別の道で）
        var other = await RequestIdAsync("別の作り物", "tags");
        var otherAsked = await _approvals.NextAsync();
        var queued = await RequestIdAsync("作り物の道具", "marks");
        await _store.ApiClients.UpdateAsync(list => list with
        {
            Refusals = [new ApiClientRefusal { Name = "作り物の道具", ExePath = FakeExe, Count = 3, LastRefusedAt = _now, BlockedAt = _now }],
        });
        _state.Clients.Reload();
        otherAsked.Answer(ApiClientDecision.Dismissed);
        await ResultAsync(other);

        // 列に残っていた求めは、窓を出さずに断る（数は足さない）
        Assert.Equal("denied", (await ResultAsync(queued)).GetProperty("state").GetString());
        Assert.Equal(2, _approvals.AskedCount);
        Assert.Equal(3, Assert.Single(Refusals).Count);
    }

    [Fact]
    public async Task 仕様書では_許しを求める道はトークンなしで_省ける欄は必須にしない()
    {
        await StartAsync();
        var document = await BodyOf(await _client.GetAsync("/v1/openapi.json"));
        var request = document.GetProperty("paths").GetProperty("/v1/clients/request").GetProperty("post");
        Assert.False(request.TryGetProperty("security", out _));
        Assert.True(document.GetProperty("paths").GetProperty("/v1/clients/me").GetProperty("get").TryGetProperty("security", out _));

        static IEnumerable<string?> Required(JsonElement schemas, string name)
            => schemas.GetProperty(name).TryGetProperty("required", out var required) ? required.EnumerateArray().Select(value => value.GetString()) : [];

        var schemas = document.GetProperty("components").GetProperty("schemas");
        Assert.Equal(["name"], Required(schemas, "ClientRequestBody"));
        Assert.Equal(["requestId"], Required(schemas, "ClientResultRequest"));
    }

    [Fact]
    public async Task 知らない合言葉は404()
    {
        await StartAsync();
        var response = await _client.PostAsync("/v1/clients/request/result", Json("""{"requestId":"nothing"}"""));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("requestNotFound", await CodeOf(response));
    }

    [Fact]
    public async Task ブラウザからは_許可したオリジンだけ受け付け_オリジンを窓に出す()
    {
        await StartAsync(origins: ["http://localhost:5173"]);

        var denied = new HttpRequestMessage(HttpMethod.Post, "/v1/clients/request") { Content = Json("""{"name":"作り物のページ"}""") };
        denied.Headers.Add("Origin", "http://evil.example");
        var refused = await _client.SendAsync(denied);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("originNotAllowed", await CodeOf(refused));

        var allowed = new HttpRequestMessage(HttpMethod.Post, "/v1/clients/request") { Content = Json("""{"name":"作り物のページ"}""") };
        allowed.Headers.Add("Origin", "http://localhost:5173");
        Assert.Equal(HttpStatusCode.Accepted, (await _client.SendAsync(allowed)).StatusCode);
        Assert.Equal("http://localhost:5173", (await _approvals.NextAsync()).Info.Origin);
    }

    [Fact]
    public async Task 書き込みを許したソフトがあれば_事前の確かめで書く方法を許す()
    {
        await StartAsync(allowWrites: false, origins: ["http://localhost:5173"]);

        async Task<string> MethodsAsync()
        {
            var preflight = new HttpRequestMessage(HttpMethod.Options, $"/v1/items/{ItemId}/favorite");
            preflight.Headers.Add("Origin", "http://localhost:5173");
            preflight.Headers.Add("Access-Control-Request-Method", "PUT");
            return string.Join(",", (await _client.SendAsync(preflight)).Headers.GetValues("Access-Control-Allow-Methods"));
        }

        Assert.DoesNotContain("PUT", await MethodsAsync());
        await GrantAsync("作り物の道具", ["marks"], [ApiScopes.Marks]);
        Assert.Contains("PUT", await MethodsAsync());
    }

    [Fact]
    public async Task 同じソフトが求め直して許すと_前の行を置き換え_前のトークンは使えない()
    {
        await StartAsync();
        var old = await GrantAsync("作り物の道具", ["marks"], [ApiScopes.Marks]);

        var requestId = await RequestIdAsync("作り物の道具", "marks");
        var asked = await _approvals.NextAsync();
        Assert.True(asked.Info.Replaces);
        asked.Answer(new ApiClientDecision(true, [ApiScopes.Marks]));
        var renewed = (await ResultAsync(requestId)).GetProperty("token").GetString()!;

        Assert.Single(_store.ApiClients.Load().Clients);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(With(HttpMethod.Get, $"/v1/items/{ItemId}", old))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(With(HttpMethod.Get, $"/v1/items/{ItemId}", renewed))).StatusCode);
    }

    /// <summary>結果を取りに行き、もう無い求め（期限切れ・失効）として 404 で断られることを確かめる。</summary>
    private async Task AssertResultGoneAsync(string requestId)
    {
        var response = await _client.PostAsync("/v1/clients/request/result", Json(JsonSerializer.Serialize(new { requestId, wait = 0 })));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("requestNotFound", await CodeOf(response));
    }

    [Fact]
    public async Task 受け取っていない前の許しは_同じソフトを後で許すと失効し_後の行を置き換えない()
    {
        await StartAsync();

        // 前の求めを許したが、ソフトは受け取らずに求め直した（受け取っていない許しと、新しい求めが並ぶ）
        var stale = await RequestIdAsync("作り物の道具", "marks", "tags");
        (await _approvals.NextAsync()).Answer(new ApiClientDecision(true, [ApiScopes.Marks, ApiScopes.Tags]));
        var renewed = await RequestIdAsync("作り物の道具", "marks");
        (await _approvals.NextAsync()).Answer(new ApiClientDecision(true, []));
        var token = (await ResultAsync(renewed)).GetProperty("token").GetString()!;

        await AssertResultGoneAsync(stale);
        var saved = Assert.Single(_store.ApiClients.Load().Clients);
        Assert.Empty(saved.Scopes);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(With(HttpMethod.Get, "/v1/clients/me", token))).StatusCode);
    }

    [Fact]
    public async Task 受け取っていない前の許しは_後で許した行を取り消した後に受け取っても_トークンを出さない()
    {
        await StartAsync();
        var stale = await RequestIdAsync("作り物の道具", "marks");
        (await _approvals.NextAsync()).Answer(new ApiClientDecision(true, [ApiScopes.Marks]));
        await GrantAsync("作り物の道具", ["marks"], []);

        // 設定の画面の取り消しと同じ書き方
        await _store.ApiClients.UpdateAsync(list => list with { Clients = [] });
        _state.Clients.Reload();

        await AssertResultGoneAsync(stale);
        Assert.Empty(_store.ApiClients.Load().Clients);
    }

    [Fact]
    public async Task 許した後に前からの行を取り消すと_受け取っていない許しは失効する()
    {
        await StartAsync();
        await GrantAsync("作り物の道具", [], []);

        // 求め直しを許した後、受け取る前に人が設定で前の行を取り消した（人の後の操作を勝たせる）
        var pending = await RequestIdAsync("作り物の道具", "marks");
        (await _approvals.NextAsync()).Answer(new ApiClientDecision(true, [ApiScopes.Marks]));
        await _store.ApiClients.UpdateAsync(list => list with { Clients = [] });
        _state.Clients.Reload();

        await AssertResultGoneAsync(pending);
        Assert.Empty(_store.ApiClients.Load().Clients);
    }

    [Fact]
    public async Task 人が求められていない種類を返しても_許さない()
    {
        await StartAsync();
        var token = await GrantAsync("作り物の道具", ["marks"], [ApiScopes.Marks, ApiScopes.Tags]);
        Assert.Equal([ApiScopes.Marks], Assert.Single(_store.ApiClients.Load().Clients).Scopes);
        Assert.Equal(HttpStatusCode.Forbidden, (await AddUserTagAsync(token)).StatusCode);
    }

    [Fact]
    public async Task 最後に使った日時は_間を空けて書く()
    {
        await StartAsync();
        var token = await GrantAsync("作り物の道具", [], []);

        await _client.SendAsync(With(HttpMethod.Get, "/v1/clients/me", token));
        var firstUse = _now;
        await WaitForAsync(() => _store.ApiClients.Load().Clients.Single().LastUsedAt == firstUse);

        // 間が短ければ書かない
        _now = firstUse.AddMinutes(1);
        await _client.SendAsync(With(HttpMethod.Get, "/v1/clients/me", token));
        await Task.Delay(200);
        Assert.Equal(firstUse, _store.ApiClients.Load().Clients.Single().LastUsedAt);

        _now = firstUse + ApiClientBook.TouchInterval;
        await _client.SendAsync(With(HttpMethod.Get, "/v1/clients/me", token));
        await WaitForAsync(() => _store.ApiClients.Load().Clients.Single().LastUsedAt == _now);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "書かれるのを待ちきれませんでした。");
            await Task.Delay(20);
        }
    }

    [Fact]
    public void TCPの表から_相手の側の行を選ぶ()
    {
        const uint loopback = 0x0100007F; // 127.0.0.1（ネットワークの順）
        TcpRow[] rows =
        [
            new(loopback, 39847, loopback, 51000, ProcessId: 10), // 待つ側（自分）
            new(loopback, 51000, loopback, 39847, ProcessId: 20), // 相手の側
            new(0x0100000A, 51000, 0x0200000A, 39847, ProcessId: 30), // 別のアドレスの同じ番号
        ];

        Assert.Equal(20, TcpConnectionOwners.FindOwner(rows, serverPort: 39847, clientPort: 51000));
        Assert.Null(TcpConnectionOwners.FindOwner(rows, serverPort: 39847, clientPort: 51001));

        // 表のポートはネットワークの順（39847 = 0x9BA7 → 下位の2バイトに A7 9B）
        Assert.Equal(39847, TcpConnectionOwners.PortOf(0xA79B));
    }

    [Fact]
    public async Task 実際の接続から_自分のexeを引ける()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var serverPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, serverPort);
        using var accepted = await listener.AcceptTcpClientAsync();
        var clientPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;

        var path = TcpConnectionOwners.Instance.ExePathOf(serverPort, clientPort);
        Assert.Equal(Environment.ProcessPath, path, ignoreCase: true);
    }

    /// <summary>作り物の exe を引く口（決まったパスを返す）。</summary>
    private sealed class FakeOwners(string? path) : IConnectionOwners
    {
        public string? ExePathOf(int serverPort, int clientPort) => path;
    }

    /// <summary>作り物の聞く口：聞かれた物を並べ、試験が答えるまで待つ。同時に開いた窓の数を数える。</summary>
    private sealed class FakeApprovals : IApiClientApprovals
    {
        private readonly System.Threading.Channels.Channel<Asked> _asked = System.Threading.Channels.Channel.CreateUnbounded<Asked>();
        private int _open;

        public bool CanAsk { get; set; } = true;

        public int AskedCount;

        public int MaxConcurrent;

        public async Task<ApiClientDecision> AskAsync(ApiClientRequestInfo request, CancellationToken cancellationToken)
        {
            var open = Interlocked.Increment(ref _open);
            InterlockedMax(ref MaxConcurrent, open);
            Interlocked.Increment(ref AskedCount);
            var asked = new Asked(request);
            await _asked.Writer.WriteAsync(asked, cancellationToken);
            try
            {
                return await asked.Decision.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _open);
            }
        }

        public async Task<Asked> NextAsync() => await _asked.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }

    private sealed class Asked(ApiClientRequestInfo info)
    {
        public ApiClientRequestInfo Info { get; } = info;

        public TaskCompletionSource<ApiClientDecision> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Answer(ApiClientDecision decision) => Decision.SetResult(decision);
    }
}
