using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Microsoft.AspNetCore.Builder;

namespace Chmonos.Api.Tests;

/// <summary>
/// 書き込みの API の下地（docs/history/headless-core-2026-10-13.md 段3）：書き込みの許し・口・編集中・取り込み中・CORS・書いた後の答え。
/// 命令の口は作り物（保存先の欄を書くだけ）。命令そのものの中身は Core の試験が見る。名前・ID は作り物
/// </summary>
public sealed class ApiWriteTests : IAsyncLifetime
{
    private const string Token = "test-token-write-0123456789";
    private const string ItemId = "9600001";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-api-tests", Guid.NewGuid().ToString("N"));
    private readonly List<UiCommand> _commands = [];
    private readonly List<ApiWrite> _written = [];
    private WebApplication? _app;
    private StoreSync _sync = null!;
    private DataStore _store = null!;
    private HttpClient _client = null!;
    private string? _editing;

    public Task InitializeAsync() => Task.CompletedTask;

    private async Task StartAsync(bool allowWrites, bool hooks = true, string[]? origins = null)
    {
        _store = new DataStore(new AppPaths(_root));
        await _store.Items.SaveAsync(new ItemRecord { Id = ItemId, Booth = new BoothBlock { Name = "作り物の帽子" } });
        JsonStore.Write(_store.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, AllowWrites = allowWrites, AllowedOrigins = origins ?? [] });

        var writes = hooks
            ? new ApiWriteHooks(ExecuteAsync, write => { lock (_written) { _written.Add(write); } }, id => id == _editing)
            : null;
        (_app, _, _sync) = await ApiHost.BuildAsync(new ApiHostOptions(_root, 0, Token, origins ?? [], Watch: false, Store: _store, Writes: writes));
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{new Uri(_app.Urls.Single()).Port}") };
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    }

    /// <summary>作り物の命令の口：欄を書く命令だけを、アプリと同じ道（持ち主を宣言した書き込み）で書く。</summary>
    private async Task<CommandResult> ExecuteAsync(UiCommand command, CancellationToken cancellationToken)
    {
        lock (_commands)
        {
            _commands.Add(command);
        }

        if (command is UiCommand.SaveItemLocal save)
        {
            await _store.Items.SaveLocalAsync(save.ItemId, save.Local, save.Owns, cancellationToken: cancellationToken);
            return new CommandResult.Done();
        }

        return new CommandResult.Failed("作り物の口では書けない命令です。");
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

    private Task<HttpResponseMessage> PutFavoriteAsync(bool value, string itemId = ItemId)
        => _client.PutAsync($"/v1/items/{itemId}/favorite", new StringContent($$"""{"value":{{(value ? "true" : "false")}}}""", Encoding.UTF8, "application/json"));

    private static async Task<string?> CodeOf(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    private async Task<bool> FavoriteInGetAsync()
        => JsonDocument.Parse(await _client.GetStringAsync($"/v1/items/{ItemId}")).RootElement.GetProperty("isFavorite").GetBoolean();

    [Fact]
    public async Task 書き込みを許していなければ_403で断り_何も書かない()
    {
        await StartAsync(allowWrites: false);

        var response = await PutFavoriteAsync(true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("writesDisabled", await CodeOf(response));
        Assert.Empty(_commands);
        Assert.False((await _store.Items.LoadAsync(ItemId))!.Local.IsFavorite);
    }

    [Fact]
    public async Task 許していれば_命令の口で書き_読み直した答えを返し_画面へ知らせる()
    {
        await StartAsync(allowWrites: true);

        var response = await PutFavoriteAsync(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("isFavorite").GetBoolean());
        Assert.True(await FavoriteInGetAsync());
        Assert.True((await _store.Items.LoadAsync(ItemId))!.Local.IsFavorite);

        var save = Assert.IsType<UiCommand.SaveItemLocal>(Assert.Single(_commands));
        Assert.Equal([LocalField.IsFavorite], save.Owns);
        var write = Assert.Single(_written);
        Assert.Equal(("items", ItemId), (write.Kind, Assert.Single(write.Ids)));
    }

    [Fact]
    public async Task 同じ要求を2回送っても_同じ結果になる()
    {
        await StartAsync(allowWrites: true);

        Assert.Equal(HttpStatusCode.OK, (await PutFavoriteAsync(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PutFavoriteAsync(true)).StatusCode);
        Assert.True(await FavoriteInGetAsync());
    }

    [Fact]
    public async Task アプリから口を受け取っていなければ_503で断る()
    {
        await StartAsync(allowWrites: true, hooks: false);

        var response = await PutFavoriteAsync(true);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("writesUnavailable", await CodeOf(response));
    }

    [Fact]
    public async Task 編集画面で開いている商品には_409で断り_無い商品は404()
    {
        await StartAsync(allowWrites: true);
        _editing = ItemId;

        var editing = await PutFavoriteAsync(true);
        Assert.Equal(HttpStatusCode.Conflict, editing.StatusCode);
        Assert.Equal("itemBeingEdited", await CodeOf(editing));

        var missing = await PutFavoriteAsync(true, "9699999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("itemNotFound", await CodeOf(missing));
        Assert.Empty(_commands);
    }

    [Fact]
    public async Task 取り込みの途中なら_書いて202で受け付けたと返し_終わったら答えに入る()
    {
        await StartAsync(allowWrites: true);
        _sync.SetBusy("import", DateTimeOffset.Now);

        var response = await PutFavoriteAsync(true);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("import", JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("busy").GetString());
        Assert.True((await _store.Items.LoadAsync(ItemId))!.Local.IsFavorite);
        Assert.False(await FavoriteInGetAsync());

        _sync.SetBusy(null, DateTimeOffset.Now);
        await _sync.ApplyPendingAsync();
        Assert.True(await FavoriteInGetAsync());
    }

    [Fact]
    public async Task 許した住所のページには_書き込みを許しているときだけ書く方法も許す()
    {
        const string page = "http://localhost:5173";
        await StartAsync(allowWrites: true, origins: [page]);

        var preflight = new HttpRequestMessage(HttpMethod.Options, $"/v1/items/{ItemId}/favorite");
        preflight.Headers.Add("Origin", page);
        var allowed = (await _client.SendAsync(preflight)).Headers.GetValues("Access-Control-Allow-Methods").Single();
        Assert.Contains("PUT", allowed);

        JsonStore.Write(_store.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, AllowWrites = false, AllowedOrigins = [page] });
        _sync.Note(_store.Paths.ApiSettingsFile);
        await _sync.ApplyPendingAsync();

        var again = new HttpRequestMessage(HttpMethod.Options, $"/v1/items/{ItemId}/favorite");
        again.Headers.Add("Origin", page);
        Assert.DoesNotContain("PUT", (await _client.SendAsync(again)).Headers.GetValues("Access-Control-Allow-Methods").Single());

        // 手で切ったら、その場で書けなくなる
        Assert.Equal(HttpStatusCode.Forbidden, (await PutFavoriteAsync(true)).StatusCode);
    }
}

/// <summary>保存先を運ぶ間の門はプロセスに1つなので、門を閉じる試験はほかと並べずに走らせる。</summary>
[CollectionDefinition(nameof(StoreWriteGateCollection), DisableParallelization = true)]
public sealed class StoreWriteGateCollection;

[Collection(nameof(StoreWriteGateCollection))]
public sealed class ApiWriteGateTests : IAsyncLifetime
{
    private const string Token = "test-token-gate-0123456789";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-api-tests", Guid.NewGuid().ToString("N"));
    private WebApplication? _app;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var store = new DataStore(new AppPaths(_root));
        await store.Items.SaveAsync(new ItemRecord { Id = "9600002", Booth = new BoothBlock { Name = "作り物の手袋" } });
        JsonStore.Write(store.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, AllowWrites = true });
        var writes = new ApiWriteHooks((_, _) => Task.FromResult<CommandResult>(new CommandResult.Done()));
        (_app, _, _) = await ApiHost.BuildAsync(new ApiHostOptions(_root, 0, Token, [], Watch: false, Store: store, Writes: writes));
        await _app.StartAsync();
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

    [Fact]
    public async Task 保存先を運んでいる間は_待たせずに503で断る()
    {
        using (await StoreWriteGate.HoldAsync())
        {
            var response = await _client.PutAsync("/v1/items/9600002/favorite", new StringContent("""{"value":true}""", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("5", response.Headers.GetValues("Retry-After").Single());
            Assert.Equal("storeBusy", JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
        }
    }
}
