using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>属性の分布の分け方（open.md 112）：幅 5・10・20 で区切り、値の種類が少なければ値ごとに1本。</summary>
public class StatsAttributeBarsTests
{
    private static StatsAttributeDistribution Of(params int[] values) => new()
    {
        Name = "a",
        Rated = values.Length,
        Average = values.Average(),
        Buckets = [],
        ValueCounts = values.GroupBy(v => v).OrderBy(g => g.Key).Select(g => (g.Key, g.Count())).ToList(),
    };

    /// <summary>値の種類が区切りより多い：0〜100 を幅で区切り、100 は最後の棒に入る</summary>
    [Theory]
    [InlineData(20, 5)]
    [InlineData(10, 10)]
    [InlineData(5, 20)]
    public void 区切りの数は幅で決まり_100は最後の棒に入る(int width, int bins)
    {
        var values = Enumerable.Range(0, 30).Select(i => i * 3).Append(100).ToArray();

        var bars = StatsExtras.AttributeBars(Of(values), width);

        Assert.Equal(bins, bars.Count);
        Assert.Equal(0, bars[0].From);
        Assert.Equal(width - 1, bars[0].To);
        Assert.Equal(100, bars[^1].To);
        Assert.Equal(values.Length, bars.Sum(bar => bar.Count));
        Assert.True(bars[^1].Count >= 1);
    }

    [Fact]
    public void 幅20の区切りは前の5区切りと同じ数え方()
    {
        var bars = StatsExtras.AttributeBars(Of(0, 19, 20, 39, 40, 60, 79, 80, 99, 100, 100), 20);

        Assert.Equal([2, 2, 1, 2, 4], bars.Select(bar => bar.Count));
        Assert.Equal([(0, 19), (20, 39), (40, 59), (60, 79), (80, 100)], bars.Select(bar => (bar.From, bar.To)));
    }

    /// <summary>0・25・50… と飛び飛びに付けた属性は、区切ると 25 と 30 のような別の値が混ざるので、値ごとに出す</summary>
    [Fact]
    public void 値の種類が区切りの数以下なら_値ごとに1本()
    {
        var bars = StatsExtras.AttributeBars(Of(0, 25, 25, 50, 100), 20);

        Assert.Equal([0, 25, 50, 100], bars.Select(bar => bar.From));
        Assert.All(bars, bar => Assert.Equal(bar.From, bar.To));
        Assert.Equal([1, 2, 1, 1], bars.Select(bar => bar.Count));
    }

    [Fact]
    public void 値の種類が区切りより多いと_幅を細かくすれば値ごとになる()
    {
        var distribution = Of(0, 10, 20, 30, 40, 50, 60);

        Assert.All(StatsExtras.AttributeBars(distribution, 20), bar => Assert.NotEqual(bar.From, bar.To));
        Assert.Equal(7, StatsExtras.AttributeBars(distribution, 10).Count);
        Assert.All(StatsExtras.AttributeBars(distribution, 10), bar => Assert.Equal(bar.From, bar.To));
    }
}
