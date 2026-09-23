using BoothAssetManager.Core.Search;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// ローマ字を読みに直す表。通信も辞書も要らない段なので、ここだけで完結して測れる。
/// </summary>
public class RomajiReadingTests
{
    [Theory]
    [InlineData("tori", "とり")]
    [InlineData("yubiwa", "ゆびわ")]
    [InlineData("same", "さめ")]
    [InlineData("tamakurage", "たまくらげ")]
    [InlineData("kurage", "くらげ")]
    [InlineData("neko", "ねこ")]
    [InlineData("shounen", "しょうねん")]
    [InlineData("gimikku", "ぎみっく")]
    public void ReadsPlainWords(string romaji, string expected)
        => Assert.Contains(expected, RomajiReading.Readings(romaji));

    /// <summary>
    /// <c>shinon</c> は「しのん」とも「しんおん」とも読める。
    /// 前者だけを返すと「心音」に届かない——実際にかな漢字変換はそれで外した。
    /// </summary>
    [Fact]
    public void KeepsBothWaysOfReadingNBeforeAVowel()
    {
        var readings = RomajiReading.Readings("shinon");

        Assert.Contains("しのん", readings);
        Assert.Contains("しんおん", readings);
    }

    /// <summary>
    /// 語頭では「ん」の枝を作らない。日本語の語は「ん」で始まらないので、
    /// nadeoto から「んあでおと」のような読みが出るだけになる。
    /// </summary>
    [Fact]
    public void DoesNotStartAWordWithN()
    {
        var readings = RomajiReading.Readings("nadeoto");

        Assert.Contains("なでおと", readings);
        Assert.DoesNotContain(readings, r => r.StartsWith("ん", StringComparison.Ordinal));
    }

    /// <summary>子音が2つ続けば促音。ん だけは撥音なので別扱い。</summary>
    [Fact]
    public void ReadsDoubledConsonantsAsSmallTsu()
        => Assert.Contains("がっこう", RomajiReading.Readings("gakkou"));

    [Fact]
    public void ReadsNBeforeAConsonantAsN()
        => Assert.Contains("かんじ", RomajiReading.Readings("kanji"));

    /// <summary>訓令式でも引ける。人によって打ち方が違う。</summary>
    [Theory]
    [InlineData("si")]
    [InlineData("shi")]
    public void AcceptsBothRomanisationStyles(string romaji)
        => Assert.Contains("し", RomajiReading.Readings(romaji));

    /// <summary>日本語でない語は崩れた読みになるが、当たらないだけで害は無い。</summary>
    [Fact]
    public void DoesNotThrowOnNonJapaneseWords()
    {
        var readings = RomajiReading.Readings("kipfel");

        Assert.NotNull(readings);
    }

    /// <summary>読み切れない綴りは、途中まで読めていても返さない。</summary>
    [Fact]
    public void RejectsWordsItCannotFinish()
        => Assert.Empty(RomajiReading.Readings("xyzq"));

    [Fact]
    public void IgnoresWordsThatAreNotLatinLetters()
    {
        Assert.False(RomajiReading.LooksRomaji("鳥"));
        Assert.Empty(RomajiReading.Readings("鳥"));
    }

    /// <summary>カタカナ寄せは文字コードを足すだけ。辞書も通信も要らない。</summary>
    [Fact]
    public void ConvertsToKatakana()
    {
        Assert.Equal("タマクラゲ", RomajiReading.ToKatakana("たまくらげ"));
        Assert.Equal("サメ", RomajiReading.ToKatakana("さめ"));
    }

    /// <summary>
    /// 枝分かれが続いて最後で読めない語でも、すぐ返る（点検 2026-09-23）。
    /// 上限が無いと 2字ごとに倍になり、60字で数分かかる。検索欄に長い英字を貼るだけで固まっていた。
    /// </summary>
    [Fact]
    public void GivesUpQuicklyOnLongBranchingInput()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.Empty(RomajiReading.Readings(string.Concat(Enumerable.Repeat("na", 40)) + "x"));

        Assert.True(watch.ElapsedMilliseconds < 2000, $"{watch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void StillReadsOrdinaryWordsWithTheLimit()
        => Assert.Contains("しんおん", RomajiReading.Readings("shinon"));
}
