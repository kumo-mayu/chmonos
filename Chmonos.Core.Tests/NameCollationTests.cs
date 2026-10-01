using System.Diagnostics;
using Chmonos.Core.Search;
using Chmonos.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace Chmonos.Core.Tests;

/// <summary>
/// 名前の読みの順（検索の「名前」「ショップ」の並べ替え・ユーザ判断 2026-09-24）。
/// 商品名はすべて作り物。
/// </summary>
public sealed class NameCollationTests
{
    private static KanjiReadings Readings => SharedDictionaries.Readings;

    private readonly ITestOutputHelper _output;

    public NameCollationTests(ITestOutputHelper output) => _output = output;

    private static string[] Sorted(NameCollation collation, params string[] names)
        => names.OrderBy(name => name, collation).ToArray();

    /// <summary>前は【】の付いた名前が符号の順で固まっていた。先頭の括弧の塊は飛ばして比べる。</summary>
    [Fact]
    public void 先頭の括弧の塊は飛ばして比べる()
    {
        var sorted = Sorted(NameCollation.Plain, "【期間限定】【新作】さくらの服", "あおい靴", "［3D］かえでの帽子", "(無料) いちょうの髪飾り");

        Assert.Equal(["あおい靴", "(無料) いちょうの髪飾り", "［3D］かえでの帽子", "【期間限定】【新作】さくらの服"], sorted);
    }

    [Fact]
    public void 括弧しか無い名前は括弧の中身で比べる()
    {
        Assert.Equal("せつと", NameCollation.BuildKey("【セット】", null));
        Assert.Equal(["【あ】", "い", "【う】"], Sorted(NameCollation.Plain, "【う】", "い", "【あ】"));
    }

    [Fact]
    public void 記号と空白は飛ばして比べる()
        => Assert.Equal(["★あ い", "あう"], Sorted(NameCollation.Plain, "あう", "★あ い"));

    [Fact]
    public void カタカナはひらがなに寄せる()
    {
        // 符号の順だとカタカナ（ウ・カ）はひらがな（い・き）の後ろにまとまった
        Assert.Equal(["いか", "ウサギ", "カメ", "きつね"], Sorted(NameCollation.Plain, "きつね", "カメ", "いか", "ウサギ"));
        Assert.Equal(NameCollation.BuildKey("ｳｻｷﾞ", null), NameCollation.BuildKey("うさぎ", null));
    }

    [Fact]
    public void 長音は前のかなの母音として比べる()
        => Assert.Equal(["ケイト", "ケーキ", "ケオ"], Sorted(NameCollation.Plain, "ケオ", "ケーキ", "ケイト"));

    [Fact]
    public void 英字は大文字小文字を区別しない()
    {
        Assert.Equal(["apple", "Banana", "cherry"], Sorted(NameCollation.Plain, "cherry", "Banana", "apple"));
        Assert.Equal(NameCollation.BuildKey("ＡＢＣ", null), NameCollation.BuildKey("abc", null));
    }

    [Fact]
    public void 数字は数として比べる()
        => Assert.Equal(["衣装 2", "衣装 10", "衣装 100"], Sorted(NameCollation.Plain, "衣装 100", "衣装 2", "衣装 10"));

    [Fact]
    public void 数字_英字_かなの順()
        => Assert.Equal(["2本セット", "Zebra", "あひる"], Sorted(NameCollation.Plain, "あひる", "Zebra", "2本セット"));

    /// <summary>漢字は読みで比べる。符号の順だと 犬(72AC) 猫(732B) 鳥(9CE5) の順で、カタカナとも混ざらなかった。</summary>
    [Fact]
    public void 漢字は読みで比べる()
    {
        if (!Readings.IsAvailable)
        {
            return;
        }

        var collation = new NameCollation(Readings);
        var sorted = Sorted(collation, "猫", "鳥", "カメ", "犬", "ウサギ");

        Assert.Equal(["犬", "ウサギ", "カメ", "鳥", "猫"], sorted);
    }

    /// <summary>熟語は音、1字は訓で読む。送り仮名はそのまま続く（音が2つ以上ある字は最初の音なので、衣装は「いそう」と外れる）。</summary>
    [Fact]
    public void 熟語は音_1字は訓_送り仮名は続ける()
    {
        if (!Readings.IsAvailable)
        {
            return;
        }

        Assert.Equal("まほう", NameCollation.BuildKey("魔法", Readings));
        Assert.Equal("とり", NameCollation.BuildKey("鳥", Readings));
        Assert.Equal("なでおと", NameCollation.BuildKey("撫で音", Readings));
        Assert.Equal(NameCollation.BuildKey("人人", Readings), NameCollation.BuildKey("人々", Readings));
    }

    [Fact]
    public void 同じ名前の鍵は控えから使い回す()
    {
        var collation = new NameCollation(null);

        Assert.Same(collation.KeyOf("【新作】うさぎの服"), collation.KeyOf("【新作】うさぎの服"));
    }

    /// <summary>
    /// 鍵を作る時間の目安（2000件）。検索の照合（約0.7ms）と読み直しの速さを壊さないかを見る。
    /// 時計に結果を左右させないので、数は出すだけで合否にしない。
    /// </summary>
    [Fact]
    public void 鍵を作る時間を出す()
    {
        if (!Readings.IsAvailable)
        {
            return;
        }

        Readings.Prepare();
        var names = Enumerable.Range(0, 2000)
            .Select(index => $"【{index % 7}周年】撫で音の衣装セット Ver.{index} ウサギ耳 {index % 13}色")
            .ToList();

        var collation = new NameCollation(Readings);
        var build = Stopwatch.StartNew();
        foreach (var name in names)
        {
            _ = collation.KeyOf(name);
        }

        build.Stop();

        // 並べる時間は、JIT を済ませた2回目を測る
        _ = names.OrderBy(collation.SortKeyOf).ToList();
        var sort = Stopwatch.StartNew();
        _ = names.OrderBy(collation.SortKeyOf).ToList();
        sort.Stop();

        _ = names.OrderBy(name => name, StringComparer.CurrentCulture).ToList();
        var before = Stopwatch.StartNew();
        _ = names.OrderBy(name => name, StringComparer.CurrentCulture).ToList();
        before.Stop();

        _output.WriteLine($"鍵を作る：2000件で {build.Elapsed.TotalMilliseconds:F1}ms／控えた鍵で並べる：{sort.Elapsed.TotalMilliseconds:F2}ms／前の並び（CurrentCulture）：{before.Elapsed.TotalMilliseconds:F2}ms");
    }
}
