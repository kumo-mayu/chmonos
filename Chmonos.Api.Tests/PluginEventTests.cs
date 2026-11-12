using System.Diagnostics;
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
/// ソフトへの出来事の流れと購読（plan 44 の①・docs/spec/plugins.md）：待って返る・すぐ返る・通番の続き・起動の名前が変わったとき・あふれ・
/// 許していない種類・古いトークン・購読していないときに積まない・許しを外したら止まる・止めると待ちが解ける。
/// 許したソフトは控えに直に書く（許しの窓の流れは ApiClientTests が見る）。名前・ID は作り物
/// </summary>
public sealed class PluginEventTests : IAsyncLifetime
{
    private const string LegacyToken = "test-token-plugin-0123456789";
    private const string WatcherToken = "watcher-token-0123456789";
    private const string ReaderToken = "reader-token-0123456789";
    private const string Origin = "http://localhost:5173";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-api-tests", Guid.NewGuid().ToString("N"));
    private readonly PluginEventHub _hub;
    private WebApplication? _app;
    private ApiState _state = null!;
    private DataStore _store = null!;
    private HttpClient _client = null!;
    private DateTimeOffset _now = new(2026, 11, 12, 9, 0, 0, TimeSpan.FromHours(9));

    public PluginEventTests() => _hub = new PluginEventHub(() => _now);

    public async Task InitializeAsync()
    {
        _store = new DataStore(new AppPaths(_root));
        JsonStore.Write(_store.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, AllowedOrigins = [Origin] });
        WriteClients(ApiScopes.Watches);

        (_app, _state, _) = await ApiHost.BuildAsync(new ApiHostOptions(
            _root, 0, LegacyToken, [Origin], Clock: () => _now, Watch: false, Store: _store, Plugins: _hub));
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{new Uri(_app.Urls.Single()).Port}") };
    }

    /// <summary>見張りを <paramref name="watcherScopes"/> だけ許したソフトと、読み取りだけのソフトを控えに書く。</summary>
    private void WriteClients(IReadOnlyList<string> watcherScopes)
        => JsonStore.Write(_store.Paths.ApiClientsFile, new ApiClientList
        {
            Clients =
            [
                new ApiClientRecord { Id = "watcher", Name = "作り物の見張り", Scopes = watcherScopes, TokenHash = ApiClientBook.HashOf(WatcherToken), GrantedAt = _now },
                new ApiClientRecord { Id = "reader", Name = "作り物の読み手", Scopes = [], TokenHash = ApiClientBook.HashOf(ReaderToken), GrantedAt = _now },
            ],
        });

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

    private HttpRequestMessage With(HttpMethod method, string path, string token, string? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private Task<HttpResponseMessage> SubscribeAsync(string token, params string[] kinds)
        => _client.SendAsync(With(HttpMethod.Put, "/v1/plugin/subscriptions", token, JsonSerializer.Serialize(new { kinds })));

    private async Task<JsonElement> EventsAsync(long after = 0, int wait = 0, string? epoch = null, string token = WatcherToken)
    {
        var query = $"/v1/plugin/events?after={after}&wait={wait}" + (epoch is null ? string.Empty : $"&epoch={epoch}");
        var response = await _client.SendAsync(With(HttpMethod.Get, query, token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static long[] SeqsOf(JsonElement answer) => answer.GetProperty("events").EnumerateArray().Select(item => item.GetProperty("seq").GetInt64()).ToArray();

    private static async Task<string?> CodeOf(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    [Fact]
    public async Task 何も無ければ待ってから_起きたらすぐ返る()
    {
        Assert.Equal(HttpStatusCode.OK, (await SubscribeAsync(WatcherToken, "itemOpened")).StatusCode);

        var watch = Stopwatch.StartNew();
        var waiting = EventsAsync(wait: 20);
        await Task.Delay(300);
        Assert.False(waiting.IsCompleted);

        _hub.PublishItemOpened("9600201");
        var answer = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
        var item = answer.GetProperty("events")[0];
        Assert.Equal("itemOpened", item.GetProperty("kind").GetString());
        Assert.Equal("9600201", item.GetProperty("itemOpened").GetProperty("itemId").GetString());
        Assert.Equal(_hub.Epoch, item.GetProperty("epoch").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("selectionChanged").ValueKind);
    }

    [Fact]
    public async Task 溜まっていれば待たずに返り_時間が来れば空で返る()
    {
        await SubscribeAsync(WatcherToken, "itemOpened");
        _hub.PublishItemOpened("9600202");

        var watch = Stopwatch.StartNew();
        Assert.Equal(new long[] { 1L }, SeqsOf(await EventsAsync(wait: 20)));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));

        // 続きが無ければ wait 秒で空を返し、last は送った after のまま
        var empty = await EventsAsync(after: 1, wait: 1);
        Assert.Empty(SeqsOf(empty));
        Assert.Equal(1, empty.GetProperty("last").GetInt64());
        Assert.False(empty.GetProperty("reset").GetBoolean());
    }

    [Fact]
    public async Task 通番の続きから取れる()
    {
        var subscribed = JsonDocument.Parse(await (await SubscribeAsync(WatcherToken, "itemOpened", "importFinished")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, subscribed.GetProperty("last").GetInt64());
        Assert.Equal(["itemOpened", "importFinished"], subscribed.GetProperty("kinds").EnumerateArray().Select(kind => kind.GetString()));

        _hub.PublishItemOpened("9600203");
        _hub.PublishImport(new PluginImport("imported", 2, 1, 0));
        _hub.PublishItemOpened("9600204");

        var all = await EventsAsync();
        Assert.Equal(new long[] { 1L, 2L, 3L }, SeqsOf(all));
        Assert.Equal(3, all.GetProperty("last").GetInt64());
        Assert.Equal(2, all.GetProperty("events")[1].GetProperty("importFinished").GetProperty("itemsAdded").GetInt32());

        Assert.Equal(new long[] { 3L }, SeqsOf(await EventsAsync(after: 2)));
    }

    [Fact]
    public async Task 起動の名前が違えば_頭から返して印を立てる()
    {
        await SubscribeAsync(WatcherToken, "itemOpened");
        _hub.PublishItemOpened("9600205");
        _hub.PublishItemOpened("9600206");

        // 前の起動の名前と通番で来た：待たずに頭から返す（ソフトは購読を送り直す）
        var watch = Stopwatch.StartNew();
        var other = await EventsAsync(after: 1, wait: 20, epoch: "0123456789ab");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.True(other.GetProperty("reset").GetBoolean());
        Assert.Equal(new long[] { 1L, 2L }, SeqsOf(other));

        // 名前を送らなくても、まだ無い通番は前の起動の物とみなす
        var ahead = await EventsAsync(after: 50, wait: 20);
        Assert.True(ahead.GetProperty("reset").GetBoolean());
        Assert.Equal(new long[] { 1L, 2L }, SeqsOf(ahead));

        Assert.False((await EventsAsync(after: 1, epoch: _hub.Epoch)).GetProperty("reset").GetBoolean());
    }

    [Fact]
    public async Task あふれたら古い方を捨てて_捨てた数を言う()
    {
        await SubscribeAsync(WatcherToken, "itemOpened");
        for (var i = 0; i < PluginEventHub.Capacity + 5; i++)
        {
            _hub.PublishItemOpened($"96003{i:000}");
        }

        var answer = await EventsAsync();
        Assert.Equal(5, answer.GetProperty("dropped").GetInt64());
        Assert.Equal(PluginEventHub.Capacity, SeqsOf(answer).Length);
        Assert.Equal(6, SeqsOf(answer)[0]);

        // 続きから取れば、もう捨てた物は無い
        Assert.Equal(0, (await EventsAsync(after: answer.GetProperty("last").GetInt64())).GetProperty("dropped").GetInt64());
    }

    [Fact]
    public async Task 選択は上限で切って_数と印で伝える()
    {
        await SubscribeAsync(WatcherToken, "selectionChanged");
        var ids = Enumerable.Range(0, PluginEventHub.MaxSelectionIds + 10).Select(i => $"97{i:000000}").ToList();
        _hub.PublishSelection("search", ids);

        var selection = (await EventsAsync()).GetProperty("events")[0].GetProperty("selectionChanged");
        Assert.Equal("search", selection.GetProperty("screen").GetString());
        Assert.Equal(PluginEventHub.MaxSelectionIds, selection.GetProperty("itemIds").GetArrayLength());
        Assert.Equal(ids.Count, selection.GetProperty("count").GetInt32());
        Assert.True(selection.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task 許していない種類と知らない種類は購読できない()
    {
        var notGranted = await SubscribeAsync(ReaderToken, "itemOpened");
        Assert.Equal(HttpStatusCode.Forbidden, notGranted.StatusCode);
        Assert.Equal("scopeNotGranted", await CodeOf(notGranted));

        var unknown = await SubscribeAsync(WatcherToken, "screenChanged");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("unknownKind", await CodeOf(unknown));

        // 断った購読は何も変えない
        Assert.False(_hub.Watches(PluginEventHub.ItemOpened));
    }

    [Fact]
    public async Task 古いトークンでは使えない()
    {
        var subscribe = await SubscribeAsync(LegacyToken, "itemOpened");
        Assert.Equal(HttpStatusCode.Forbidden, subscribe.StatusCode);
        Assert.Equal("clientTokenRequired", await CodeOf(subscribe));

        var events = await _client.SendAsync(With(HttpMethod.Get, "/v1/plugin/events", LegacyToken));
        Assert.Equal(HttpStatusCode.Forbidden, events.StatusCode);
        Assert.Equal("clientTokenRequired", await CodeOf(events));
    }

    [Fact]
    public async Task 購読していなければ積まず_種類を選べる()
    {
        // 誰も購読していない間は、画面が出来事を作らない
        Assert.False(_hub.Watches(PluginEventHub.ItemOpened));
        _hub.PublishItemOpened("9600207");

        await SubscribeAsync(WatcherToken, "importFinished");
        _hub.PublishItemOpened("9600208");
        Assert.Empty(SeqsOf(await EventsAsync()));

        // 空の配列で止める
        await SubscribeAsync(WatcherToken);
        Assert.False(_hub.Watches(PluginEventHub.ImportFinished));
    }

    [Fact]
    public async Task 許しを外すと_購読もその場で止まる()
    {
        await SubscribeAsync(WatcherToken, "itemOpened", "selectionChanged");
        Assert.True(_hub.Watches(PluginEventHub.ItemOpened));

        // 設定で「商品ページを開いたこと」のチェックを外した
        WriteClients([ApiScopes.WatchSelectionChanged]);
        _state.Clients.Reload();
        Assert.False(_hub.Watches(PluginEventHub.ItemOpened));
        Assert.True(_hub.Watches(PluginEventHub.SelectionChanged));

        // 取り消した：待っている問い合わせは返り、次はトークンが通らない
        await SubscribeAsync(WatcherToken, "selectionChanged");
        var waiting = EventsAsync(wait: 20);
        await Task.Delay(300);
        JsonStore.Write(_store.Paths.ApiClientsFile, new ApiClientList());
        _state.Clients.Reload();
        Assert.Empty(SeqsOf(await waiting.WaitAsync(TimeSpan.FromSeconds(5))));
        Assert.False(_hub.Watches(PluginEventHub.SelectionChanged));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(With(HttpMethod.Get, "/v1/plugin/events", WatcherToken))).StatusCode);
    }

    [Fact]
    public async Task 見張りだけ許したページも_購読をPUTで送れる()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/v1/plugin/subscriptions");
        request.Headers.Add("Origin", Origin);
        request.Headers.Add("Access-Control-Request-Method", "PUT");
        var response = await _client.SendAsync(request);
        Assert.Contains("PUT", response.Headers.GetValues("Access-Control-Allow-Methods").Single());
    }

    [Fact]
    public async Task 取りに来た時刻を覚え_控えには間引いて書く()
    {
        await EventsAsync();
        Assert.Equal(_now, _hub.PollStateOf("watcher").LastPolledAt);
        await WaitForAsync(() => _store.ApiClients.Load().Clients.Single(client => client.Id == "watcher").LastPolledAt == _now);

        // 間隔の内に来ても控えは書き直さない（メモリの時刻は進む）
        var first = _now;
        _now = first.AddMinutes(1);
        await EventsAsync();
        Assert.Equal(_now, _hub.PollStateOf("watcher").LastPolledAt);
        await Task.Delay(200);
        Assert.Equal(first, _store.ApiClients.Load().Clients.Single(client => client.Id == "watcher").LastPolledAt);
    }

    [Fact]
    public async Task 止めると待っている問い合わせが解ける()
    {
        await SubscribeAsync(WatcherToken, "itemOpened");
        var waiting = _client.SendAsync(With(HttpMethod.Get, "/v1/plugin/events?wait=30", WatcherToken));
        await Task.Delay(300);

        var watch = Stopwatch.StartNew();
        await _app!.StopAsync();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"止めるのに {watch.Elapsed} かかった");
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));

        // 止まった間は出来事を作らせない
        Assert.False(_hub.Watches(PluginEventHub.ItemOpened));
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
}
