using System.Diagnostics;
using BoothAssetManager.Core.Search;
using Xunit;
using Xunit.Abstractions;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 同梱のJMdictから引けるか。辞書ファイルが無い環境では飛ばす。
/// </summary>
public class JapaneseDictionaryTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _cacheDir;
    private readonly JapaneseDictionary _dictionary;

    public JapaneseDictionaryTests(ITestOutputHelper output)
    {
        _output = output;
        _cacheDir = Path.Combine(Path.GetTempPath(), "bam-dict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cacheDir);
        _dictionary = new JapaneseDictionary(
            DictionaryPath(),
            Path.Combine(_cacheDir, "search-bridge.cache"));
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

    /// <summary>テストはビルド出力から走るので、そこに配られた辞書を見る。</summary>
    private static string DictionaryPath()
        => Path.Combine(AppContext.BaseDirectory, "assets", "JMdict_e.gz");

    private bool Available => File.Exists(DictionaryPath());

    [Fact]
    public void FindsJapaneseWordsFromEnglish()
    {
        if (!Available)
        {
            return;
        }

        // かなで書くのが普通の語は、かなが先に来ていないと手元の商品名に当たらない
        Assert.Contains("サメ", _dictionary.ByEnglish("shark"));
        Assert.Contains("クラゲ", _dictionary.ByEnglish("jellyfish"));

        Assert.Contains("鳥", _dictionary.ByEnglish("bird"));
        Assert.Contains("猫", _dictionary.ByEnglish("cat"));
        Assert.Contains("ギミック", _dictionary.ByEnglish("gimmick"));
        Assert.Contains("アクセサリー", _dictionary.ByEnglish("accessory"));
        Assert.Contains("少年", _dictionary.ByEnglish("boy"));
    }

    /// <summary>
    /// 「1番目の意味かどうか」を頻度より強く見ていないと、
    /// ring が「核」「輪」だけになって指輪に届かない。
    /// </summary>
    [Fact]
    public void PutsThePrimarySenseFirst()
    {
        if (!Available)
        {
            return;
        }

        Assert.Contains("指輪", _dictionary.ByEnglish("ring"));
        Assert.Contains("音", _dictionary.ByEnglish("sound"));
        Assert.Contains("ペン", _dictionary.ByEnglish("pen"));
    }

    /// <summary>読みからも引ける。かな漢字変換APIが外した「しんおん→心音」も当たる。</summary>
    [Fact]
    public void FindsJapaneseWordsFromReadings()
    {
        if (!Available)
        {
            return;
        }

        Assert.Contains("鳥", _dictionary.ByReading("とり"));
        Assert.Contains("指輪", _dictionary.ByReading("ゆびわ"));
        Assert.Contains("心音", _dictionary.ByReading("しんおん"));
        Assert.Contains("少年", _dictionary.ByReading("しょうねん"));
        Assert.Contains("サメ", _dictionary.ByReading("さめ"));
    }

    /// <summary>読めなかったときは理由が残る。黙って引けなくなると原因を追えない。</summary>
    [Fact]
    public void ReportsWhyItCouldNotBuildTheIndex()
    {
        if (!Available)
        {
            return;
        }

        _dictionary.ByEnglish("bird");
        _output.WriteLine("LoadError: " + (_dictionary.LoadError ?? "(なし)"));
        Assert.Null(_dictionary.LoadError);
    }

    [Fact]
    public void ReturnsNothingForWordsItDoesNotKnow()
    {
        if (!Available)
        {
            return;
        }

        Assert.Empty(_dictionary.ByEnglish("zzzznotaword"));
        Assert.Empty(_dictionary.ByReading("ずずずず"));
    }

    /// <summary>
    /// 2回目はキャッシュから読む。63MBのXMLを毎回開いていては検索の途中に挟めない。
    /// </summary>
    [Fact]
    public void BuildsOnceAndReadsTheCacheAfterwards()
    {
        if (!Available)
        {
            return;
        }

        var first = Stopwatch.StartNew();
        Assert.NotEmpty(_dictionary.ByEnglish("bird"));
        first.Stop();

        var cache = Path.Combine(_cacheDir, "search-bridge.cache");
        Assert.True(File.Exists(cache));

        var second = new JapaneseDictionary(DictionaryPath(), cache);
        var reload = Stopwatch.StartNew();
        Assert.Contains("鳥", second.ByEnglish("bird"));
        reload.Stop();

        _output.WriteLine($"組み上げ {first.ElapsedMilliseconds}ms / キャッシュ読み {reload.ElapsedMilliseconds}ms "
            + $"/ キャッシュ {new FileInfo(cache).Length / 1024 / 1024}MB");

        Assert.True(reload.ElapsedMilliseconds < first.ElapsedMilliseconds);
    }
}
