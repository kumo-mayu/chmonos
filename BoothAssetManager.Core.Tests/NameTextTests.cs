using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>名前の均し方と長さの上限（I13）。</summary>
public class NameTextTests
{
    [Theory]
    [InlineData("  衣装  ", "衣装")]
    [InlineData("夏\n衣装", "夏 衣装")]          // 貼り付けの改行は空白に寄せる
    [InlineData("夏\t衣装", "夏 衣装")]
    [InlineData("夏   衣装", "夏 衣装")]          // 続いた空白は1つ
    [InlineData("\n\n", "")]
    [InlineData(null, "")]
    public void FoldsIntoOneLine(string? text, string expected)
        => Assert.Equal(expected, NameText.Normalize(text));

    [Fact]
    public void RefusesNamesLongerThanTheLimit()
    {
        Assert.False(NameText.IsTooLong(new string('あ', NameText.MaxNameLength)));
        Assert.True(NameText.IsTooLong(new string('あ', NameText.MaxNameLength + 1)));
    }

    [Fact]
    public void DoesNotCutTheNameItself()
    {
        // 切ると、打った名前と保存された名前が黙って食い違う。均すだけで長さは触らない
        var long60Plus = new string('あ', NameText.MaxNameLength + 5);
        Assert.Equal(long60Plus, NameText.Normalize(long60Plus));
    }
}
