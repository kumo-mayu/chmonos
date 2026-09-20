using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 金額の欄の読み取り（I3）。人が普通に打つ形は受け、読めないものは**黙って捨てずに** null を返す。
/// </summary>
public class MoneyTextTests
{
    [Theory]
    [InlineData("1200", 1200)]
    [InlineData(" 1200 ", 1200)]
    [InlineData("¥1,200", 1200)]
    [InlineData("￥1200", 1200)]
    [InlineData("1,200円", 1200)]
    [InlineData("１２００", 1200)]          // 全角
    [InlineData("￥１，２００", 1200)]      // 全角の記号とコンマ
    [InlineData("1200.0", 1200)]
    [InlineData("0", 0)]
    public void ReadsTheWaysPeopleActuallyType(string text, int expected)
        => Assert.Equal(expected, MoneyText.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("たぶん1200くらい")]
    [InlineData("1200.5")]   // 端数のある金額は扱わない
    [InlineData("-300")]     // 支払いに負の額は無い
    public void ReturnsNullWhenItCannotRead(string text)
        => Assert.Null(MoneyText.Parse(text));

    [Fact]
    public void TellsApartAnEmptyFieldFromAnUnreadableOne()
    {
        // 空欄は「入れていない」で、読めなかったのとは違う（注意を出すのは後者だけ）
        Assert.False(MoneyText.IsUnreadable(""));
        Assert.False(MoneyText.IsUnreadable("¥1,200"));
        Assert.True(MoneyText.IsUnreadable("だいたい千円"));
    }
}
