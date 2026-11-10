using Chmonos.Core.Search;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// かなの見分けと寄せ合い（<see cref="Kana"/>）、書庫の見分け（<see cref="ArchiveFiles"/>）。
/// 10か所ほどに写していた式を1つにしたので、範囲の端と、長音・中黒を含めないことを確かめる。
/// </summary>
public class KanaTests
{
    [Fact]
    public void ConvertsOnlyThePairedRange()
    {
        Assert.Equal("ぁゖーヷ・", Kana.ToHiragana("ァヶーヷ・"));
        Assert.Equal("ァヶー・", Kana.ToKatakana("ぁゖー・"));
        Assert.Equal("あabc漢", Kana.ToHiragana("アabc漢"));
    }

    [Fact]
    public void LongVowelAndMiddleDotAreNotKana()
    {
        Assert.False(Kana.IsHiragana('ー'));
        Assert.False(Kana.IsKatakana('ー'));
        Assert.False(Kana.IsKatakana('・'));
        Assert.True(Kana.IsKanaOrLongVowel('ー'));
        Assert.False(Kana.IsKanaOrLongVowel('・'));
    }

    [Fact]
    public void FirstExistingZipSkipsMissingAndNonZip()
    {
        string[] paths = [@"D:\a\x.rar", @"D:\a\gone.zip", @"D:\a\here.ZIP"];

        Assert.Equal(@"D:\a\here.ZIP", ArchiveFiles.FirstExistingZip(paths, path => !path.Contains("gone", StringComparison.Ordinal)));
        Assert.Null(ArchiveFiles.FirstExistingZip(paths, _ => false));
        Assert.True(ArchiveFiles.IsArchive(@"D:\a\x.7Z"));
        Assert.False(ArchiveFiles.IsArchive(@"D:\a\x.unitypackage"));
    }
}
