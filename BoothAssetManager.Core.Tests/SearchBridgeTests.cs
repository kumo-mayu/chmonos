using BoothAssetManager.Core.Search;
using Xunit;
using Xunit.Abstractions;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 打った語から別表記を作る段。辞書が配られていない環境では飛ばす。
/// </summary>
public class SearchBridgeTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _cacheDir;
    private readonly SearchBridge _bridge;

    public SearchBridgeTests(ITestOutputHelper output)
    {
        _output = output;
        _cacheDir = Path.Combine(Path.GetTempPath(), "bam-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cacheDir);
        _bridge = new SearchBridge(new JapaneseDictionary(
            DictionaryPath(),
            Path.Combine(_cacheDir, "search-bridge.cache")));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_cacheDir, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string DictionaryPath()
        => Path.Combine(AppContext.BaseDirectory, "assets", "JMdict_e.gz");

    private bool Available => File.Exists(DictionaryPath());

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

    /// <summary>日本語で打たれた語には橋を架けない。そのまま当たるので要らない。</summary>
    [Fact]
    public void DoesNothingForJapaneseInput()
    {
        Assert.Empty(_bridge.Expand("鳥"));
        Assert.Empty(_bridge.Expand("タマクラゲ"));
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
