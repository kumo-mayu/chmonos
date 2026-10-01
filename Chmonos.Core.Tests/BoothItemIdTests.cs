using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

public class BoothItemIdTests
{
    [Theory]
    [InlineData("1234567", "1234567")]
    [InlineData("  1234567  ", "1234567")]
    [InlineData("https://booth.pm/ja/items/1234567", "1234567")]
    [InlineData("https://booth.pm/items/1234567", "1234567")]
    [InlineData("https://shop.booth.pm/items/1234567", "1234567")]
    [InlineData("https://booth.pm/en/items/1234567?utm=1", "1234567")]
    public void ReadsIdFromNumberOrUrl(string input, string expected)
        => Assert.Equal(expected, BoothItemId.Parse(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("https://example.com/items/1234567")]
    [InlineData("くうた")]
    public void ReturnsNullWhenNotAnId(string? input)
        => Assert.Null(BoothItemId.Parse(input));
}
