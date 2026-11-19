using Chmonos.Core.Models;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>1行の見出しの大きさの段（設定 compactHeaderPercent。ユーザ判断 2026-11-20）。</summary>
public class CompactHeaderSizeTests
{
    [Fact]
    public void DefaultIsTheOldHeight()
    {
        // 前からの高さ 52 を既定のまま変えない（既存の人の見た目が変わらない）
        Assert.Equal(100, new AppSettings().CompactHeaderPercent);
        Assert.Equal(52, CompactHeaderSize.Height(CompactHeaderSize.DefaultPercent));
    }

    [Theory]
    [InlineData(100, 52)]
    [InlineData(125, 65)]
    [InlineData(150, 78)]
    public void HeightScalesWithPercent(int percent, double expected)
        => Assert.Equal(expected, CompactHeaderSize.Height(percent));

    [Theory]
    [InlineData(0, 100)]
    [InlineData(110, 100)]
    [InlineData(113, 125)]
    [InlineData(140, 150)]
    [InlineData(999, 150)]
    public void NormalizeRoundsHandWrittenValuesToAStep(int input, int expected)
        => Assert.Equal(expected, CompactHeaderSize.Normalize(input));

    [Fact]
    public void MissingKeyInJsonReadsAsDefault()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}");
        Assert.Equal(100, settings!.CompactHeaderPercent);
    }
}
