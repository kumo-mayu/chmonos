using System.Net;
using System.Net.Http.Headers;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using Chmonos.Core.Booth;
using Chmonos.Core.Commands;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Microsoft.AspNetCore.Builder;

namespace Chmonos.Api.Tests;

/// <summary>
/// API は BOOTH へ問い合わせない（docs/spec/local-api.md の守り）。2026-11-28 の点検で、これを見る試験が無かった。
/// - 組み立ての中身（参照している型）に、BOOTH へ問い合わせる型が無い事
/// - API が流す命令の種類が、BOOTH に触らない物だけである事
/// - 書き込みの口を本物の <see cref="CommandHandler"/> と商品の作業で通しても、BOOTH の通信の口に届かない事
/// 名前・ID は作り物。BOOTH への通信は作り物の口で受け、実際には出さない
/// </summary>
public sealed class ApiNoBoothTests : IAsyncLifetime
{
    private const string Token = "no-booth-token-0123456789";
    private const string ItemId = "9613579";

    /// <summary>BOOTH へ問い合わせる型（参照するだけで、API から BOOTH への道ができる）</summary>
    private static readonly string[] BoothTypes = [nameof(BoothClient), nameof(IBoothClient), "BoothMachineGate"];

    /// <summary>
    /// API が流してよい命令（BOOTH に触らない物）。命令を足すときは、BOOTH に触らない事を確かめてからここに足す
    /// （FetchBoothImage・RebuildItemFromBooth のような BOOTH へ問い合わせる命令を、API から流さない）
    /// </summary>
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        "AddAttribute", "AddUserTag", "ChangeAvatarLinks", "ChangeExtensionMemo", "ChangeExtensionSchema", "ChangeExtensionValues",
        "ConfirmAvatars", "CreateModification", "MarkNotificationsRead", "ResolveNotifications", "SaveItemLocal",
        "SetModificationMemberDetached", "SetNotificationRead", "TouchRecent", "ChangeItemLocal", "SetModificationMemo",
        "SetModificationBlueprintId", "SetModificationProject", "AddModificationMember",
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-api-tests", Guid.NewGuid().ToString("N"));
    private readonly CountingBooth _booth = new();
    private WebApplication? _app;
    private DataStore _store = null!;
    private HttpClient _client = null!;
    private bool _editing;

    public async Task InitializeAsync()
    {
        _store = new DataStore(new AppPaths(_root));
        JsonStore.Write(_store.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, AllowWrites = true });
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { Name = "作り物の帽子" },
            Local = new LocalBlock { Memo = "作り物のメモ" },
        });

        // 待ちを渡すと PC で1つの門を使わない（作り物の口なので BOOTH へ出ない）
        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var booth = new BoothClient(new HttpClient(_booth), settings, (_, _) => Task.CompletedTask);
        var items = new ItemService(_store, booth, new ImagePipeline(booth, _store.Paths, settings));
        var handler = new CommandHandler(null!, items, new EditService(_store));
        var hooks = new ApiWriteHooks(
            (command, token) => handler.ExecuteAsync(command, cancellationToken: token, origin: CommandOrigin.Api),
            ClaimItem: _ => _editing ? null : new Claim());
        (_app, _, _) = await ApiHost.BuildAsync(new ApiHostOptions(_root, 0, Token, [], Watch: false, Store: _store, Writes: hooks));
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{new Uri(_app.Urls.Single()).Port}"), Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>組み立てが参照している型の名前（入れ子は「外の型+中の型」）</summary>
    private static List<string> ReferencedTypes(Type inAssembly)
    {
        using var stream = File.OpenRead(inAssembly.Assembly.Location);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var names = new List<string>();
        foreach (var handle in reader.TypeReferences)
        {
            var type = reader.GetTypeReference(handle);
            var name = reader.GetString(type.Name);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                name = reader.GetString(reader.GetTypeReference((TypeReferenceHandle)type.ResolutionScope).Name) + "+" + name;
            }

            names.Add(name);
        }

        return names;
    }

    [Fact]
    public void APIと問い合わせの組み立ては_BOOTHへ問い合わせる型を参照しない()
    {
        foreach (var assembly in new[] { typeof(ApiHost), typeof(Chmonos.Query.ImportProgress) })
        {
            var referenced = ReferencedTypes(assembly);
            Assert.True(referenced.Count > 50, $"{assembly.Assembly.GetName().Name} の参照が読めていない");
            Assert.DoesNotContain(referenced, name => BoothTypes.Contains(name.Split('+')[^1]));
        }
    }

    [Fact]
    public void APIが流す命令は_BOOTHに触らない物だけ()
    {
        var commands = ReferencedTypes(typeof(ApiHost))
            .Where(name => name.StartsWith(nameof(UiCommand) + "+", StringComparison.Ordinal))
            .Select(name => name[(nameof(UiCommand).Length + 1)..]).Distinct().ToList();
        Assert.NotEmpty(commands);
        var unknown = commands.Where(command => !AllowedCommands.Contains(command)).ToList();
        Assert.True(unknown.Count == 0, "許していない命令：" + string.Join("、", unknown));
    }

    [Fact]
    public async Task 書き込みは本物の命令の口を通り_ほかの欄を残し_BOOTHへ問い合わせない()
    {
        using (var answer = await _client.SendAsync(Put($"/v1/items/{ItemId}/favorite", """{"value":true}""")))
        {
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        }

        var saved = (await _store.Items.LoadAsync(ItemId))!;
        Assert.True(saved.Local.IsFavorite);
        Assert.Equal("作り物のメモ", saved.Local.Memo);
        Assert.Equal(0, _booth.Calls);
    }

    [Fact]
    public async Task 保存先を運んでいる間と編集画面で開いている間は_本物の命令の口でも書かない()
    {
        var before = await File.ReadAllTextAsync(_store.Paths.ItemFile(ItemId));
        using (await StoreWriteGate.HoldAsync().WaitAsync(TimeSpan.FromSeconds(10)))
        {
            using var busy = await _client.SendAsync(Put($"/v1/items/{ItemId}/favorite", """{"value":true}"""));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
            Assert.Contains("storeBusy", await busy.Content.ReadAsStringAsync());
        }

        _editing = true;
        using var editing = await _client.SendAsync(Put($"/v1/items/{ItemId}/favorite", """{"value":true}"""));
        Assert.Equal(HttpStatusCode.Conflict, editing.StatusCode);
        Assert.Contains("itemBeingEdited", await editing.Content.ReadAsStringAsync());

        Assert.Equal(before, await File.ReadAllTextAsync(_store.Paths.ItemFile(ItemId)));
        Assert.Equal(0, _booth.Calls);
    }

    private static HttpRequestMessage Put(string path, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return request;
    }

    private sealed class Claim : IDisposable
    {
        public void Dispose()
        {
        }
    }

    /// <summary>BOOTH への通信を受ける作り物。届いたら数え、答えずに断る</summary>
    private sealed class CountingBooth : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
