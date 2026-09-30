using System.Collections.Concurrent;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 画面のスレッドの外で順に書く列（<see cref="BackgroundWriteQueue"/>）。商品ページを開くたびの検索の履歴と足跡が通る。
/// 確かめるのは、呼んだスレッドで走らないこと・頼まれた順を守ること・1つの失敗で後ろを止めないこと・書き終わりを待てること。
/// 時計には頼らない（順は、前の作業を手で止めて作る）。
/// </summary>
public sealed class BackgroundWriteQueueTests : IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-write-queue-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task 画面のスレッドから頼んでも画面のスレッドでは走らない()
    {
        using var screen = new SingleThreadContext();
        var queue = new BackgroundWriteQueue();

        // 続きまで含めて、画面のスレッドへ戻らないこと（戻ると、ディスクへの書き出しがそこで走る）
        var seen = await screen.RunAsync(async () =>
        {
            var caller = Environment.CurrentManagedThreadId;
            var inside = await queue.RunAsync(async () =>
            {
                var threadBefore = Environment.CurrentManagedThreadId;
                var contextBefore = SynchronizationContext.Current;
                await Task.Yield();
                return (ThreadBefore: threadBefore, ContextBefore: contextBefore,
                    ThreadAfter: Environment.CurrentManagedThreadId, ContextAfter: SynchronizationContext.Current);
            });
            return (Caller: caller, Inside: inside);
        }).WaitAsync(Limit);

        Assert.NotEqual(seen.Caller, seen.Inside.ThreadBefore);
        Assert.NotEqual(seen.Caller, seen.Inside.ThreadAfter);
        Assert.Null(seen.Inside.ContextBefore);
        Assert.Null(seen.Inside.ContextAfter);
    }

    [Fact]
    public async Task 前の作業が終わっていても呼んだスレッドでは走らない()
    {
        using var screen = new SingleThreadContext();
        var queue = new BackgroundWriteQueue();
        await queue.RunAsync(() => Task.CompletedTask).WaitAsync(Limit);

        var (caller, ran) = await screen.RunAsync(async () =>
        {
            var callerThread = Environment.CurrentManagedThreadId;
            var ranOn = await queue.RunAsync(() => Task.FromResult(Environment.CurrentManagedThreadId));
            return (callerThread, ranOn);
        }).WaitAsync(Limit);

        Assert.NotEqual(caller, ran);
    }

    [Fact]
    public async Task 頼まれた順に1本ずつ走る()
    {
        var queue = new BackgroundWriteQueue();
        var order = new ConcurrentQueue<string>();
        var holdFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = queue.RunAsync(async () =>
        {
            order.Enqueue("1 始まり");
            firstStarted.SetResult();
            await holdFirst.Task;
            order.Enqueue("1 終わり");
        });
        var second = queue.RunAsync(() =>
        {
            order.Enqueue("2");
            return Task.CompletedTask;
        });
        var third = queue.RunAsync(() =>
        {
            order.Enqueue("3");
            return Task.CompletedTask;
        });

        await firstStarted.Task.WaitAsync(Limit);

        // 1つ目が止まっている間、後ろは始まらない
        Assert.False(second.IsCompleted);
        Assert.False(third.IsCompleted);
        Assert.Equal(["1 始まり"], order);

        holdFirst.SetResult();
        await Task.WhenAll(first, second, third).WaitAsync(Limit);

        Assert.Equal(["1 始まり", "1 終わり", "2", "3"], order);
    }

    [Fact]
    public async Task 失敗しても後ろは止まらず失敗は頼んだ側へ返る()
    {
        var queue = new BackgroundWriteQueue();

        var failing = queue.RunAsync(() => Task.FromException(new IOException("書けなかった")));
        var thrownBeforeAwait = queue.RunAsync<int>(() => throw new InvalidOperationException("始める前に落ちた"));
        var after = queue.RunAsync(() => Task.FromResult(7));

        await Assert.ThrowsAsync<IOException>(() => failing.WaitAsync(Limit));
        await Assert.ThrowsAsync<InvalidOperationException>(() => thrownBeforeAwait.WaitAsync(Limit));
        Assert.Equal(7, await after.WaitAsync(Limit));

        // 失敗が混ざっても、書き終わりの待ちは失敗にならない（閉じる前の待ちが例外で抜けないように）
        await queue.WhenIdleAsync().WaitAsync(Limit);
    }

    [Fact]
    public async Task 書き終わりの待ちは列が空くまで終わらない()
    {
        var queue = new BackgroundWriteQueue();
        Assert.True(queue.WhenIdleAsync().IsCompletedSuccessfully);

        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var written = false;
        _ = queue.RunAsync(async () =>
        {
            await hold.Task;
            written = true;
        });
        _ = queue.RunAsync(() => Task.CompletedTask);

        var idle = queue.WhenIdleAsync();
        Assert.False(idle.IsCompleted);

        hold.SetResult();
        await idle.WaitAsync(Limit);
        Assert.True(written);
    }

    /// <summary>
    /// 続けて開いた足跡が、頼んだ順に当たる（後から押した方の時刻が残る）。
    /// プールへ投げるだけだと走り出す順は決まらず、古い時刻が後から書かれて勝ち得る。
    /// </summary>
    [Fact]
    public async Task 同じ商品の足跡を続けて頼むと後の時刻が残る()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        var store = new DataStore(paths);
        var queue = new BackgroundWriteQueue();
        var start = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(9));

        // 1つ目が書き始める前に全部つなぐ（走り出す順が決まらない形を作る）
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = queue.RunAsync(() => hold.Task);
        const int Count = 40;
        for (var i = 0; i < Count; i++)
        {
            var at = start.AddSeconds(i);
            _ = queue.RunAsync(() => store.Recent.UpdateAsync(
                log => new RecentLog { Entries = RecentActivity.Touch(log.Entries, "11", RecentKind.Viewed, at) }));
        }

        hold.SetResult();
        await queue.WhenIdleAsync().WaitAsync(Limit);

        var entry = Assert.Single(store.Recent.Load().Entries);
        Assert.Equal(start.AddSeconds(Count - 1), entry.ViewedAt);
    }

    /// <summary>
    /// 画面のスレッドの代わり。1本のスレッドで、送られた続きを順に回す（<c>StoreGateCommandTests</c> と同じ形）。
    /// </summary>
    private sealed class SingleThreadContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public SingleThreadContext()
        {
            var thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                {
                    callback(state);
                }
            })
            {
                IsBackground = true,
                Name = "画面のスレッドの代わり",
            };
            thread.Start();
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            if (!_queue.IsAddingCompleted)
            {
                _queue.Add((d, state));
            }
        }

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

        public Task<T> RunAsync<T>(Func<Task<T>> work)
        {
            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(_ => _ = RunCoreAsync(), null);
            return done.Task;

            async Task RunCoreAsync()
            {
                try
                {
                    done.SetResult(await work());
                }
                catch (Exception exception)
                {
                    done.SetException(exception);
                }
            }
        }

        public void Dispose() => _queue.CompleteAdding();
    }
}
