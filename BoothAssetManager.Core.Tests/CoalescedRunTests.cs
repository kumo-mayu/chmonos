using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 走っている間に来た依頼を1回にまとめ、取りこぼさずにもう一度走らせる（対応アバターの検出の依頼・N4）。
/// </summary>
public class CoalescedRunTests
{
    /// <summary>1回ずつ止めて進められる走り。何回走ったかを数える。</summary>
    private sealed class Steps
    {
        private readonly Queue<TaskCompletionSource> _pending = new();

        public int Started { get; private set; }

        public Exception? FailFirstWith { get; init; }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pending)
            {
                Started++;
                _pending.Enqueue(gate);
            }

            await gate.Task;

            if (Started == 1 && FailFirstWith is { } failure)
            {
                throw failure;
            }
        }

        public void FinishOne()
        {
            lock (_pending)
            {
                _pending.Dequeue().SetResult();
            }
        }

        public async Task WaitForStartAsync(int count)
        {
            for (var i = 0; i < 200 && Started < count; i++)
            {
                await Task.Delay(10);
            }

            Assert.Equal(count, Started);
        }
    }

    /// <summary>走っている間に何回頼まれても、終わった後にもう1回だけ走る（全件走査を10回並べない）。</summary>
    [Fact]
    public async Task RunsOnceMoreForEveryRequestThatCameWhileRunning()
    {
        var steps = new Steps();
        var run = new CoalescedRun(steps.RunAsync);

        var first = run.RequestAsync();
        await steps.WaitForStartAsync(1);

        // 走っている間の依頼はすぐ返る（任せる）
        await run.RequestAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await run.RequestAsync().WaitAsync(TimeSpan.FromSeconds(5));

        steps.FinishOne();
        await steps.WaitForStartAsync(2);
        steps.FinishOne();

        await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, steps.Started);
    }

    /// <summary>
    /// **走っている回が例外で抜けても、その間に来た依頼は捨てない。**例外は続きを走らせてから投げる。
    /// 前は例外で抜けると「もう一度」の印ごと捨てられ、手で紐付けた分の検出が次の依頼まで走らなかった。
    /// </summary>
    [Fact]
    public async Task DoesNotDropRequestsWhenARunFails()
    {
        var steps = new Steps { FailFirstWith = new InvalidOperationException("検出で落ちた") };
        var run = new CoalescedRun(steps.RunAsync);

        var first = run.RequestAsync();
        await steps.WaitForStartAsync(1);
        await run.RequestAsync().WaitAsync(TimeSpan.FromSeconds(5));

        steps.FinishOne();
        await steps.WaitForStartAsync(2);
        steps.FinishOne();

        await Assert.ThrowsAsync<InvalidOperationException>(() => first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, steps.Started);
    }

    /// <summary>
    /// **走っている人が中断で抜けても、任された依頼は捨てない。**中断したのは走っていた人の都合なので、
    /// 任せた人の分は裏で続きを走らせる。前は誰も拾わず、次の依頼まで走らなかった。
    /// </summary>
    [Fact]
    public async Task DoesNotDropRequestsWhenTheRunnerIsCanceled()
    {
        var started = 0;
        var secondRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = new CoalescedRun(async token =>
        {
            if (Interlocked.Increment(ref started) == 1)
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            else
            {
                secondRun.TrySetResult();
            }
        });

        using var leaving = new CancellationTokenSource();
        var first = run.RequestAsync(leaving.Token);
        for (var i = 0; i < 200 && Volatile.Read(ref started) < 1; i++)
        {
            await Task.Delay(10);
        }

        // 走っている間に別の人が頼んで任せ、その後に走っていた人が中断する
        await run.RequestAsync().WaitAsync(TimeSpan.FromSeconds(5));
        leaving.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(5)));
        await secondRun.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, Volatile.Read(ref started));
    }

    /// <summary>任された依頼が無ければ、中断した後に勝手に走らせない。</summary>
    [Fact]
    public async Task DoesNotRunAgainAfterCancelWhenNothingWasHandedOver()
    {
        var started = 0;
        var run = new CoalescedRun(async token =>
        {
            Interlocked.Increment(ref started);
            await Task.Delay(Timeout.Infinite, token);
        });

        using var leaving = new CancellationTokenSource();
        var first = run.RequestAsync(leaving.Token);
        for (var i = 0; i < 200 && Volatile.Read(ref started) < 1; i++)
        {
            await Task.Delay(10);
        }

        leaving.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(5)));
        await Task.Delay(100);
        Assert.Equal(1, Volatile.Read(ref started));
    }

    /// <summary>誰も走っていなければ、その場で走って終わるまで待つ。</summary>
    [Fact]
    public async Task RunsRightAwayWhenIdle()
    {
        var count = 0;
        var run = new CoalescedRun(_ =>
        {
            count++;
            return Task.CompletedTask;
        });

        await run.RequestAsync();
        await run.RequestAsync();

        Assert.Equal(2, count);
    }
}
