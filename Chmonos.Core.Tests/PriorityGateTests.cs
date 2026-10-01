using Chmonos.Core.Booth;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 取得の順番待ち。**空いたときは、待っている中でいちばん急ぐものを通す。**
///
/// 順位を分ける理由は1つだけで、人を待たせないこと。
/// 100商品の取り込み中に「このIDで確認」を押して5分待たされるのが元の姿だった。
/// </summary>
public class PriorityGateTests
{
    /// <summary>誰も待っていなければ、そのまま通る。</summary>
    [Fact]
    public async Task LetsTheFirstCallerStraightThrough()
    {
        var gate = new PriorityGate();

        await gate.EnterAsync(BoothPriority.Gallery);

        gate.Release();
    }

    /// <summary>
    /// **本体。**先に並んだ取り込みより、後から来た人の操作を先に通す。
    /// </summary>
    [Fact]
    public async Task LetsAUserActionOvertakeAnImportAlreadyQueued()
    {
        var gate = new PriorityGate();
        var order = new List<string>();

        // いま1本が通っている
        await gate.EnterAsync(BoothPriority.Metadata);

        // 取り込みの続きが3本並ぶ
        var gallery = Queue(gate, BoothPriority.Gallery, "gallery", order);
        var metadata = Queue(gate, BoothPriority.Metadata, "metadata", order);
        var icon = Queue(gate, BoothPriority.ShopIcon, "icon", order);

        // 後から人が押した
        var user = Queue(gate, BoothPriority.User, "user", order);

        // 4本とも並び終えてから流す。並ぶ前に解放すると順番が運任せになる
        await WaitUntilQueued(gate, 4);

        // 通っていた1本が終わる。以降は解放のたびに1本ずつ流れる
        foreach (var waiter in new[] { user, metadata, gallery, icon })
        {
            gate.Release();
            await waiter.WaitAsync(TimeSpan.FromSeconds(5));
        }

        gate.Release();

        Assert.Equal(["user", "metadata", "gallery", "icon"], order);
    }

    /// <summary>同じ優先度の中は着いた順。段ごとの並び（古い商品から）が崩れない。</summary>
    [Fact]
    public async Task KeepsArrivalOrderWithinTheSamePriority()
    {
        var gate = new PriorityGate();
        var order = new List<string>();

        await gate.EnterAsync(BoothPriority.Metadata);

        // 1本ずつ並べる。同じ優先度なので、着いた順がそのまま通る順になるはず
        var first = Queue(gate, BoothPriority.Metadata, "1", order);
        await WaitUntilQueued(gate, 1);

        var second = Queue(gate, BoothPriority.Metadata, "2", order);
        await WaitUntilQueued(gate, 2);

        var third = Queue(gate, BoothPriority.Metadata, "3", order);
        await WaitUntilQueued(gate, 3);

        foreach (var waiter in new[] { first, second, third })
        {
            gate.Release();
            await waiter.WaitAsync(TimeSpan.FromSeconds(5));
        }

        gate.Release();

        Assert.Equal(["1", "2", "3"], order);
    }

    /// <summary>
    /// 中断された待ち手は飛ばす。**そこで門が閉じたままになってはいけない。**
    /// 取り込みを中断した直後に人が押す、はごく普通に起きる。
    /// </summary>
    [Fact]
    public async Task SkipsAWaiterThatWasCancelledAndKeepsGoing()
    {
        var gate = new PriorityGate();
        var order = new List<string>();

        await gate.EnterAsync(BoothPriority.User);

        using var cancellation = new CancellationTokenSource();
        var abandoned = Queue(gate, BoothPriority.Metadata, "abandoned", order, cancellation.Token);
        var survivor = Queue(gate, BoothPriority.Gallery, "survivor", order);
        await WaitUntilQueued(gate, 2);

        await cancellation.CancelAsync();

        gate.Release();
        await survivor.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["survivor"], order);

        // 中断された方は通っていない
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => abandoned.WaitAsync(TimeSpan.FromSeconds(5)));

        gate.Release();

        // 門はまだ使える
        await gate.EnterAsync(BoothPriority.User);
        gate.Release();
    }

    /// <summary>既に中断されているトークンで入ろうとしたら、その場で断る。</summary>
    [Fact]
    public async Task RefusesToQueueWithATokenAlreadyCancelled()
    {
        var gate = new PriorityGate();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => gate.EnterAsync(BoothPriority.User, cancellation.Token));
    }

    /// <summary>
    /// 全員が並び終えるまで待つ。
    /// 並ぶ前に解放すると、何番目が通ったのかが実行の速さで変わってしまう。
    /// </summary>
    private static async Task WaitUntilQueued(PriorityGate gate, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (gate.WaitingCount < expected)
        {
            Assert.True(DateTime.UtcNow < deadline, $"{expected} 本が並ばなかった（{gate.WaitingCount} 本）");
            await Task.Delay(5);
        }
    }

    private static Task Queue(
        PriorityGate gate,
        BoothPriority priority,
        string name,
        List<string> order,
        CancellationToken cancellationToken = default)
    {
        var entered = new TaskCompletionSource();

        _ = Task.Run(async () =>
        {
            try
            {
                await gate.EnterAsync(priority, cancellationToken);

                lock (order)
                {
                    order.Add(name);
                }

                entered.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                entered.TrySetCanceled();
            }
        });

        return entered.Task;
    }
}
