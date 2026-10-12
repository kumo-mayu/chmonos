using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Chmonos.Query;
using Microsoft.AspNetCore.Builder;

namespace Chmonos.Api.Tests;

/// <summary>点検30の直し（守り・同期の取りこぼし・読み直しの失敗・式の大きさ）。名前・ID は作り物。</summary>
public sealed class ApiAuditFixTests : IAsyncLifetime
{
    private const string Token = "test-token-audit-0123456789";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-api-tests", Guid.NewGuid().ToString("N"));
    private WebApplication? _app;
    private StoreSync _sync = null!;
    private DataStore _store = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _store = new DataStore(new AppPaths(_root));
        await _store.Items.SaveAsync(new ItemRecord { Id = "9300001", Booth = new BoothBlock { Name = "作り物の一" } });
        Directory.CreateDirectory(_store.Paths.ItemsDir);
        await File.WriteAllTextAsync(_store.Paths.ItemFile("9300002"), "{ 壊れた");

        (_app, _, _sync) = await ApiHost.BuildAsync(new ApiHostOptions(_root, 0, Token, [], Watch: false));
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{new Uri(_app.Urls.Single()).Port}") };
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

    private async Task<JsonElement> GetAsync(string path, bool token = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        }

        var response = await _client.SendAsync(request);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task 合言葉なしのhealthは読めなかった商品の数だけで_IDは合言葉の要るURLで返す()
    {
        var health = await GetAsync("/v1/health", token: false);
        Assert.Equal(1, health.GetProperty("failedItemCount").GetInt32());
        Assert.False(health.TryGetProperty("failedItemIds", out _));
        Assert.DoesNotContain("9300002", health.GetRawText());

        Assert.Equal("unauthorized", (await GetAsync("/v1/failed-items", token: false)).GetProperty("code").GetString());
        Assert.Equal(["9300002"], (await GetAsync("/v1/failed-items")).EnumerateArray().Select(id => id.GetString()));
    }

    [Fact]
    public void 変化の一覧と通番は一緒に取り_聞き始めも通番と一緒に取る()
    {
        var feed = new ChangeFeed();
        feed.Publish("changed", ["items"], ["1"], null, DateTimeOffset.Now);

        var (changes, sequence) = feed.Since(0);
        Assert.Equal(1, sequence);
        Assert.Equal([1L], changes!.Select(change => change.Seq));

        var (channel, listenedAt) = feed.Listen();
        Assert.Equal(1, listenedAt);
        feed.Publish("changed", ["items"], ["2"], null, DateTimeOffset.Now);
        Assert.True(channel.Reader.TryRead(out var next));
        Assert.Equal(2, next.Seq);
        feed.Stop(channel);

        // 持っていない通番（未来）は取り直してもらう
        Assert.Null(feed.Since(5).Changes);
    }

    [Fact]
    public void SSEは通番が飛んだら_先にreloadedを送る()
    {
        var feed = new ChangeFeed();
        var (channel, sent) = feed.Listen(capacity: 2);
        for (var index = 0; index < 5; index++)
        {
            feed.Publish("changed", ["items"], [$"{index}"], null, DateTimeOffset.Now);
        }

        // 待ち行列は2つで、古い物（1〜3）が落ちた
        var outgoing = new List<ChangeEventRef>();
        while (channel.Reader.TryRead(out var change))
        {
            outgoing.AddRange(ApiEndpoints.Outgoing(sent, change));
            sent = Math.Max(sent, change.Seq);
        }

        Assert.Equal([("reloaded", 3L), ("changed", 4L), ("changed", 5L)], outgoing.Select(change => (change.Kind, change.Seq)));

        // 送り済みの物は送らない
        Assert.Empty(ApiEndpoints.Outgoing(5, outgoing[^1]));
    }

    [Fact]
    public async Task 読み直しに失敗したら_変化を貯め直し_次の読み直しで追いつく()
    {
        JsonStore.Write(_store.Paths.AvatarRegistryFile, new AvatarRegistry { Entries = [new AvatarRegistryEntry { ItemId = "7000001", BoothName = "作り物のアバター" }] });

        // ほかのアプリが開いたままで読めない
        using (new FileStream(_store.Paths.AvatarRegistryFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            _sync.Note(_store.Paths.AvatarRegistryFile);
            await Assert.ThrowsAnyAsync<IOException>(() => _sync.ApplyPendingAsync());
        }

        Assert.Empty((await GetAsync("/v1/avatars")).EnumerateArray());

        // 新しい知らせが無くても、貯め直した変化で読み直す（印を確認済みにしていない）
        await _sync.ApplyPendingAsync();
        Assert.Single((await GetAsync("/v1/avatars")).EnumerateArray());
    }
    [Fact]
    public async Task 改変が一時的に読めなければ_前の記録を残し_読めるようになったら印が変わらなくても読み直す()
    {
        await _store.Modifications.SaveAsync(new ModificationRecord { Id = "mod-0001", AvatarItemId = "9300001", Name = "作り物の改変" });
        _sync.Note(_store.Paths.ModificationFile("mod-0001"));
        await _sync.ApplyPendingAsync();
        Assert.Single((await GetAsync("/v1/modifications")).EnumerateArray());

        // 名前を変えたが、ほかのアプリが開いたままで読めない。改変の読み込みは投げないので、前は一覧から消えて印も確定していた（点検31）
        await _store.Modifications.SaveAsync(new ModificationRecord { Id = "mod-0001", AvatarItemId = "9300001", Name = "作り物の改変・二" });
        using (new FileStream(_store.Paths.ModificationFile("mod-0001"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            _sync.Note(_store.Paths.ModificationFile("mod-0001"));
            await _sync.ApplyPendingAsync();
        }

        Assert.Equal("作り物の改変", (await GetAsync("/v1/modifications"))[0].GetProperty("name").GetString());

        // ファイルは変わらないまま。2分ごとの見比べ（全部の部分）で読み直す
        var everything = new StoreChangeSet();
        everything.Add(StorePart.Modifications);
        _sync.Enqueue(everything);
        await _sync.ApplyPendingAsync();
        Assert.Equal("作り物の改変・二", (await GetAsync("/v1/modifications"))[0].GetProperty("name").GetString());
    }
    [Fact]
    public async Task 壊れた改変が残っても_見比べのたびに知らせない()
    {
        await _store.Modifications.SaveAsync(new ModificationRecord { Id = "mod-0001", AvatarItemId = "9300001", Name = "作り物の改変" });
        Directory.CreateDirectory(_store.Paths.ModificationsDir);
        await File.WriteAllTextAsync(_store.Paths.ModificationFile("mod-0002"), "{ 壊れた");
        _sync.Note(_store.Paths.ModificationFile("mod-0001"));
        await _sync.ApplyPendingAsync();
        var sequence = (await GetAsync("/v1/changes")).GetProperty("sequence").GetInt64();

        // 読み終えていないので見比べのたびに読み直すが、中身は変わらないので知らせない（点検32）
        for (var round = 0; round < 3; round++)
        {
            var everything = new StoreChangeSet();
            everything.Add(StorePart.Modifications);
            _sync.Enqueue(everything);
            await _sync.ApplyPendingAsync();
        }

        Assert.Equal(sequence, (await GetAsync("/v1/changes")).GetProperty("sequence").GetInt64());
        Assert.Single((await GetAsync("/v1/modifications")).EnumerateArray());
    }

    [Theory]
    [InlineData("/v1/items/batch", """{"ids":[null]}""", "invalidIds")]
    [InlineData("/v1/search", """{"where":{"and":[null]}}""", "invalidExpression")]
    [InlineData("/v1/search", """{"sort":[null]}""", "invalidSort")]
    public async Task 配列の中のnullは_入力の誤りとして400を返す(string path, string body, string code)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
    }
}
