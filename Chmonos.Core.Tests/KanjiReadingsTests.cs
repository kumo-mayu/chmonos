using Chmonos.Core.Search;
using Xunit;
using Xunit.Abstractions;

namespace Chmonos.Core.Tests;

/// <summary>
/// 漢字1字ごとの音訓から商品名の読みを組み立てる段。
/// 辞書に載っていない造語（撫で音）のためにある。
/// </summary>
public class KanjiReadingsTests
{
    private readonly ITestOutputHelper _output;
    private readonly KanjiReadings _readings;

    public KanjiReadingsTests(ITestOutputHelper output)
    {
        _output = output;
        _readings = SharedDictionaries.Readings;
    }

    private bool Available => _readings.IsAvailable;

    private IReadOnlyList<string> Of(string text)
    {
        var result = _readings.Of(text);
        _output.WriteLine($"{text} → {result.Count}通り: {string.Join(" ", result.Take(8))}");
        return result;
    }

    /// <summary>これが本命。どの語彙辞書にも載っていない複合語を継いで読む。</summary>
    [Fact]
    public void ReadsCompoundsThatNoWordDictionaryHas()
    {
        if (!Available)
        {
            return;
        }

        Assert.Contains("なでおと", Of("撫で音"));
    }

    /// <summary>送り仮名は表記の一部として消費する。二重に読むと「なででおと」になる。</summary>
    [Fact]
    public void ConsumesOkuriganaInsteadOfReadingItTwice()
    {
        if (!Available)
        {
            return;
        }

        var readings = Of("撫で音");

        Assert.DoesNotContain("なででおと", readings);
    }

    [Fact]
    public void ReadsOrdinaryWords()
    {
        if (!Available)
        {
            return;
        }

        Assert.Contains("しんおん", Of("心音"));
        Assert.Contains("ゆびわ", Of("指輪"));
        Assert.Contains("とり", Of("鳥"));
    }

    /// <summary>
    /// 商品名まるごとから読める。カタカナやラテン文字で切れた区間ごとに作る。
    /// 「【フカさんの！】撫で音ギミック【VRChat】」なら「撫で音」の区間が対象。
    /// </summary>
    [Fact]
    public void ReadsSpansInsideAProductName()
    {
        if (!Available)
        {
            return;
        }

        Assert.Contains("なでおと", Of("【フカさんの！】撫で音ギミック【VRChat】【MA式】"));
    }

    /// <summary>漢字が1字も無ければ作らない。カタカナはカタカナ寄せの側で拾う。</summary>
    [Fact]
    public void MakesNothingWithoutKanji()
    {
        if (!Available)
        {
            return;
        }

        Assert.Empty(Of("タマクラゲ"));
        Assert.Empty(Of("Kuuta3D"));
    }

    /// <summary>組み合わせは積で増えるので、上限で頭打ちにする。</summary>
    [Fact]
    public void StaysBoundedOnLongNames()
    {
        if (!Available)
        {
            return;
        }

        var readings = Of("真剣勝負山田太郎商店街");

        Assert.True(readings.Count <= 48, $"{readings.Count} 通りは多すぎる");
    }
}
