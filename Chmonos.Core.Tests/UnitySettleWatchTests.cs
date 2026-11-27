using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

public sealed class UnitySettleWatchTests
{
    [Fact]
    public void QuietLongEnough_Settles()
    {
        var watch = new UnitySettleWatch();
        Assert.False(watch.Observe(0, busy: false, replied: true, newLogLines: 0));
        Assert.False(watch.Observe(UnitySettleWatch.QuietMs - 1, busy: false, replied: true, newLogLines: 0));
        Assert.True(watch.Observe(UnitySettleWatch.QuietMs, busy: false, replied: true, newLogLines: 0));
    }

    [Theory]
    [InlineData(true, true, 0)] // 進捗の窓が出ている
    [InlineData(false, false, 0)] // 返事が遅い（窓が消えた直後の止まり）
    [InlineData(false, true, 47)] // ログに行がまとめて出た
    public void AnyNoise_RestartsTheQuietTime(bool busy, bool replied, int lines)
    {
        var watch = new UnitySettleWatch();
        Assert.False(watch.Observe(0, busy: false, replied: true, newLogLines: 0));
        Assert.False(watch.Observe(250, busy, replied, lines));

        // 乱れた後は、そこから数え直す（前の静けさを足さない）
        Assert.False(watch.Observe(300, busy: false, replied: true, newLogLines: 0));
        Assert.False(watch.Observe(599, busy: false, replied: true, newLogLines: 0));
        Assert.True(watch.Observe(600, busy: false, replied: true, newLogLines: 0));
    }

    [Fact]
    public void StalledReply_IsNotCountedAsQuiet()
    {
        // 窓が消えた後に返事が 1.7 秒止まる間はログも出ない。止まりの間を静けさと数えると早く選んで失敗した（実機 2026-11-27）
        var watch = new UnitySettleWatch();
        for (var now = 0; now <= 1700; now += 100)
        {
            Assert.False(watch.Observe(now, busy: false, replied: false, newLogLines: 0));
        }

        Assert.False(watch.Observe(1800, busy: false, replied: true, newLogLines: 47));
        Assert.False(watch.Observe(1900, busy: false, replied: true, newLogLines: 0));
        Assert.True(watch.Observe(2200, busy: false, replied: true, newLogLines: 0));
    }
}
