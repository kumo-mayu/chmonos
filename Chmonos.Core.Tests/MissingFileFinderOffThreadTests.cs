using System.Collections;
using System.Collections.Concurrent;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 「見つからないファイルを探す」を、丸ごと呼んだスレッドの外で回す（2026-09-30）。
///
/// 命令の入口から <c>await</c> でつながっているだけだったので、続きは毎回画面のスレッドへ戻り、走査の控えの読みと読み直し
/// （8万件・21MB で 1回 0.19〜0.27秒）・監視フォルダの列挙・ファイルが在るかの確かめ・ハッシュの合間が画面を止めていた。
///
/// ここで確かめるのは4つ：どのスレッドで走るか／外へ出しても結果が同じこと／取り込みの書き込みと重なっても
/// 控えと商品が欠けないこと／中断が効くこと。
/// スレッドは、試験が作った「続きが1本のスレッドへ戻る」文脈（画面のスレッドの代わり）から呼んで見分ける。
/// 文脈の無いスレッドから呼ぶと、直す前でも続きはスレッドプールへ行くので見分けられない。時計には頼らない。
/// </summary>
public sealed class MissingFileFinderOffThreadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-missing-thread-" + Guid.NewGuid().ToString("N"));
    private readonly string _watched;
    private readonly DataStore _store;
    private readonly MissingFileFinder _finder;

    public MissingFileFinderOffThreadTests()
    {
        _watched = Path.Combine(_root, "watched");
        Directory.CreateDirectory(_watched);
        var paths = new AppPaths(Path.Combine(_root, "store"));
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _finder = new MissingFileFinder(_store);
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

    // ---- 道具 ----

    /// <summary>
    /// 画面のスレッドの代わり。<c>await</c> の続きを1本のスレッドへ戻す（WPF の Dispatcher と同じ形）。
    /// 戻ってきた続きの数を数える——探す処理が呼んだスレッドへ1つも戻らなければ、処理は丸ごと外で走っている。
    /// </summary>
    private sealed class OneThread : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
        private int _posts;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _posts);
            _queue.Add((d, state));
        }

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

        /// <summary><paramref name="start"/> をこのスレッドで呼び、返った仕事が終わるまで続きを回す。</summary>
        public static (int Caller, int Posts, T Result) Run<T>(Func<Task<T>> start)
        {
            var context = new OneThread();
            var caller = 0;
            Task<T>? work = null;
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                caller = Environment.CurrentManagedThreadId;
                SetSynchronizationContext(context);
                try
                {
                    work = start();
                }
                catch (Exception exception)
                {
                    failure = exception;
                    return;
                }

                // 終わりの知らせはこの文脈へ戻さない（戻すと、それが続きの1つに数えられる）
                work.ContinueWith(_ => context._queue.CompleteAdding(), TaskScheduler.Default);
                foreach (var (callback, state) in context._queue.GetConsumingEnumerable())
                {
                    callback(state);
                }
            });
            thread.Start();
            thread.Join();
            if (failure is not null)
            {
                throw new InvalidOperationException("呼んだスレッドで失敗した", failure);
            }

            return (caller, context._posts, work!.GetAwaiter().GetResult());
        }
    }

    /// <summary>
    /// 監視フォルダの一覧。たどられたスレッドを控える。
    /// 探す処理は、控えを読んだ直後にこれをたどり始め（間に待ちは無い）、たどり終えた直後に控えを書き換える。
    /// だから「たどったスレッド」が、控えの読み・列挙・錠の中の読み直しのスレッドになる。
    /// </summary>
    private sealed class ProbedFolders(params string[] folders) : IReadOnlyList<string>
    {
        public ConcurrentQueue<int> WalkedOn { get; } = new();

        public int Count => folders.Length;

        public string this[int index] => folders[index];

        public IEnumerator<string> GetEnumerator()
        {
            foreach (var folder in folders)
            {
                WalkedOn.Enqueue(Environment.CurrentManagedThreadId);
                yield return folder;
            }

            WalkedOn.Enqueue(Environment.CurrentManagedThreadId);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>進み具合の受け手。呼ばれたスレッドを控える（<c>Progress</c> と違い、どこへも運ばずその場で受ける）。</summary>
    private sealed class ProbedProgress(Action<int>? onReport = null) : IProgress<(int Hashed, string? Detail)>
    {
        public ConcurrentQueue<int> ReportedOn { get; } = new();

        public void Report((int Hashed, string? Detail) value)
        {
            ReportedOn.Enqueue(Environment.CurrentManagedThreadId);
            onReport?.Invoke(value.Hashed);
        }
    }

    private string NewFile(string name, string content)
    {
        var path = Path.Combine(_watched, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// 商品1件のファイルを監視フォルダの中で移した形。ほかに、大きさが同じで中身の違うファイルを1つ置く
    /// （大きさが合う物は中身まで確かめるので、ハッシュを取るのは2個。取った物は控えに足される）。
    /// </summary>
    private async Task<(string Gone, string Moved, string Other)> MovedWithinTheWatchedFolderAsync(string itemId = "111")
    {
        var moved = NewFile(Path.Combine("sub", $"移した-{itemId}.zip"), "なかみ" + itemId);
        var other = NewFile($"別の-{itemId}.zip", "べつの" + itemId);
        var gone = Path.Combine(_watched, $"元の場所-{itemId}.zip");
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = itemId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.UnixEpoch, Name = "作り物の商品" },
            Local = new LocalBlock
            {
                LocalFiles =
                [
                    new LocalFileRecord
                    {
                        Hash = await FileHasher.ComputeSha256Async(moved),
                        SizeBytes = new FileInfo(moved).Length,
                        Paths = [gone],
                    },
                ],
            },
        });

        return (gone, moved, other);
    }

    private static ScanCacheEntry Cached(string path) => new()
    {
        Path = path,
        SizeBytes = 1,
        ModifiedAtUtc = DateTimeOffset.UnixEpoch,
        Hash = "ab",
    };

    private async Task<string> PathOfAsync(string itemId)
        => Assert.Single(Assert.Single((await _store.Items.LoadAsync(itemId))!.Local.LocalFiles).Paths);

    // ---- どのスレッドで走るか・結果が同じか ----

    /// <summary>
    /// 1本のスレッドから呼ぶと、控えの読み・列挙・ハッシュ・控えと商品の書き換えは別のスレッドで走り、
    /// 呼んだスレッドへ続きは1つも戻らない。結果（見つけた数・付け替えた場所・控えに足した物）は前と同じ。
    /// </summary>
    [Fact]
    public async Task TheWholeSearchRunsOffTheCallingThread()
    {
        var (_, moved, other) = await MovedWithinTheWatchedFolderAsync();
        await _store.ScanCache.SaveAsync([Cached(@"Z:\前から.zip")]);
        var folders = new ProbedFolders(_watched);
        var progress = new ProbedProgress();

        var (caller, posts, result) = OneThread.Run(() => _finder.FindAsync(folders, progress));

        // 続きが呼んだスレッドへ戻っていない（戻っていれば、その続きの仕事が呼んだスレッドを塞ぐ）
        Assert.Equal(0, posts);

        // 控えを読んだ直後・列挙の合間・控えを書き換える直前
        Assert.Equal(2, folders.WalkedOn.Count);
        Assert.DoesNotContain(caller, folders.WalkedOn);

        // ハッシュの合間（1個取るたび）
        Assert.Equal(2, progress.ReportedOn.Count);
        Assert.DoesNotContain(caller, progress.ReportedOn);

        Assert.Equal((1, 1, 2, 0), (result.MissingBefore, result.Relinked, result.Hashed, result.StillMissing));
        Assert.Empty(result.Unreachable);
        Assert.Equal(moved, await PathOfAsync("111"));
        Assert.Equal(
            new[] { @"Z:\前から.zip", moved, other }.Order(),
            _store.ScanCache.Load().Select(entry => entry.Path).Order());
    }

    /// <summary>見つからないファイルが無いとき（控えを読む前に返る道）も、商品の読みと在るかの確かめは外で走る。</summary>
    [Fact]
    public async Task NothingMissingStillReturnsWithoutComingBackToTheCaller()
    {
        var (gone, _, _) = await MovedWithinTheWatchedFolderAsync();
        File.WriteAllText(gone, "元の場所に在る");
        var folders = new ProbedFolders(_watched);

        var (_, posts, result) = OneThread.Run(() => _finder.FindAsync(folders));

        Assert.Equal(0, posts);
        Assert.Equal(0, result.MissingBefore);
        Assert.Empty(folders.WalkedOn);
    }

    // ---- 取り込みの書き込みと重なっても欠けない ----

    /// <summary>
    /// 取り込みが控えの錠を持っている間に押しても、呼んだスレッドは止まらない（錠は待ちで、塞がない）。
    /// 錠が明けたら、取り込みが足した分に重ねて書くので、どちらの分も残る。
    /// </summary>
    [Fact]
    public async Task SearchingWhileAnImportHoldsTheScanCacheNeitherBlocksNorLosesEntries()
    {
        var (_, moved, other) = await MovedWithinTheWatchedFolderAsync();
        await _store.ScanCache.SaveAsync([Cached(@"Z:\前から.zip")]);

        // 取り込みの代わり：錠を持ったまま止まり、合図で1件足して書く
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var import = Task.Run(() => _store.ScanCache.TryUpdateAwaitingAsync(async current =>
        {
            holding.SetResult();
            await release.Task;
            current.Add(Cached(@"Z:\取り込みが足した.zip"));
            return current;
        }));
        await holding.Task;

        var hashedBoth = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new ProbedProgress(hashed =>
        {
            if (hashed == 2)
            {
                hashedBoth.SetResult();
            }
        });

        var (caller, _, result) = OneThread.Run(async () =>
        {
            var search = _finder.FindAsync([_watched], progress);

            // ハッシュを取り終えたら、次は控えの書き換え。錠は取り込みが持っているので、ここから先へは進めない。
            // この待ちの続きが呼んだスレッドで走ること自体が、呼んだスレッドが塞がっていない証し
            await hashedBoth.Task;
            Assert.False(search.IsCompleted);

            release.SetResult();
            return await search;
        });

        Assert.True(await import);
        Assert.DoesNotContain(caller, progress.ReportedOn);
        Assert.Equal((1, 1, 2), (result.MissingBefore, result.Relinked, result.Hashed));
        Assert.Equal(moved, await PathOfAsync("111"));
        Assert.Equal(
            new[] { @"Z:\前から.zip", @"Z:\取り込みが足した.zip", moved, other }.Order(),
            _store.ScanCache.Load().Select(entry => entry.Path).Order());
    }

    /// <summary>
    /// 探している間に、取り込みが控えを書き、別の書き手が同じ商品のほかの項目を書いても、どれも消えない。
    /// どちらが先に錠を取っても同じ結果になる（控えは錠の中で今の控えに足す・商品は錠の中で今の値の場所だけを差し替える）。
    /// </summary>
    [Fact]
    public async Task SearchingAlongsideOtherWritersKeepsEveryonesChanges()
    {
        for (var round = 0; round < 6; round++)
        {
            var itemId = (1000 + round).ToString();
            var (_, moved, other) = await MovedWithinTheWatchedFolderAsync(itemId);
            var imported = Enumerable.Range(0, 5).Select(index => $@"Z:\取り込み\{round}-{index}.zip").ToList();

            var writers = new List<Func<Task>>
            {
                () => _finder.FindAsync([_watched]),
                () => _store.ScanCache.UpdateAsync(current => [.. current, .. imported.Select(Cached)]),
                () => _store.Items.ChangeLocalAsync(
                    itemId, local => local with { Memo = "人が書いたメモ" }, [LocalField.Memo]),
            };

            // 始める順を回ごとにずらす（どれが先に錠を取るかを変える）
            var shift = round % writers.Count;
            await Task.WhenAll(writers.Skip(shift).Concat(writers.Take(shift)).Select(writer => Task.Run(writer)).ToList());

            var item = await _store.Items.LoadAsync(itemId);
            Assert.Equal(moved, Assert.Single(Assert.Single(item!.Local.LocalFiles).Paths));
            Assert.Equal("人が書いたメモ", item.Local.Memo);

            var cached = _store.ScanCache.Load().Select(entry => entry.Path).ToHashSet();
            Assert.Superset(imported.Append(moved).Append(other).ToHashSet(), cached);
        }
    }

    // ---- 中断 ----

    /// <summary>始める前に取り消されていれば、何も書かずに取り消しで終わる。</summary>
    [Fact]
    public async Task ACancelledSearchWritesNothing()
    {
        var (gone, _, _) = await MovedWithinTheWatchedFolderAsync();
        var before = _store.ScanCache.WriteCount;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _finder.FindAsync([_watched], cancellationToken: new CancellationToken(canceled: true)));

        Assert.Equal(gone, await PathOfAsync("111"));
        Assert.Equal(before, _store.ScanCache.WriteCount);
    }

    /// <summary>
    /// 途中（ハッシュを1個取った所）で取り消すと、そこで止まる：商品の場所は付け替えず、控えも書かない。
    /// 1本のスレッドから呼んでも、取り消しは呼んだ側へ例外として届く。
    /// </summary>
    [Fact]
    public async Task CancellingInTheMiddleStopsBeforeAnythingIsWritten()
    {
        var (gone, _, _) = await MovedWithinTheWatchedFolderAsync();
        var before = _store.ScanCache.WriteCount;
        using var cancel = new CancellationTokenSource();
        var progress = new ProbedProgress(_ => cancel.Cancel());

        var failure = Assert.Throws<InvalidOperationException>(() => OneThread.Run(async () =>
        {
            try
            {
                return await _finder.FindAsync([_watched], progress, cancel.Token);
            }
            catch (OperationCanceledException exception)
            {
                throw new InvalidOperationException("取り消しが届いた", exception);
            }
        }));

        Assert.Equal("取り消しが届いた", failure.Message);
        Assert.Single(progress.ReportedOn);
        Assert.Equal(gone, await PathOfAsync("111"));
        Assert.Equal(before, _store.ScanCache.WriteCount);
        Assert.Empty(_store.ScanCache.Load());
    }
}
