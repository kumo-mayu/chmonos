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
}
