using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Microsoft.AspNetCore.Builder;

namespace Chmonos.Api.Tests;

/// <summary>同期（docs/spec/local-api.md「同期」）：変わった部分の読み直し・変化の記録・SSE・まとめて書く間。名前・ID は作り物。</summary>
public sealed class ApiSyncTests : IAsyncLifetime
{
    private const string Token = "test-token-sync-0123456789";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-api-tests", Guid.NewGuid().ToString("N"));
    private WebApplication? _app;
    private StoreSync _sync = null!;
    private DataStore _store = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync() => StartAsync(watch: false);

    private async Task StartAsync(bool watch)
    {
        _store = new DataStore(new AppPaths(_root));
        await _store.Items.SaveAsync(new ItemRecord { Id = "9400001", Booth = new BoothBlock { Name = "作り物の一" } });
        (_app, _, _sync) = await ApiHost.BuildAsync(new ApiHostOptions(_root, 0, Token, [], Watch: watch));
        await _app.StartAsync();
        if (watch)
        {
            _sync.Start();
        }

        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{new Uri(_app.Urls.Single()).Port}") };
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
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

    private async Task<JsonElement> GetAsync(string path)
        => JsonDocument.Parse(await _client.GetStringAsync(path)).RootElement;

    private async Task<HttpStatusCode> StatusAsync(string path) => (await _client.GetAsync(path)).StatusCode;

    private async Task SaveAndNoteAsync(ItemRecord item)
    {
        await _store.Items.SaveAsync(item);
        _sync.Note(_store.Paths.ItemFile(item.Id));
    }

    [Fact]
    public async Task 変わった商品を読み直し_変化として記録する()
    {
        await SaveAndNoteAsync(new ItemRecord { Id = "9400002", Booth = new BoothBlock { Name = "作り物の二" } });
        await _sync.ApplyPendingAsync();

        Assert.Equal(HttpStatusCode.OK, await StatusAsync("/v1/items/9400002"));
        var version = await GetAsync("/v1/version");
        var changes = await GetAsync($"/v1/changes?since=0&epoch={version.GetProperty("epoch").GetString()}");
        Assert.False(changes.GetProperty("reload").GetBoolean());
        var change = changes.GetProperty("changes").EnumerateArray().Single();
        Assert.Equal("changed", change.GetProperty("kind").GetString());
        Assert.Equal(["items"], change.GetProperty("parts").EnumerateArray().Select(part => part.GetString()));
        Assert.Equal(["9400002"], change.GetProperty("itemIds").EnumerateArray().Select(id => id.GetString()));

        // 起動の名前が違う（API が起動し直した）・持っていない古い通番なら、全部を取り直してもらう
        Assert.True((await GetAsync("/v1/changes?since=0&epoch=other")).GetProperty("reload").GetBoolean());
        Assert.True((await GetAsync("/v1/changes?since=99")).GetProperty("reload").GetBoolean());
    }

    [Fact]
    public async Task ファイルの印が変わっていなければ_読み直しても知らせない()
    {
        _sync.Note(_store.Paths.AvatarRegistryFile);
        _sync.Note(_store.Paths.ItemFile("9400001"));
        await _sync.ApplyPendingAsync();

        Assert.Equal(0, (await GetAsync("/v1/version")).GetProperty("sequence").GetInt64());
    }

    [Fact]
    public async Task まとめて書いている間は古い答えを返し_終わってから読み直す()
    {
        _sync.SetBusy("import", DateTimeOffset.Now);
        Assert.Equal("import", (await GetAsync("/v1/version")).GetProperty("busy").GetString());

        await SaveAndNoteAsync(new ItemRecord { Id = "9400003", Booth = new BoothBlock { Name = "作り物の三" } });
        await _sync.ApplyPendingAsync();
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync("/v1/items/9400003"));

        _sync.SetBusy(null, DateTimeOffset.Now);
        await _sync.ApplyPendingAsync();
        Assert.Equal(HttpStatusCode.OK, await StatusAsync("/v1/items/9400003"));

        var kinds = (await GetAsync("/v1/changes?since=0")).GetProperty("changes").EnumerateArray()
            .Select(change => (change.GetProperty("kind").GetString(), change.GetProperty("busy").GetString()));
        Assert.Equal([("busy", "import"), ("busy", null), ("changed", null)], kinds);
    }

    [Fact]
    public async Task 許す住所は_APIの設定が変われば読み直す()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/health");
        request.Headers.Add("Origin", "http://localhost:5173");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(request)).StatusCode);

        JsonStore.Write(_store.Paths.ApiSettingsFile, new ApiSettings { AllowedOrigins = ["http://localhost:5173"] });
        _sync.Note(_store.Paths.ApiSettingsFile);
        await _sync.ApplyPendingAsync();

        var again = new HttpRequestMessage(HttpMethod.Get, "/v1/health");
        again.Headers.Add("Origin", "http://localhost:5173");
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(again)).StatusCode);
    }

    [Fact]
    public async Task SSEで変化を流し_つなぎ直すと続きから流す()
    {
        using var events = await _client.GetAsync("/v1/events", HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal("text/event-stream", events.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await events.Content.ReadAsStreamAsync());

        await SaveAndNoteAsync(new ItemRecord { Id = "9400004", Booth = new BoothBlock { Name = "作り物の四" } });
        await _sync.ApplyPendingAsync();

        var lines = new List<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await reader.ReadLineAsync(timeout.Token) is { } line && line.Length > 0)
        {
            lines.Add(line);
        }

        var epoch = (await GetAsync("/v1/version")).GetProperty("epoch").GetString();
        Assert.Equal($"id: {epoch}-1", lines[0]);
        Assert.Equal("event: changed", lines[1]);
        Assert.Contains("\"itemIds\":[\"9400004\"]", lines[2]);

        // つなぎ直し：前に受けた id より後の分だけ流す。起動の名前が違えば reloaded
        await SaveAndNoteAsync(new ItemRecord { Id = "9400005", Booth = new BoothBlock { Name = "作り物の五" } });
        await _sync.ApplyPendingAsync();
        Assert.Contains("9400005", await FirstEventAsync($"{epoch}-1"));
        Assert.Contains("event: reloaded", await FirstEventAsync("other-1"));
    }

    private async Task<string> FirstEventAsync(string lastEventId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/events");
        request.Headers.Add("Last-Event-ID", lastEventId);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var text = new StringBuilder();
        while (await reader.ReadLineAsync(timeout.Token) is { } line && line.Length > 0)
        {
            text.AppendLine(line);
        }

        return text.ToString();
    }

    [Fact]
    public async Task 本物の見張りで_アプリの書き込みを拾う()
    {
        await DisposeAsync();
        await StartAsync(watch: true);

        await _store.Items.SaveAsync(new ItemRecord { Id = "9400006", Booth = new BoothBlock { Name = "作り物の六" } });

        // 見張りの知らせは少し遅れて届く。待つ上限は長めにし、届いたらすぐ抜ける
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && await StatusAsync("/v1/items/9400006") != HttpStatusCode.OK)
        {
            await Task.Delay(100);
        }

        Assert.Equal(HttpStatusCode.OK, await StatusAsync("/v1/items/9400006"));
    }
}
