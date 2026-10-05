using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// ⑦の予定日のばらつきは、起動をまたいで同じ商品なら同じ日数になる（file-lifecycle.md 気になった所19）。
/// 前は string.GetHashCode で、プロセスごとに種が変わるので起動し直すと違う日になっていた。
/// 決まった数と比べるのは、プロセスの中で何度呼んでも同じなだけでは、起動をまたいで同じことを確かめられないため。
/// </summary>
public sealed class RefreshJitterTests
{
    [Theory]
    [InlineData("9900001", -1)]
    [InlineData("9900002", 1)]
    [InlineData("9900003", 3)]
    [InlineData("local-abc", -2)]
    public void 商品IDから決まる日数は_起動をまたいで同じ(string itemId, int expected)
        => Assert.Equal(expected, RefreshJitter.Days(itemId, 3));

    [Fact]
    public void 幅の中に収まり_幅が0なら0()
    {
        for (var i = 0; i < 1000; i++)
        {
            Assert.InRange(RefreshJitter.Days($"990{i:0000}", 3), -3, 3);
        }

        Assert.Equal(0, RefreshJitter.Days("9900001", 0));
    }
}
