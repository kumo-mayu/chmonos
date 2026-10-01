using Chmonos.Core.Booth;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

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

    private static AvatarRegistry NotFoundEntry(string id, int daysAgo) => new()
    {
        Entries =
        [
            new AvatarRegistryEntry
            {
                ItemId = id,
                Category = null,
                CheckedAt = DateTimeOffset.Now.AddDays(-daysAgo),
                DisplayName = "手で付けた名前",
                Memo = "メモ",
            },
        ],
    };

    /// <summary>
    /// 404 だった項目は、30日を過ぎたら問い合わせ直し、再公開されていたら名前とカテゴリを埋める。
    /// 以前は一度 404 で入ると二度と問い合わせなかった。
    /// </summary>
    [Fact]
    public async Task AsksAgainAboutANotFoundEntryAfter30Days()
    {
        const string id = "3001";
        await SeedAsync([id], NotFoundEntry(id, daysAgo: 31));
        var client = new ScriptedClient();

        await new AvatarService(_store, client: client).DetectAsync();

        Assert.Equal([id], client.Asked);
        var entry = Entry(id)!;
        Assert.Equal("3Dキャラクター", entry.Category);
        Assert.Equal($"アバター{id}", entry.BoothName);
        Assert.True(entry.CheckedAt > DateTimeOffset.Now.AddDays(-1));
        // 人が付けた物は残す
        Assert.Equal("手で付けた名前", entry.DisplayName);
        Assert.Equal("メモ", entry.Memo);
    }

    /// <summary>30日以内の 404 は問い合わせない（問い合わせを増やす向きなので、間隔を守る）。</summary>
    [Fact]
    public async Task DoesNotAskAboutANotFoundEntryWithin30Days()
    {
        const string id = "3001";
        await SeedAsync([id], NotFoundEntry(id, daysAgo: 29));
        var client = new ScriptedClient();

        await new AvatarService(_store, client: client).DetectAsync();

        Assert.Empty(client.Asked);
        Assert.Null(Entry(id)!.Category);
    }

    /// <summary>まだ 404 なら、確かめた日だけ進める（次の30日は問い合わせない）。</summary>
    [Fact]
    public async Task MovesTheCheckedDateWhenStillNotFound()
    {
        const string id = "3001";
        await SeedAsync([id], NotFoundEntry(id, daysAgo: 31));
        var client = new ScriptedClient { Answer = _ => BoothFetchResult<string>.NotFound() };

        await new AvatarService(_store, client: client).DetectAsync();

        Assert.Single(client.Asked);
        var entry = Entry(id)!;
        Assert.Null(entry.Category);
        Assert.Null(entry.BoothName);
        Assert.True(entry.CheckedAt > DateTimeOffset.Now.AddDays(-1));
        Assert.Equal("手で付けた名前", entry.DisplayName);
    }

    /// <summary>手元に持っている商品の項目は、カテゴリが無くても問い合わせ直さない（その商品は⑦が取り直す）。</summary>
    [Fact]
    public async Task DoesNotAskAgainAboutAnItemInTheLibrary()
    {
        const string id = "3001";
        await SeedAsync([id], NotFoundEntry(id, daysAgo: 400));
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "カテゴリの無い商品" },
            Local = new LocalBlock(),
        });
        var client = new ScriptedClient();

        await new AvatarService(_store, client: client).DetectAsync();

        Assert.Empty(client.Asked);
    }

    /// <summary>
    /// 404 で空の項目ができた商品を、あとで買って取り込んだら、手元の情報で埋める。問い合わせない（ユーザ判断 2026-09-29）。
    /// 前は手元の情報で埋めるのが項目の無いときだけで、404 の確かめ直しからも外れるので、ずっと空のまま残った
    /// </summary>
    [Fact]
    public async Task FillsANotFoundEntryFromTheLibraryWithoutAsking()
    {
        const string id = "3001";
        await SeedAsync([id], NotFoundEntry(id, daysAgo: 5));
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock
            {
                FetchedAt = DateTimeOffset.Now,
                Name = "再公開されたアバター",
                Category = new BoothCategory { Id = 208, Name = "3Dキャラクター" },
                Shop = new BoothShop { Name = "手元のショップ", Subdomain = "local-shop" },
            },
            Local = new LocalBlock(),
        });
        var client = new ScriptedClient();

        await new AvatarService(_store, client: client).DetectAsync();

        Assert.Empty(client.Asked);
        var entry = Entry(id)!;
        Assert.Equal("3Dキャラクター", entry.Category);
        Assert.Equal("再公開されたアバター", entry.BoothName);
        Assert.Equal("手元のショップ", entry.ShopName);
        // 人が付けた物は残す
        Assert.Equal("手で付けた名前", entry.DisplayName);
        Assert.Equal("メモ", entry.Memo);
    }

    /// <summary>手元の商品にもカテゴリが無いときは、空のまま（埋める材料が無い）。</summary>
    [Fact]
    public async Task LeavesANotFoundEntryEmptyWhenTheLibraryHasNoCategoryEither()
    {
        const string id = "3001";
        await SeedAsync([id], NotFoundEntry(id, daysAgo: 5));
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "カテゴリの無い商品" },
            Local = new LocalBlock(),
        });

        await new AvatarService(_store, client: new ScriptedClient()).DetectAsync();

        Assert.Null(Entry(id)!.Category);
    }

    [Fact]
    public void RecheckIsDueOnlyForNotFoundEntriesOutsideTheLibraryAfterTheInterval()
    {
        var now = new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.FromHours(9));
        var notFound = new AvatarRegistryEntry { ItemId = "1", CheckedAt = now.AddDays(-30) };

        Assert.True(AvatarService.IsNotFoundRecheckDue(notFound, inLibrary: false, intervalDays: 30, now));
        Assert.False(AvatarService.IsNotFoundRecheckDue(notFound with { CheckedAt = now.AddDays(-29) }, inLibrary: false, intervalDays: 30, now));
        Assert.False(AvatarService.IsNotFoundRecheckDue(notFound, inLibrary: true, intervalDays: 30, now));
        Assert.False(AvatarService.IsNotFoundRecheckDue(notFound with { Category = "衣装" }, inLibrary: false, intervalDays: 30, now));
    }

    /// <summary>
    /// ネットにつながらないのが3件続いたら、残りは問い合わせずに抜け、打ち切ったことを返す。
    /// 問い合わせなかったIDは登録簿に何も書かない（次の検出でまた試す）。
    /// </summary>
    [Fact]
    public async Task StopsAskingWhenBoothCannotBeReached()
    {
        await SeedAsync(Ids(10));
        var client = new ScriptedClient { Answer = _ => BoothFetchResult<string>.Unreachable("試験") };

        var result = await new AvatarService(_store, client: client).DetectAsync();

        Assert.Equal(BoothOutageWatch.Limit, client.Asked.Count);
        Assert.Equal(BoothOutageKind.Offline, result.Outage);
        Assert.Equal(10, result.Unresolved);
        Assert.Empty(_store.Avatars.Load().Entries);
    }

    /// <summary>BOOTH が 5xx を返し続けても打ち切る（落ちている相手へ問い合わせを重ねない）。</summary>
    [Fact]
    public async Task StopsAskingWhenBoothKeepsFailing()
    {
        await SeedAsync(Ids(10));
        var client = new ScriptedClient
        {
            Answer = _ => new BoothFetchResult<string>
            {
                Status = BoothFetchStatus.TemporaryFailure,
                Error = "HTTP 503",
                IsServerError = true,
            },
        };

        var result = await new AvatarService(_store, client: client).DetectAsync();

        Assert.Equal(BoothOutageWatch.Limit, client.Asked.Count);
        Assert.Equal(BoothOutageKind.ServerDown, result.Outage);
    }

    /// <summary>間に取れた物があれば数え直す（1件だけ届かない商品で全体を止めない）。</summary>
    [Fact]
    public async Task CountsAgainAfterASuccess()
    {
        await SeedAsync(Ids(9));
        var client = new ScriptedClient();
        client.Answer = id => client.Asked.Count % 3 == 0
            ? BoothFetchResult<string>.Success(AvatarJson(id))
            : BoothFetchResult<string>.Unreachable("試験");

        var result = await new AvatarService(_store, client: client).DetectAsync();

        Assert.Equal(BoothOutageKind.None, result.Outage);
        // 届かないのは2件ずつしか続かないので、1回目で9件を全部聞いている
        Assert.Equal(9, client.Asked.Take(9).Distinct().Count());
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
