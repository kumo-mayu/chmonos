using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 門はアプリに1つ（static）なので、閉じる試験は他の試験と並べて走らせない。
/// 並べると、他の試験の保存が閉じている間だけ待たされ、待ち時間を見る試験が揺れる。
/// </summary>
[CollectionDefinition(nameof(StoreWriteGateCollection), DisableParallelization = true)]
public sealed class StoreWriteGateCollection;

/// <summary>
/// 保存先を丸ごと運んでいる間、書き込みを待たせる門（E8）。
/// 通してしまうと、コピー済みへ書いた分が元を消すときに失われる。
/// </summary>
[Collection(nameof(StoreWriteGateCollection))]
public class StoreWriteGateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"bam-gate-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task LetsWritesThroughWhenNobodyIsMoving()
    {
        Assert.False(StoreWriteGate.IsHeld);

        // 止まっていないときは待たせない（毎回の書き込みが通る道なので、空振りは速く抜ける）
        await StoreWriteGate.WaitAsync();
        using var writing = await StoreWriteGate.EnterAsync();
    }

    [Fact]
    public async Task HoldsWritesWhileMovingAndLetsThemThroughAfterwards()
    {
        var hold = await StoreWriteGate.HoldAsync();
        Assert.True(StoreWriteGate.IsHeld);

        var waiting = StoreWriteGate.WaitAsync();
        var entering = StoreWriteGate.EnterAsync();
        Assert.False(waiting.IsCompleted);
        Assert.False(entering.IsCompleted);

        hold.Dispose();

        // 運び終えたら、待たせていた書き込みを通す（中止ではなく待たせる）
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        (await entering.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        Assert.False(StoreWriteGate.IsHeld);
    }

    [Fact]
    public async Task OnlyOneMoveAtATime()
    {
        var first = await StoreWriteGate.HoldAsync();

        var second = StoreWriteGate.HoldAsync();
        Assert.False(second.IsCompleted);

        first.Dispose();

        var taken = await second.WaitAsync(TimeSpan.FromSeconds(5));
        taken.Dispose();
        Assert.False(StoreWriteGate.IsHeld);
    }

    /// <summary>
    /// **走っている書き込みが抜けるまで、運び始めない。**
    /// 前は入口で一度見るだけだったので、見た後に走り出した書き込みが運んでいる最中にも書けていた。
    /// </summary>
    [Fact]
    public async Task WaitsForWritesAlreadyRunningBeforeMoving()
    {
        var writing = await StoreWriteGate.EnterAsync();

        var hold = StoreWriteGate.HoldAsync();
        await Task.Delay(50);
        Assert.False(hold.IsCompleted);

        // 閉じ始めた後に来た書き込みは、運び終わるまで待たされる
        Assert.True(StoreWriteGate.IsHeld);
        var late = StoreWriteGate.EnterAsync();
        Assert.False(late.IsCompleted);

        writing.Dispose();
        var held = await hold.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(late.IsCompleted);

        held.Dispose();
        (await late.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    /// <summary>抜けを待つ間に中断されたら、閉じたのを戻す（待たせていた書き込みを止めたままにしない）。</summary>
    [Fact]
    public async Task ReopensWhenMovingIsCanceledWhileWaiting()
    {
        var writing = await StoreWriteGate.EnterAsync();
        using var source = new CancellationTokenSource();

        var hold = StoreWriteGate.HoldAsync(source.Token);
        var late = StoreWriteGate.EnterAsync();

        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hold);

        Assert.False(StoreWriteGate.IsHeld);
        (await late.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        writing.Dispose();
    }

    /// <summary>JSON の保存も門を通る（裏へ投げた書き込みも、どこから呼ばれても止まる）。</summary>
    [Fact]
    public async Task JsonSavesWaitWhileMoving()
    {
        var path = Path.Combine(_directory, "a.json");
        var hold = await StoreWriteGate.HoldAsync();

        var saving = Task.Run(() => JsonStore.WriteAsync(path, new { Name = "x" }));
        await Task.Delay(50);
        Assert.False(saving.IsCompleted);
        Assert.False(File.Exists(path));

        hold.Dispose();
        await saving.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(File.Exists(path));
    }

    /// <summary>
    /// 専用のスレッドで同期の口を走らせ、門で止まる（待ちの状態になる）か、終わるまで見る。
    /// 時計は試験が止まったままにならないための打ち切りだけで、門が効いていれば止まった所で返る
    /// </summary>
    private static Thread StartAndWaitUntilBlocked(Action action, Action<Exception> failed)
    {
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failed(exception);
            }
        })
        { IsBackground = true };
        thread.Start();

        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (thread.IsAlive && !thread.ThreadState.HasFlag(ThreadState.WaitSleepJoin) && waited.Elapsed < TimeSpan.FromSeconds(10))
        {
            Thread.Sleep(1);
        }

        return thread;
    }

    /// <summary>
    /// 同期の入口も、運んでいる間は呼んだスレッドを止めて待たせ、開けたら通す。
    /// 非同期の口しか見ていなかったので、同期の Enter が閉じた門を素通りしても通っていた（2026-11-28 の点検 M19）。
    /// 同期で書くのは unitypackage の控えなど裏のスレッドの保存で、引越しの間に古い場所へ書かれると失われる
    /// </summary>
    [Fact]
    public async Task SyncEnterWaitsWhileMoving()
    {
        var entered = 0;
        Exception? failure = null;
        var hold = await StoreWriteGate.HoldAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Thread thread;
        try
        {
            thread = StartAndWaitUntilBlocked(
                () =>
                {
                    using var writing = StoreWriteGate.Enter();
                    Volatile.Write(ref entered, 1);
                },
                exception => failure = exception);

            Assert.True(thread.IsAlive);
            Assert.Equal(0, Volatile.Read(ref entered));
        }
        finally
        {
            hold.Dispose();
        }

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
        Assert.Equal(1, Volatile.Read(ref entered));
    }

    /// <summary>
    /// 同期の <see cref="JsonStore.Write{T}"/> も門を通る（<see cref="JsonSavesWaitWhileMoving"/> の同期版）。
    /// 同期の Write が門を通らなくなっても通っていた（2026-11-28 の点検 M25）
    /// </summary>
    [Fact]
    public async Task SyncJsonSavesWaitWhileMoving()
    {
        var path = Path.Combine(_directory, "sync.json");
        Exception? failure = null;
        var hold = await StoreWriteGate.HoldAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Thread thread;
        try
        {
            thread = StartAndWaitUntilBlocked(() => JsonStore.Write(path, new { Name = "x" }), exception => failure = exception);

            Assert.True(thread.IsAlive);
            Assert.False(File.Exists(path));
        }
        finally
        {
            hold.Dispose();
        }

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
        Assert.True(File.Exists(path));
    }

    /// <summary>
    /// 商品を消すのも書き込みなので、運んでいる間は待ち、開けたら消す。
    /// 削除が門を通らなくなっても通っていた（2026-11-28 の点検 M26）。運んでいる間に消すと、写し済みの側に商品が残って生き返る。
    ///
    /// 錠の中の条件（門の直前）で「ここまで来た」を受け取る。非同期の待ちは外から見えないので、その後は打ち切りつきで
    /// 終わらない事を見る。門が効いていれば打ち切りまで待つだけで、待ちが短すぎても見逃しの側に倒れるだけ（誤って落ちない）。
    /// 200ms は、門を素通りした削除（ファイル3つと空のフォルダ）が終わるのに十分で、この試験を重くしない長さ
    /// </summary>
    [Fact]
    public async Task ItemDeletesWaitWhileMoving()
    {
        const string itemId = "777";
        var store = new DataStore(new AppPaths(Path.Combine(_directory, "store")));
        store.Paths.EnsureCreated();
        await store.Items.SaveAsync(new Models.ItemRecord { Id = itemId, Booth = new Models.BoothBlock(), Local = new Models.LocalBlock() });
        var decided = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var hold = await StoreWriteGate.HoldAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Task<bool> deleting;
        try
        {
            deleting = Task.Run(() => store.Items.DeleteIfAsync(itemId, _ =>
            {
                decided.TrySetResult();
                return true;
            }));
            await decided.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAny(deleting, Task.Delay(TimeSpan.FromMilliseconds(200)));

            Assert.False(deleting.IsCompleted);
            Assert.True(File.Exists(store.Paths.ItemFile(itemId)));
        }
        finally
        {
            hold.Dispose();
        }

        Assert.True(await deleting.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(File.Exists(store.Paths.ItemFile(itemId)));
    }
}
