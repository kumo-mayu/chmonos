using System.Net;
using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 新しい商品を作る道（取り込みの①・未確定の「BOOTHに無い商品として登録」）と、人の保存が同じ商品で重なっても、
/// 人が入れた名前・メモ・購入記録が消えない（2026-10-02）。
///
/// 前は**錠の外で在るかを見て**、無ければ丸ごと保存していた。見てから書くまでの間に人の保存が同じ商品を作ると、
/// 作り直した空の商品で丸ごと上書きしていた。
///
/// 重なりは時計で作らない：人の保存が商品の錠を持ったまま止まり（変え方の関数の中で待つ）、
/// 相手が同じ錠の前まで来た（錠を持つ・待つ人が2人になった）のを見てから放す。
/// </summary>
public sealed class CreateWhileSomeoneSavesTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 2, 12, 0, 0, TimeSpan.FromHours(9));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-create-while-saving-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly AppSettings _settings = new() { FetchIntervalMs = 0 };
    private readonly BoothClient _client;

    /// <summary>BOOTH から商品の情報を返す直前に呼ぶ（取り込みの①の最中に人の保存を始めるため）。</summary>
    private Action<string>? _duringFetch;

    public CreateWhileSomeoneSavesTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);
        _client = new BoothClient(new HttpClient(new FakeBooth(this)), _settings, TestWait.None);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class FakeBooth(CreateWhileSomeoneSavesTests owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (url.EndsWith(".json", StringComparison.Ordinal))
            {
                var id = url.Split('/')[^1].Replace(".json", string.Empty, StringComparison.Ordinal);
                owner._duringFetch?.Invoke(id);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                        {
                          "id": {{id}},
                          "name": "商品 {{id}}",
                          "url": "https://booth.pm/ja/items/{{id}}",
                          "shop": { "name": "shop", "subdomain": "shop", "thumbnail_url": "", "url": "https://shop.booth.pm/" },
                          "images": [],
                          "variations": [ { "id": 1, "name": null, "price": 100 } ]
                        }
                        """),
                });
            }

            // 商品ページ・画像は中身の無い物でよい（ここで見るのは①の書き込みだけ）
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html><body></body></html>") });
        }
    }

    /// <summary>人が入れた物。</summary>
    private static LocalBlock PersonEntered() => new()
    {
        DisplayName = "人が入れた名前",
        Memo = "人が書いたメモ",
        Purchases = [new Purchase { Price = 500, Note = "人が入れた購入記録" }],
    };

    /// <summary>
    /// 人の保存を始め、商品の錠を持ったまま止める。<see cref="ReleaseWhenQueued"/> で、相手が錠の前まで来たら放す。
    /// </summary>
    private (Task<bool> Saved, ManualResetEventSlim Release) HoldPersonSave(string itemId)
    {
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var saved = Task.Run(() => _store.Items.CreateOrChangeLocalAsync(
            itemId,
            () => new ItemRecord { Id = itemId, Booth = new BoothBlock(), Local = PersonEntered() },
            local =>
            {
                entered.Set();
                release.Wait();
                return local;
            },
            [LocalField.DisplayName, LocalField.Memo, LocalField.Purchases]));

        // 止まらずに終わった（投げた）ときに待ち続けないための上限。重なりを作るのは時計ではなく、錠の中で止めること
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)), "人の保存が錠の中まで来なかった");
        return (saved, release);
    }

    /// <summary>相手がその商品の錠の前まで来たら（持つ・待つ人が2人）、止めていた人の保存を放す。</summary>
    private Task ReleaseWhenQueued(string itemId, ManualResetEventSlim release, Task other) => Task.Run(async () =>
    {
        while (_store.Items.LockUsers(itemId) < 2 && !other.IsCompleted)
        {
            await Task.Yield();
        }

        release.Set();
    });

    private static void AssertPersonKept(LocalBlock local, bool keepsName = true)
    {
        if (keepsName)
        {
            Assert.Equal("人が入れた名前", local.DisplayName);
        }

        Assert.Equal("人が書いたメモ", local.Memo);
        Assert.Equal("人が入れた購入記録", Assert.Single(local.Purchases).Note);
    }

    /// <summary>
    /// 取り込みの①：BOOTH から取っている間に人が同じ商品を作っても、取ってきた商品で丸ごと上書きしない。
    /// 取ってきた booth と見つけたファイルだけを重ねる。
    /// </summary>
    [Fact]
    public async Task ImportDoesNotOverwriteAnItemAPersonCreatesMeanwhile()
    {
        var folder = Path.Combine(_root, "dl");
        Directory.CreateDirectory(folder);
        var zip = Path.Combine(folder, "item_111.zip");
        File.WriteAllText(zip, "111");
        File.WriteAllText(zip + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://booth.pm/ja/items/111\r\n");

        var importing = new TaskCompletionSource<Task>();
        Task? releasing = null;
        _duringFetch = id =>
        {
            if (id != "111" || releasing is not null)
            {
                return;
            }

            var (saved, release) = HoldPersonSave("111");
            releasing = Task.Run(async () =>
            {
                await ReleaseWhenQueued("111", release, await importing.Task);
                await saved;
            });
        };

        var pipeline = new ImportPipeline(_store, _client, new ImagePipeline(_client, _paths, _settings), _settings);
        var run = pipeline.RunAsync(new ImportWorkSet([folder]));
        importing.SetResult(run);
        await run;
        Assert.NotNull(releasing);
        await releasing!;

        var item = (await _store.Items.LoadAsync("111"))!;
        AssertPersonKept(item.Local);
        Assert.Equal("商品 111", item.Booth.Name);
        Assert.Contains(item.Local.LocalFiles, file => file.Paths.Contains(zip, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 未確定の「BOOTHに無い商品として登録」：同じ仮IDの商品を人が保存している最中でも、作り直した商品で丸ごと上書きしない。
    /// 名前とファイルは登録の決まりどおり登録の側が書く（名前は登録で付けた物になる）。
    /// </summary>
    [Fact]
    public async Task RegisteringDoesNotOverwriteAnItemAPersonCreatesMeanwhile()
    {
        var path = Path.Combine(_root, "files", "loose.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "loose");
        var file = new UnresolvedFile
        {
            Hash = "0a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f9",
            Paths = [path],
            SizeBytes = 5,
            ModifiedAtUtc = At,
            FirstSeenAt = At,
        };
        await _store.Unresolved.SaveAsync([file]);
        var itemId = LocalItemId.For(file.Hash);

        var service = new ItemService(_store, _client, new ImagePipeline(_client, _paths));
        var (saved, release) = HoldPersonSave(itemId);
        var registering = Task.Run(() => service.RegisterLocalItemAsync(file.Hash, "登録で付けた名前"));
        var releasing = ReleaseWhenQueued(itemId, release, registering);

        Assert.Equal(itemId, await registering);
        await releasing;
        await saved;

        var item = (await _store.Items.LoadAsync(itemId))!;
        AssertPersonKept(item.Local, keepsName: false);
        Assert.Equal("登録で付けた名前", item.Local.DisplayName);
        Assert.Equal(file.Hash, Assert.Single(item.Local.LocalFiles).Hash);
        Assert.Empty(_store.Unresolved.Load());
    }
}
