using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>検索の価格の「外れ値を無視」の境（95%の位置の5倍以上・ユーザ判断 2026-09-16）。</summary>
public class OutliersTests
{
    /// <summary>普通の価格の並び（100〜8,500円）。上位の刻みは1.2倍前後。</summary>
    private static List<int> Ordinary()
    {
        var values = new List<int>();
        for (var i = 0; i < 95; i++)
        {
            values.Add(100 + (i * 20));
        }

        values.AddRange([3000, 4000, 5000, 6600, 8500]);
        return values;
    }

    [Fact]
    public void 支援用の桁違いの価格だけが外れ値になる()
    {
        var values = Ordinary();
        values.Add(99_999);

        var fence = Outliers.UpperFence(values);

        Assert.NotNull(fence);
        Assert.True(99_999 >= fence);
        Assert.True(8_500 < fence, $"普通の高い商品は残す（境 {fence}）");
    }

    /// <summary>止め値が近い値で何段もあると、隣との差では取りこぼす。基準との比なら全部取れる。</summary>
    [Fact]
    public void 止め値が何段あってもまとめて外れ値になる()
    {
        var values = Ordinary();
        values.AddRange([30_000, 50_000, 99_999]);

        var fence = Outliers.UpperFence(values)!.Value;

        Assert.All(new[] { 30_000, 50_000, 99_999 }, value => Assert.True(value >= fence));
        Assert.True(8_500 < fence);
    }

    [Fact]
    public void 外れ値が無ければ手元の最大も残る()
    {
        var values = Ordinary();

        Assert.True(values.Max() < Outliers.UpperFence(values));
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 0, 0, 0, 0 })]
    public void 数が無いか基準が0なら外れ値は無い(int[] values)
        => Assert.Null(Outliers.UpperFence(values));
}
