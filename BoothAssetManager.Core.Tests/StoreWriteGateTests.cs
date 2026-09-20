using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 保存先を丸ごと運んでいる間、書き込みを待たせる門（E8）。
/// 通してしまうと、コピー済みへ書いた分が元を消すときに失われる。
/// </summary>
public class StoreWriteGateTests
{
    [Fact]
    public async Task LetsWritesThroughWhenNobodyIsMoving()
    {
        Assert.False(StoreWriteGate.IsHeld);

        // 止まっていないときは待たせない（毎回の書き込みが通る道なので、空振りは速く抜ける）
        await StoreWriteGate.WaitAsync();
    }

    [Fact]
    public async Task HoldsWritesWhileMovingAndLetsThemThroughAfterwards()
    {
        var hold = await StoreWriteGate.HoldAsync();
        Assert.True(StoreWriteGate.IsHeld);

        var waiting = StoreWriteGate.WaitAsync();
        Assert.False(waiting.IsCompleted);

        hold.Dispose();

        // 運び終えたら、待たせていた書き込みを通す（中止ではなく待たせる）
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
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
}
