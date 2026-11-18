using Chmonos.App.ViewModels;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>統計の属性の分布の札（open.md 112）：区切りのときは下に「0」「100」、値ごとのときは棒の下に値。吹き出しは範囲と件数。</summary>
public sealed class StatsAttributeRowTests
{
    private static StatsAttributeDistribution Of(params int[] values) => new()
    {
        Name = "a",
        Rated = values.Length,
        Average = values.Average(),
        Buckets = [],
        ValueCounts = values.GroupBy(v => v).OrderBy(g => g.Key).Select(g => (g.Key, g.Count())).ToList(),
    };

    [Fact]
    public void 値ごとのときは棒の下に値を出す()
    {
        var row = new AttributeDistributionRowViewModel(Of(0, 50, 50, 100), 20);

        Assert.True(row.IsByValue);
        Assert.Equal(["0", "50", "100"], row.Bars.Select(bar => bar.ValueLabel));
        Assert.Equal("50%：2 件", row.Bars[1].Tooltip);
        Assert.Equal(AttributeDistributionRowViewModel.ChartHeight, row.Bars[1].Height);
    }

    [Fact]
    public void 区切りのときは範囲を吹き出しに出し_棒の下は空()
    {
        var row = new AttributeDistributionRowViewModel(Of(0, 10, 20, 30, 40, 50, 100), 20);

        Assert.False(row.IsByValue);
        Assert.All(row.Bars, bar => Assert.Empty(bar.ValueLabel));
        Assert.Equal("80〜100%：1 件", row.Bars[^1].Tooltip);
    }
}
