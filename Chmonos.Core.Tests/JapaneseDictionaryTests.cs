using System.Diagnostics;
using Chmonos.Core.Search;
using Xunit;
using Xunit.Abstractions;

namespace Chmonos.Core.Tests;

/// <summary>
/// 同梱のJMdictから引けるか。辞書ファイルが無い環境では飛ばす。
/// </summary>
public class JapaneseDictionaryTests
{
    private readonly ITestOutputHelper _output;

    // 一式で1回だけ XML から組んだ物を使う（組むのに数秒かかる。SharedDictionaries）
    private readonly JapaneseDictionary _dictionary = SharedDictionaries.Japanese;

    public JapaneseDictionaryTests(ITestOutputHelper output) => _output = output;

    private static bool Available => SharedDictionaries.JapaneseAvailable;

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

        // 組むのは一式で1回（SharedDictionaries）。そのときにかかった時間と、そのときに書かれた控えを見る
        var first = SharedDictionaries.BuildTime;
        Assert.NotEmpty(_dictionary.ByEnglish("bird"));

        var cache = SharedDictionaries.CachePath;
        Assert.True(File.Exists(cache));

        var second = new JapaneseDictionary(SharedDictionaries.JapanesePath, cache);
        var reload = Stopwatch.StartNew();
        Assert.Contains("鳥", second.ByEnglish("bird"));
        reload.Stop();

        _output.WriteLine($"組み上げ {(long)first.TotalMilliseconds}ms / キャッシュ読み {reload.ElapsedMilliseconds}ms "
            + $"/ キャッシュ {new FileInfo(cache).Length / 1024 / 1024}MB");

        Assert.True(reload.Elapsed < first);
    }
}
