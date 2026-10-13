using System.IO;
using Chmonos.App.Services;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>2本目の起動から動いている方へ窓を開かせる合図（段4）。保存先は作り物の場所（作らない・名前を決めるだけ）。</summary>
public sealed class OpenRequestChannelTests
{
    private static AppPaths NewPaths() => new(Path.Combine(Path.GetTempPath(), "chmonos-open-tests", Guid.NewGuid().ToString("N")));

    [Fact]
    public void 待っている方がいれば頼みが届き_受けたと返る()
    {
        var paths = NewPaths();
        using var received = new ManualResetEventSlim();
        using var channel = OpenRequestChannel.Listen(paths, received.Set);

        Assert.True(OpenRequestChannel.Request(paths, TimeSpan.FromSeconds(5)));
        Assert.True(received.Wait(TimeSpan.FromSeconds(5)));

        // 2回目も届く（1回で待ちを外さない）
        received.Reset();
        Assert.True(OpenRequestChannel.Request(paths, TimeSpan.FromSeconds(5)));
        Assert.True(received.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void 待っている方がいなければ_偽を返す()
        => Assert.False(OpenRequestChannel.Request(NewPaths(), TimeSpan.FromMilliseconds(100)));

    [Fact]
    public void 別の保存先の頼みは届かず_同じ保存先は書き方が違っても届く()
    {
        var paths = NewPaths();
        var called = 0;
        using var channel = OpenRequestChannel.Listen(paths, () => Interlocked.Increment(ref called));

        Assert.False(OpenRequestChannel.Request(NewPaths(), TimeSpan.FromMilliseconds(100)));
        Assert.Equal(0, Volatile.Read(ref called));

        var sameRoot = new AppPaths(paths.Root.ToLowerInvariant() + Path.DirectorySeparatorChar);
        Assert.Equal(OpenRequestChannel.NameOf(paths, "open"), OpenRequestChannel.NameOf(sameRoot, "open"));
    }

    /// <summary>頼みを受けている最中に終えても、片付けた合図に触れて落ちない（点検38）。</summary>
    [Fact]
    public void 頼みの処理の最中に終えても_落ちずに処理が終わるのを待つ()
    {
        var paths = NewPaths();
        using var entered = new ManualResetEventSlim();
        var finished = false;
        var channel = OpenRequestChannel.Listen(paths, () =>
        {
            entered.Set();
            Thread.Sleep(300);
            finished = true;
        });

        var request = Task.Run(() => OpenRequestChannel.Request(paths, TimeSpan.FromSeconds(5)));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        channel.Dispose();

        Assert.True(Volatile.Read(ref finished));
        Assert.True(request.Wait(TimeSpan.FromSeconds(5)));
    }
}
