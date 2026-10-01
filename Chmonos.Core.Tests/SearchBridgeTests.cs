using Chmonos.Core.Search;
using Xunit;
using Xunit.Abstractions;

namespace Chmonos.Core.Tests;

/// <summary>
/// 打った語から別表記を作る段。辞書が配られていない環境では飛ばす。
/// </summary>
public class SearchBridgeTests
{
    private readonly ITestOutputHelper _output;

    // 辞書は一式で1回だけ組んだ物を使う（組むのに数秒かかる。SharedDictionaries）
    private readonly SearchBridge _bridge = SharedDictionaries.Bridge;

    public SearchBridgeTests(ITestOutputHelper output) => _output = output;

    private static bool Available => SharedDictionaries.JapaneseAvailable;

    private IReadOnlyList<string> Texts(string word)
    {
        var candidates = _bridge.Expand(word);
        _output.WriteLine($"{word} → " + string.Join(" ", candidates.Select(c => $"{c.Text}({c.Via})")));
        return candidates.Select(c => c.Text).ToList();
    }

    /// <summary>ユーザが最初に挙げた例。`tori` から「鳥」に届くこと。</summary>
    [Fact]
    public void ReachesKanjiFromRomaji()
    {
        if (!Available)
        {
            return;
        }

        Assert.Contains("鳥", Texts("tori"));
        Assert.Contains("指輪", Texts("yubiwa"));
        Assert.Contains("心音", Texts("shinon"));
    }

    /// <summary>英語からも引ける。</summary>
    [Fact]
    public void ReachesJapaneseFromEnglish()
    {
        if (!Available)
        {
            return;
        }

        Assert.Contains("サメ", Texts("shark"));
        Assert.Contains("クラゲ", Texts("jellyfish"));
        Assert.Contains("少年", Texts("boy"));
    }

    /// <summary>カタカナ寄せは辞書に無い語でも効く。造語の商品名はここで拾う。</summary>
    [Fact]
    public void ReachesKatakanaWithoutTheDictionary()
    {
        var candidates = _bridge.Expand("tamakurage").Select(c => c.Text).ToList();

        Assert.Contains("タマクラゲ", candidates);
        Assert.Contains("たまくらげ", candidates);
    }

    /// <summary>
    /// 1文字のかなは候補にしない。「わ」「め」が無関係な語の中に当たってしまう。
    /// 漢字1文字は残す——短いが、当たれば正しいことが多い。
    /// </summary>
    [Fact]
    public void DropsSingleKanaCandidates()
    {
        if (!Available)
        {
            return;
        }

        Assert.DoesNotContain("わ", Texts("ring"));
        Assert.Contains("鳥", Texts("bird"));
    }

    /// <summary>漢字を含む語には橋を架けない。そのまま当たるので要らない。</summary>
    [Fact]
    public void DoesNothingForKanjiInput()
    {
        Assert.Empty(_bridge.Expand("鳥"));
        Assert.Empty(_bridge.Expand("撫で音"));
    }

    /// <summary>ひらがなで打った語から漢字へ（漢字変換・2026-09-16）。</summary>
    [Fact]
    public void ReachesKanjiFromHiragana()
    {
        if (!Available)
        {
            return;
        }

        Assert.Contains("鳥", Texts("とり"));
        Assert.Contains("指輪", Texts("ゆびわ"));
    }

    /// <summary>カタカナで打った外来語から英語へ（日英変換・2026-09-16）。</summary>
    [Fact]
    public void ReachesEnglishFromKatakana()
    {
        if (!Available)
        {
            return;
        }

        Assert.Contains("shark", Texts("サメ"));
        Assert.Contains("ribbon", Texts("リボン"));
    }

    /// <summary>道ごとに切れる（ユーザ案：別表記の中身をそれぞれトグル）。</summary>
    [Fact]
    public void RoutesCanBeTurnedOff()
    {
        if (!Available)
        {
            return;
        }

        var noKanji = _bridge.Expand("tori", new BridgeOptions(Kanji: false)).Select(c => c.Text).ToList();
        Assert.Contains("とり", noKanji);
        Assert.DoesNotContain("鳥", noKanji);

        Assert.DoesNotContain("サメ", _bridge.Expand("shark", new BridgeOptions(EnglishToJapanese: false)).Select(c => c.Text));
        Assert.Empty(_bridge.Expand("サメ", new BridgeOptions(JapaneseToEnglish: false)));
        Assert.DoesNotContain("とり", _bridge.Expand("tori", new BridgeOptions(Romaji: false)).Select(c => c.Text));
    }

    /// <summary>1文字の入力には架けない。候補が多すぎて絞り込みにならない。</summary>
    [Fact]
    public void DoesNothingForASingleLetter() => Assert.Empty(_bridge.Expand("a"));

    /// <summary>打った語そのものは候補に入れない。二重に探しても結果は変わらない。</summary>
    [Fact]
    public void DoesNotRepeatTheWordItself()
    {
        if (!Available)
        {
            return;
        }

        Assert.DoesNotContain("bird", Texts("bird"));
    }
}
