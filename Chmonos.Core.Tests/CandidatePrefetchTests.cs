using Chmonos.Core.Booth;
using Chmonos.Core.Resolution;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests;

/// <summary>
/// 未確定の自動検索の先回り（ユーザ判断 2026-11-10・2026-11-11）。検索の中身は差し替え、通信しない。
/// 新しい順・控えのある対象は飛ばす・⑧の段で引く・取り込みの間は止めて捨て、終わったら同じ対象から引き直す・
/// 届かなかったら打ち切る・設定がオフなら走らない、を確かめる。
/// </summary>
public sealed class CandidatePrefetchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bam-prefetch-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly CandidateSearchCache _cache;

    public CandidatePrefetchTests()
    {
        Directory.CreateDirectory(_dir);
        _cache = new CandidateSearchCache(new DataStore(new AppPaths(_dir)).UnresolvedSearches);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static readonly DateTimeOffset Day = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private static PrefetchTarget Target(string name, int day) => new($@"D:\dl\{name}", [$"hash-{name}"], Day.AddDays(day));

    private static ResolutionProposal Found(string id) => new([new ResolutionCandidate { ItemId = id, Score = 3 }], BoothUnreachable: false);

    [Fact]
    public async Task 新しく見つかった順に引き_控えのある対象は飛ばし_引いた物は控えて段は8()
    {
        await _cache.AddIfMissingAsync(@"D:\dl\b.zip", ["hash-b.zip"], Found("既に"), Day);

        var asked = new List<(string Target, BoothPriority Priority)>();
        var prefetch = new CandidatePrefetch(
            _cache,
            _ => Task.FromResult<IReadOnlyList<PrefetchTarget>>([Target("a.zip", 1), Target("b.zip", 3), Target("c.zip", 2)]),
            (target, _) =>
            {
                asked.Add((target.Target, BoothClient.CurrentPriority));
                return Task.FromResult(Found("1" + asked.Count));
            },
            () => true);

        var run = await prefetch.RunAsync();

        Assert.Equal(PrefetchEnd.Finished, run.End);
        Assert.Equal(2, run.Searched);
        Assert.Equal([@"D:\dl\c.zip", @"D:\dl\a.zip"], asked.Select(entry => entry.Target));
        Assert.All(asked, entry => Assert.Equal(BoothPriority.Prefetch, entry.Priority));
        Assert.NotNull(_cache.Find(@"D:\dl\a.zip"));
        Assert.Equal("既に", Assert.Single(_cache.Find(@"D:\dl\b.zip")!.Candidates).ItemId);
    }

    [Fact]
    public async Task 設定がオフなら何も引かない()
    {
        var asked = 0;
        var prefetch = new CandidatePrefetch(
            _cache,
            _ => Task.FromResult<IReadOnlyList<PrefetchTarget>>([Target("a.zip", 1)]),
            (_, _) =>
            {
                asked++;
                return Task.FromResult(Found("1"));
            },
            () => false);

        var run = await prefetch.RunAsync();

        Assert.Equal(PrefetchEnd.Disabled, run.End);
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task 取り込みが始まったら走っている検索を捨てて止まり_終わったら同じ対象から引き直す()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var asked = new List<string>();
        CandidatePrefetch? prefetch = null;
        prefetch = new CandidatePrefetch(
            _cache,
            _ => Task.FromResult<IReadOnlyList<PrefetchTarget>>([Target("a.zip", 1)]),
            async (target, token) =>
            {
                asked.Add(target.Target);
                if (asked.Count == 1)
                {
                    // 1回目は検索の途中で取り込みが始まる。中断を受けて抜ける
                    started.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                }

                return Found("100");
            },
            () => true);

        var running = prefetch.RunAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        prefetch.Pause();

        // 止めている間は引かない（捨てた1回目の結果も控えない）
        Assert.Single(asked);
        Assert.Null(_cache.Find(@"D:\dl\a.zip"));
        Assert.False(running.IsCompleted);

        prefetch.Resume();
        var run = await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(PrefetchEnd.Finished, run.End);
        Assert.Equal([@"D:\dl\a.zip", @"D:\dl\a.zip"], asked);
        Assert.NotNull(_cache.Find(@"D:\dl\a.zip"));
    }

    [Fact]
    public async Task 取り込みの間に始めた先回りは_終わるまで引かない()
    {
        var asked = 0;
        var prefetch = new CandidatePrefetch(
            _cache,
            _ => Task.FromResult<IReadOnlyList<PrefetchTarget>>([Target("a.zip", 1)]),
            (_, _) =>
            {
                asked++;
                return Task.FromResult(Found("1"));
            },
            () => true);

        prefetch.Pause();
        var running = prefetch.RunAsync();
        Assert.Equal(0, asked);

        prefetch.Resume();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task BOOTHに届かなかったら_その回の残りは引かない()
    {
        var asked = 0;
        var prefetch = new CandidatePrefetch(
            _cache,
            _ => Task.FromResult<IReadOnlyList<PrefetchTarget>>([Target("a.zip", 2), Target("b.zip", 1)]),
            (_, _) =>
            {
                asked++;
                return Task.FromResult(new ResolutionProposal([], BoothUnreachable: true));
            },
            () => true);

        var run = await prefetch.RunAsync();

        Assert.Equal(PrefetchEnd.Unreachable, run.End);
        Assert.Equal(1, asked);
        Assert.Null(_cache.Find(@"D:\dl\a.zip"));
    }

    [Fact]
    public async Task 未確定に残っていない持ち主の控えは_先回りの頭で消える()
    {
        await _cache.AddIfMissingAsync(@"D:\dl\gone.zip", ["hash-gone"], Found("1"), Day);
        var prefetch = new CandidatePrefetch(
            _cache,
            _ => Task.FromResult<IReadOnlyList<PrefetchTarget>>([]),
            (_, _) => Task.FromResult(Found("1")),
            () => true);

        await prefetch.RunAsync();

        Assert.Null(_cache.Find(@"D:\dl\gone.zip"));
    }
}
