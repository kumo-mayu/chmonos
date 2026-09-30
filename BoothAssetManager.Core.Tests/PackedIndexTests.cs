using System.Text;
using BoothAssetManager.Core.Search;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 表記をまたぐ辞書の索引を詰めて持つ形（2026-09-24）。前の形（控えを1行ずつ Split して
/// <c>Dictionary&lt;string, string[]&gt;</c> に入れる）と、引いた結果が同じであることを確かめる。
/// </summary>
public sealed class PackedIndexTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bam-packed-" + Guid.NewGuid().ToString("N"));

    public PackedIndexTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void LooksUpWhatWasAdded()
    {
        var builder = new PackedIndex.Builder(4);
        Add(builder, "ring", "指輪", "輪", "リング");
        Add(builder, "wheel", "輪", "車輪");
        Add(builder, "", "空の鍵");
        Add(builder, "blank", "", "後ろ");

        // 小さく見積もっても広げて入る（見積もりは速さのためだけ）
        for (var i = 0; i < 5000; i++)
        {
            Add(builder, "k" + i, "v" + (i % 7), "w" + i);
        }

        var index = builder.Build();

        Assert.Equal(["指輪", "輪", "リング"], index.Lookup("ring"));
        Assert.Equal(["輪", "車輪"], index.Lookup("wheel"));
        Assert.Equal(["空の鍵"], index.Lookup(""));
        Assert.Equal(["", "後ろ"], index.Lookup("blank"));
        Assert.Equal(["v1", "w4999"], index.Lookup("k4999"));

        // 表記としてだけ出てくる文字列は鍵ではない
        Assert.Empty(index.Lookup("輪"));
        Assert.Empty(index.Lookup("nothing"));
        Assert.Equal(5004, index.KeyCount);
    }

    /// <summary>同じ鍵が2度あれば後の方（前の読み方 <c>target[key] = …</c> と同じ）。書き出しも後の方だけ。</summary>
    [Fact]
    public void LaterDuplicateKeyWins()
    {
        var builder = new PackedIndex.Builder();
        Add(builder, "cat", "猫");
        Add(builder, "dog", "犬");
        Add(builder, "cat", "ネコ", "猫");
        var index = builder.Build();

        Assert.Equal(["ネコ", "猫"], index.Lookup("cat"));
        Assert.Equal(["dog", "cat"], index.Entries().Select(entry => entry.Key));
    }

    /// <summary>
    /// 手で書いた控え（CRLF・BOM・表記の無い行・3つ目より後ろの空行）を、前の読み方と同じに読む。
    /// 引かれた節だけを読み、ほかの節は持たない。
    /// </summary>
    [Fact]
    public void ReadsHandWrittenCacheLikeTheOldReader()
    {
        var (dictionary, cache) = WriteCache(
            withBom: true,
            newline: "\r\n",
            "ring\t指輪\t輪",
            "lonely",
            "shark\tサメ\t鮫",
            "",
            "とり\t鳥\t取り",
            "さめ\tサメ",
            "",
            "さめ\tshark",
            "",
            "りぼん\tribbon");

        var japanese = new JapaneseDictionary(dictionary, cache);

        Assert.Equal(["指輪", "輪"], japanese.ByEnglish("RING"));
        Assert.Equal(1, japanese.LoadedSectionCount);
        Assert.Empty(japanese.ByEnglish("lonely"));

        Assert.Equal(["鳥", "取り"], japanese.ByReading("とり"));
        Assert.Equal(2, japanese.LoadedSectionCount);

        // 3つ目より後ろの空行の先も3つ目の節（前の読み方と同じ）
        Assert.Equal(["shark"], japanese.ByKana("さめ"));
        Assert.Equal(["ribbon"], japanese.ByKana("りぼん"));
        Assert.Null(japanese.LoadError);

        AssertSameAsOldReader(japanese, cache);
    }

    /// <summary>見出しの版や辞書の印が違う控えは読まない（組み直しに回る。ここでは辞書が偽物なので組めずに理由が残る）。</summary>
    [Fact]
    public void IgnoresCacheWithOtherHeader()
    {
        var (dictionary, cache) = WriteCache(withBom: false, newline: "\n", "ring\t指輪");
        File.WriteAllText(cache, "bridge\t2\t0-0\nring\t指輪\n", new UTF8Encoding(false));

        var japanese = new JapaneseDictionary(dictionary, cache);

        Assert.Empty(japanese.ByEnglish("ring"));
        Assert.NotNull(japanese.LoadError);
    }

    /// <summary>
    /// 同梱の JMdict から組んだ本物の控えで、全部の鍵が前の読み方と同じ並びを返す（48.5万鍵）。
    /// 辞書ファイルが無い環境では飛ばす。
    /// </summary>
    [Fact]
    public void RealCacheGivesSameResultsAsTheOldReader()
    {
        if (!SharedDictionaries.JapaneseAvailable)
        {
            return;
        }

        // 組むのは一式で1回（SharedDictionaries）。XML から組んだままの索引と、そのときに書かれた控えを使う
        var built = SharedDictionaries.Japanese;
        var cache = SharedDictionaries.CachePath;
        Assert.NotEmpty(built.ByEnglish("bird"));
        Assert.True(File.Exists(cache));

        // 組んだ直後の索引と、控えから読み直した索引の両方を、前の読み方と比べる
        AssertSameAsOldReader(built, cache);
        AssertSameAsOldReader(new JapaneseDictionary(SharedDictionaries.JapanesePath, cache), cache);
    }

    private static void Add(PackedIndex.Builder builder, string key, params string[] forms)
    {
        builder.BeginKey(key);
        foreach (var form in forms)
        {
            builder.AddForm(form);
        }
    }

    private (string Dictionary, string Cache) WriteCache(bool withBom, string newline, params string[] lines)
    {
        var dictionary = Path.Combine(_dir, "JMdict_e.gz");
        File.WriteAllText(dictionary, "偽物の辞書");
        var info = new FileInfo(dictionary);
        var header = $"bridge\t3\t{info.Length}-{info.LastWriteTimeUtc.Ticks}";

        var cache = Path.Combine(_dir, "bridge.cache");
        File.WriteAllText(cache, string.Join(newline, [header, .. lines]) + newline, new UTF8Encoding(withBom));
        return (dictionary, cache);
    }

    /// <summary>前の読み方（2026-09-24 まで）：ReadLine して Split し、空行ごとに次の節へ。</summary>
    private static void AssertSameAsOldReader(JapaneseDictionary japanese, string cache)
    {
        var sections = new[]
        {
            new Dictionary<string, string[]>(StringComparer.Ordinal),
            new Dictionary<string, string[]>(StringComparer.Ordinal),
            new Dictionary<string, string[]>(StringComparer.Ordinal),
        };

        using (var reader = new StreamReader(cache, Encoding.UTF8))
        {
            reader.ReadLine();
            var section = 0;
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0)
                {
                    section = Math.Min(section + 1, 2);
                    continue;
                }

                var parts = line.Split('\t');
                if (parts.Length > 1)
                {
                    sections[section][parts[0]] = parts[1..];
                }
            }
        }

        // ByEnglish は小文字に寄せて引くので、鍵がすでに小文字の物だけで比べる（控えの英語の鍵は全部小文字）
        foreach (var (key, forms) in sections[0].Where(pair => pair.Key == pair.Key.ToLowerInvariant()))
        {
            Assert.Equal(forms, japanese.ByEnglish(key));
        }

        foreach (var (key, forms) in sections[1])
        {
            Assert.Equal(forms, japanese.ByReading(key));
        }

        foreach (var (key, forms) in sections[2])
        {
            Assert.Equal(forms, japanese.ByKana(key));
        }
    }
}
