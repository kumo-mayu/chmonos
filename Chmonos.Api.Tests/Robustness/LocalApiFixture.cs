using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Microsoft.AspNetCore.Builder;

namespace Chmonos.Api.Tests.Robustness;

/// <summary>
/// 意地の悪い条件で API の守りを確かめる試験の土台（2026-11-13 の Codex の点検 attack で書いた物を移した）。
/// 保存先は一時フォルダ、通信先は自分で立てた 127.0.0.1、許しの窓と接続元の exe は作り物。ソフト a・b を許した控えから始める。名前・ID は作り物
/// </summary>
public abstract class LocalApiFixture : IAsyncLifetime
{
    protected const string TokenA = "robustness-token-a-0123456789";
    protected const string TokenB = "robustness-token-b-0123456789";
    protected const string AllowedOrigin = "https://tool.example";
    protected readonly string Root = Path.Combine(Path.GetTempPath(), "chmonos-api-tests", Guid.NewGuid().ToString("N"));
    protected DateTimeOffset Now = new(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
    protected DataStore Store = null!;
    internal ApiState State = null!;
    internal ApiClientRequests Requests = null!;
    protected PluginEventHub Hub = null!;
    protected HttpClient Client = null!;
    protected readonly FakeApprovals Approvals = new();
    private WebApplication? _app;

    protected ApiClientRecord Record(string id, string token) => new()
    {
        Id = id, Name = "作り物の道具" + id, TokenHash = ApiClientBook.HashOf(token),
        Scopes = ApiScopes.Watches, GrantedAt = Now, LastUsedAt = Now, LastPolledAt = Now,
        Menu = [new PluginMenuItemRecord { Id = "preview", Label = "確認", Where = ["item"], Needs = ["name"] }],
    };

    public async Task InitializeAsync()
    {
        Store = new DataStore(new AppPaths(Root));
        JsonStore.Write(Store.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, AllowedOrigins = [AllowedOrigin] });
        JsonStore.Write(Store.Paths.ApiClientsFile, new ApiClientList { Clients = [Record("a", TokenA), Record("b", TokenB)] });
        await Store.Items.SaveAsync(new ItemRecord { Id = "9600901", Booth = new BoothBlock { Name = "作り物の試験商品" } });
        Hub = new PluginEventHub(() => Now);
        (_app, State, _) = await ApiHost.BuildAsync(new ApiHostOptions(
            Root, 0, "robustness-legacy-0123456789", [AllowedOrigin],
            Clock: () => Now, Watch: false, Store: Store,
            Writes: new ApiWriteHooks((_, _) => Task.FromResult<Chmonos.Core.Commands.CommandResult>(new Chmonos.Core.Commands.CommandResult.Done()), Approvals: Approvals),
            Owners: new FakeOwners(), Plugins: Hub));
        await _app.StartAsync();
        Requests = (ApiClientRequests)_app.Services.GetService(typeof(ApiClientRequests))!;
        Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{new Uri(_app.Urls.Single()).Port}"), Timeout = TimeSpan.FromSeconds(10) };
    }

    protected HttpRequestMessage Request(HttpMethod method, string path, string? body = null, string? token = TokenA)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }

    protected async Task<JsonElement> GetJson(string path, string token = TokenA)
    {
        using var request = Request(HttpMethod.Get, path, token: token);
        using var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    protected async Task Subscribe(string token = TokenA)
    {
        using var request = Request(HttpMethod.Put, "/v1/plugin/subscriptions", """{"kinds":["itemOpened","selectionChanged"]}""", token);
        using var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    protected async Task ReplaceClients(params ApiClientRecord[] clients)
    {
        await Store.ApiClients.UpdateAsync(list => list with { Clients = clients });
        State.Clients.Reload();
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        // 試験ごとの GUID のフォルダだけを片付ける
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }

    private sealed class FakeOwners : IConnectionOwners
    {
        public string? ExePathOf(int serverPort, int clientPort) => null;
    }

    protected sealed class FakeApprovals : IApiClientApprovals
    {
        private readonly System.Threading.Channels.Channel<TaskCompletionSource<ApiClientDecision>> _asked =
            System.Threading.Channels.Channel.CreateUnbounded<TaskCompletionSource<ApiClientDecision>>();
        public bool CanAsk => true;
        public Task<ApiClientDecision> AskAsync(ApiClientRequestInfo request, CancellationToken cancellationToken)
            => Task.FromResult(ApiClientDecision.Dismissed);
        public async Task<ApiClientDecision> AskMenuAsync(PluginMenuRequestInfo request, CancellationToken cancellationToken)
        {
            var decision = new TaskCompletionSource<ApiClientDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _asked.Writer.WriteAsync(decision, cancellationToken);
            return await decision.Task.WaitAsync(cancellationToken);
        }
        public Task<TaskCompletionSource<ApiClientDecision>> Next()
            => _asked.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }
}
