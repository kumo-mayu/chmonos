using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 対応アバターの検出が BOOTH へ問い合わせる所（登録簿に無いIDの確かめ）の試験。通信はしない（作り物の問い合わせ先）。
/// </summary>
public class AvatarDetectFetchTests : IDisposable
{
    private const string ItemId = "999";

    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly DataStore _store;

    public AvatarDetectFetchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-detect-fetch-" + Guid.NewGuid().ToString("N"));
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new DataStore(_paths);
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

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 問い合わせのたびに <see cref="OnCall"/> を呼び、<see cref="Answer"/> の答えを返す作り物。
    /// 何番目の問い合わせかと、どのIDを聞かれたかを控える。
    /// </summary>
    private sealed class ScriptedClient : IBoothClient
    {
        public List<string> Asked { get; } = [];

        public Func<string, BoothFetchResult<string>> Answer { get; set; } = id => BoothFetchResult<string>.Success(AvatarJson(id));

        public Action<int, string, CancellationToken>? OnCall { get; set; }

        public Task<BoothFetchResult<string>> GetItemJsonAsync(string itemId, CancellationToken cancellationToken = default)
        {
            Asked.Add(itemId);
            OnCall?.Invoke(Asked.Count, itemId, cancellationToken);
            return Task.FromResult(Answer(itemId));
        }

        public Task<BoothFetchResult<string>> GetItemHtmlAsync(string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BoothFetchResult<byte[]>> GetBinaryAsync(string url, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BoothFetchResult<string>> SearchAsync(string query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BoothFetchResult<string>> GetTextUntilAsync(
            string url, Func<string, bool> found, int maxBytes = 262144, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public int CurrentIntervalMs => 1500;

        public bool IsThrottled => false;

        public event Action<BoothActivity>? ActivityChanged
        {
            add { }
            remove { }
        }
    }

    private static string AvatarJson(string id)
        => $$$"""{"name":"アバター{{{id}}}","category":{"name":"3Dキャラクター"},"shop":{"name":"テストの店"}}""";

    private static List<string> Ids(int count) => Enumerable.Range(1, count).Select(n => (2000 + n).ToString()).ToList();

    /// <summary>対応アバターの節に <paramref name="avatarIds"/> を挙げた商品を1件置く。</summary>
    private async Task SeedAsync(IEnumerable<string> avatarIds, AvatarRegistry? registry = null)
    {
        if (registry is not null)
        {
            await _store.Avatars.SaveAsync(registry);
        }

        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "ネイルチップ" },
            Local = new LocalBlock(),
        });

        var lines = string.Concat(avatarIds.Select(id => $"<p>https://booth.pm/ja/items/{id}</p>"));
        File.WriteAllText(_paths.ItemHtmlFile(ItemId), $"<h2>対応アバター</h2>{lines}");
    }

    private AvatarRegistryEntry? Entry(string id) => _store.Avatars.Load().Entries.SingleOrDefault(entry => entry.ItemId == id);

    /// <summary>
    /// 途中で中止しても、それまでに問い合わせて分かった項目は登録簿に残る。
    /// 最後に1回だけ書いていたので、中止すると全部捨てられ、次の検出がまた最初から問い合わせていた。
    /// </summary>
    [Fact]
    public async Task KeepsWhatWasFetchedWhenCancelled()
    {
        await SeedAsync(Ids(5));
        using var stop = new CancellationTokenSource();
        var client = new ScriptedClient
        {
            OnCall = (call, _, token) =>
            {
                if (call == 4)
                {
                    stop.Cancel();
                    token.ThrowIfCancellationRequested();
                }
            },
        };
        var service = new AvatarService(_store, client: client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DetectAsync(cancellationToken: stop.Token));

        foreach (var id in client.Asked.Take(3))
        {
            Assert.Equal($"アバター{id}", Entry(id)?.BoothName);
            Assert.Equal("3Dキャラクター", Entry(id)?.Category);
        }

        // 中止した問い合わせの分は何も書かない（次回また問い合わせる）
        Assert.Null(Entry(client.Asked[3]));

        // 最後にまとめて書く物は書かない。検出を終えたことにすると、起動時の検出し直しが遅れる
        Assert.Null(_store.Avatars.Load().DetectedAt);
        Assert.All(_store.Avatars.Load().Entries, entry => Assert.Empty(entry.SeenAs));

        // 次の検出は残りだけを問い合わせる
        var again = new ScriptedClient();
        await new AvatarService(_store, client: again).DetectAsync();
        Assert.Equal(2, again.Asked.Count);
        Assert.DoesNotContain(again.Asked, client.Asked.Take(3).Contains);
    }

    /// <summary>
    /// 問い合わせが例外で止まっても、それまでの分は残る（閉じる・想定外の失敗）。
    /// </summary>
    [Fact]
    public async Task KeepsWhatWasFetchedWhenSomethingThrows()
    {
        await SeedAsync(Ids(3));
        var client = new ScriptedClient
        {
            OnCall = (call, _, _) =>
            {
                if (call == 3)
                {
                    throw new InvalidOperationException("試験");
                }
            },
        };
        var service = new AvatarService(_store, client: client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DetectAsync());

        Assert.NotNull(Entry(client.Asked[0]));
        Assert.NotNull(Entry(client.Asked[1]));
        Assert.Null(Entry(client.Asked[2]));
    }

    /// <summary>
    /// 多く問い合わせる回は、止めなくても途中で書いている（強制終了で失うのを抑える）。
    /// 途中で書いた後に人が付けた名前は、次に途中で書くときにも、最後に書くときにも消さない。
    /// </summary>
    [Fact]
    public async Task WritesAlongTheWayAndKeepsANameGivenMeanwhile()
    {
        await SeedAsync(Ids(25));
        var registryWritesSeen = 0;
        AvatarService? service = null;
        var client = new ScriptedClient();
        client.OnCall = (call, _, _) =>
        {
            if (call == 21)
            {
                // 20件目の後に書いてある。書いた項目に人が名前を付ける
                registryWritesSeen = _store.Avatars.Load().Entries.Count;
                service!.SetDisplayNameAsync(client.Asked[0], "手で付けた名前").GetAwaiter().GetResult();
            }
        };
        service = new AvatarService(_store, client: client);

        await service.DetectAsync();

        Assert.Equal(20, registryWritesSeen);
        Assert.Equal("手で付けた名前", Entry(client.Asked[0])?.DisplayName);
        Assert.Equal(25, _store.Avatars.Load().Entries.Count);
        Assert.NotNull(_store.Avatars.Load().DetectedAt);
    }

    /// <summary>途中で書くときは、人が消した別名の印・メモを最新のまま残す（最新に重ねる）。</summary>
    [Fact]
    public void MergeFetchedKeepsWhatAPersonChanged()
    {
        var snapshot = new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = "1", Category = null, CheckedAt = DateTimeOffset.Now.AddDays(-40) }],
        };
        var latest = new AvatarRegistry
        {
            DetectedAt = DateTimeOffset.Now.AddDays(-1),
            Entries =
            [
                new AvatarRegistryEntry
                {
                    ItemId = "1",
                    Category = null,
                    CheckedAt = DateTimeOffset.Now.AddDays(-40),
                    DisplayName = "手で付けた名前",
                    Memo = "メモ",
                },
            ],
            BaseGroups = [new AvatarBaseGroup { Name = "手で足した素体", IsManual = true }],
        };
        var fetched = new Dictionary<string, AvatarRegistryEntry>
        {
            ["1"] = new() { ItemId = "1", BoothName = "戻ってきたアバター", Category = "3Dキャラクター", CheckedAt = DateTimeOffset.Now },
            ["2"] = new() { ItemId = "2", BoothName = "新しいアバター", Category = "3Dキャラクター", CheckedAt = DateTimeOffset.Now },
        };

        var merged = AvatarService.MergeFetched(latest, snapshot, fetched);

        var one = merged.Entries.Single(entry => entry.ItemId == "1");
        Assert.Equal("手で付けた名前", one.DisplayName);
        Assert.Equal("メモ", one.Memo);
        Assert.Equal("戻ってきたアバター", one.BoothName);
        Assert.Equal("3Dキャラクター", one.Category);
        Assert.Contains(merged.Entries, entry => entry.ItemId == "2");
        Assert.Equal(latest.DetectedAt, merged.DetectedAt);
        Assert.Single(merged.BaseGroups);
    }
}
