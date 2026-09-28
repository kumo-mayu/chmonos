using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>検索の日付の欄に打った文字の読み方（ユーザ案 2026-09-15）。</summary>
public class DateTextTests
{
    private static readonly DateOnly Today = new(2026, 8, 1);

    private static DateOnly? Since(string text) => DateText.Parse(text, isEnd: false, Today);

    private static DateOnly? Till(string text) => DateText.Parse(text, isEnd: true, Today);

    [Theory]
    [InlineData("2026/09/01")]
    [InlineData("2026-09-01")]
    [InlineData("2026-9-1")]
    [InlineData("2026.9.1")]
    [InlineData("2026年9月1日")]
    [InlineData("20260901")]
    [InlineData("２０２６／０９／０１")]
    public void ReadsAFullDate(string text)
    {
        Assert.Equal(new DateOnly(2026, 9, 1), Since(text));
        Assert.Equal(new DateOnly(2026, 9, 1), Till(text));
    }

    /// <summary>年の無い日付は、今日以前で最も近いその日（今日が 8月1日なら 12/01 は去年）。</summary>
    [Fact]
    public void MonthAndDayMeanTheNearestPastDay()
    {
        Assert.Equal(new DateOnly(2025, 12, 1), Since("12/01"));
        Assert.Equal(new DateOnly(2026, 7, 31), Since("7/31"));
        Assert.Equal(new DateOnly(2026, 8, 1), Since("8/1"));
    }

    /// <summary>年と月だけなら、開始は月初め・終わりは月末。</summary>
    [Fact]
    public void YearAndMonthCoverTheWholeMonth()
    {
        Assert.Equal(new DateOnly(2026, 2, 1), Since("2026/02"));
        Assert.Equal(new DateOnly(2026, 2, 28), Till("2026/02"));
        Assert.Equal(new DateOnly(2024, 2, 29), Till("2024-2"));
    }

    [Fact]
    public void YearAloneCoversTheWholeYear()
    {
        Assert.Equal(new DateOnly(2025, 1, 1), Since("2025"));
        Assert.Equal(new DateOnly(2025, 12, 31), Till("2025"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("きのう")]
    [InlineData("2026/13/01")]
    [InlineData("2026/02/30")]
    [InlineData("1/2/3/4")]
    // int に入らない桁の数。前は Parse が投げて、編集画面から抜けられなくなった（点検 2026-09-28）
    [InlineData("20260920000")]
    [InlineData("2026/99999999999/1")]
    public void UnreadableTextIsNull(string text) => Assert.Null(Since(text));
}
