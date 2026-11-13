using System.IO.Pipelines;
using System.Text.Json;
using Chmonos.Api;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Microsoft.AspNetCore.Builder;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Chmonos.Mcp.Tests;

/// <summary>
/// 試験の台：一時フォルダの保存先で API を 127.0.0.1 に立て、札の置き場とトークンの控えも一時フォルダに置く
/// （本物の <c>%LOCALAPPDATA%\Chmonos.Shared</c> を読まない・書かない。通信は自分で立てた API だけ）。
/// 通訳とは MCP の公式の使い手（McpClient）でメモリの流れ越しに話す。名前は作り物だけ
/// </summary>
internal sealed class McpHarness : IAsyncDisposable
{
    public const string ClientName = "作り物の道具";

    public readonly string Root = Path.Combine(Path.GetTempPath(), "chmonos-mcp-tests", Guid.NewGuid().ToString("N"));
    public readonly FakeApprovals Approvals = new();
    public readonly StringWriter Log = new();
    private readonly List<WebApplication> _apps = [];
    private readonly List<(McpClient Client, Task Server, CancellationTokenSource Cancel)> _sessions = [];

    public McpHarness()
    {
        Directory.CreateDirectory(Root);
    }

    public string RunningDirectory => Path.Combine(Root, "running");

    public string TokenFile => Path.Combine(Root, "shared", "mcp-tokens.json");

    /// <summary>保存先を作って API を立てる。札は書かない（<see cref="Announce"/> で書く）。返すのはポート。</summary>
    public async Task<(string StoreRoot, int Port)> StartApiAsync(string name, bool showAdult = true, bool canAsk = true, Action<DataStore>? fill = null)
    {
        var storeRoot = Path.Combine(Root, name);
        var store = new DataStore(new AppPaths(storeRoot));
        await store.Items.SaveAsync(new ItemRecord { Id = "9700001", Booth = new BoothBlock { Name = "作り物の帽子" } });
        await store.Items.SaveAsync(new ItemRecord { Id = "9700002", Booth = new BoothBlock { Name = "作り物の隠した靴" }, Local = new LocalBlock { IsHidden = true } });
        await store.Items.SaveAsync(new ItemRecord { Id = "9700003", Booth = new BoothBlock { Name = "作り物の大人の服", IsAdult = true } });
        JsonStore.Write(store.Paths.ApiSettingsFile, new ApiSettings { Enabled = true });
        JsonStore.Write(store.Paths.SettingsFile, new AppSettings { ShowAdult = showAdult });
        fill?.Invoke(store);

        Approvals.CanAsk = canAsk;
        var writes = new ApiWriteHooks((_, _) => Task.FromResult<CommandResult>(new CommandResult.Done()), Approvals: Approvals);
        var (app, _, _) = await ApiHost.BuildAsync(new ApiHostOptions(
            storeRoot, 0, "test-token-mcp-legacy-0123456789", [], Watch: false, Store: store, Writes: writes, Owners: new FakeOwners()));
        await app.StartAsync();
        _apps.Add(app);
        return (storeRoot, new Uri(app.Urls.Single()).Port);
    }

    /// <summary>動いている Chmonos の札を書く（このプロセスの番号で書く。生きているかの確かめは試験の作り物で答える）。</summary>
    public void Announce(string storeRoot, int? port, int processId)
        => RunningInstances.Write(RunningDirectory, new RunningInstanceRecord
        {
            ProcessId = processId,
            StartedAt = DateTimeOffset.Now,
            StoreRoot = storeRoot,
            Port = port,
            ApiFile = Path.Combine(storeRoot, "api.json"),
            Version = "1.2.0",
        });

    public McpOptions Options(string? store = null, TimeSpan? wait = null)
        => new(store, RunningDirectory, TokenFile, wait ?? TimeSpan.FromSeconds(3));

    /// <summary>通訳を立て、MCP の使い手でつなぐ。札の番号はどれも生きていると答える（落ちた札は <paramref name="dead"/> に）。</summary>
    public async Task<McpClient> ConnectAsync(McpOptions options, string clientName = ClientName, params int[] dead)
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var cancel = new CancellationTokenSource();
        // 札には作り物の番号を書き、API は試験のプロセスの中で待ち受ける。ポートの持ち主は本物の TCP の表で読み、試験のプロセスならどの札の本人とも見る
        var bridge = new ChmonosBridge(
            options,
            Log,
            isAlive: (processId, _) => !dead.Contains(processId),
            listensAs: (port, _) => PortOwner.OwnerOf(port) is { } owner ? owner == Environment.ProcessId : null);
        var server = Task.Run(async () =>
        {
            using (bridge)
            {
                var transport = new ModelContextProtocol.Server.StreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream(), "chmonos");
                await using (transport)
                {
                    await using var mcp = ChmonosMcpServer.Create(transport, bridge);
                    try
                    {
                        await mcp.RunAsync(cancel.Token);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
        });

        var client = await McpClient.CreateAsync(
            new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()),
            new McpClientOptions { ClientInfo = new Implementation { Name = clientName, Version = "1.0" } });
        _sessions.Add((client, server, cancel));
        return client;
    }

    public static async Task<(bool IsError, string Text)> CallAsync(McpClient client, string tool, object? args = null)
    {
        var arguments = args is null
            ? new Dictionary<string, object?>()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(args))!;
        var result = await client.CallToolAsync(tool, arguments);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        return (result.IsError == true, text);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (client, server, cancel) in _sessions)
        {
            await client.DisposeAsync();
            cancel.Cancel();
            await server.WaitAsync(TimeSpan.FromSeconds(10)).ContinueWith(_ => { });
            cancel.Dispose();
        }

        foreach (var app in _apps)
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    internal sealed class FakeOwners : IConnectionOwners
    {
        public string? ExePathOf(int serverPort, int clientPort) => @"C:\作り物\Chmonos\Chmonos.exe";
    }

    /// <summary>作り物の聞く口：聞かれた物を並べ、試験が答えるまで待つ。</summary>
    internal sealed class FakeApprovals : IApiClientApprovals
    {
        private readonly System.Threading.Channels.Channel<Asked> _asked = System.Threading.Channels.Channel.CreateUnbounded<Asked>();

        public bool CanAsk { get; set; } = true;

        public int AskedCount;

        public async Task<ApiClientDecision> AskAsync(ApiClientRequestInfo request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref AskedCount);
            var asked = new Asked(request);
            await _asked.Writer.WriteAsync(asked, cancellationToken);
            return await asked.Decision.Task.WaitAsync(cancellationToken);
        }

        public async Task<Asked> NextAsync() => await _asked.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        /// <summary>次に聞かれたら許す（裏で待って答える）。</summary>
        public Task ApproveNextAsync() => Task.Run(async () => (await NextAsync()).Decision.TrySetResult(new ApiClientDecision(true, [])));
    }

    internal sealed class Asked(ApiClientRequestInfo info)
    {
        public ApiClientRequestInfo Info { get; } = info;

        public TaskCompletionSource<ApiClientDecision> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
