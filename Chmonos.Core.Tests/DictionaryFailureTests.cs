using System.IO.Compression;
using System.Text;
using Chmonos.Core.Search;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 同梱の辞書が壊れている・索引の控えを書けないときに、検索を巻き込まないこと（点検 2026-09-23）。
/// 作り物の小さな辞書で試す（本物の63MBは組むのに時間が掛かり、壊れた形を作れない）。
/// </summary>
public sealed class DictionaryFailureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bam-dict-" + Guid.NewGuid().ToString("N"));

    public DictionaryFailureTests() => Directory.CreateDirectory(_dir);

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

    private string Broken(string name)
    {
        // gzip の頭（1f 8b）だけ合っていて、中身は使われていない種類の区切り（BTYPE=11）。GZipStream は InvalidDataException を投げる
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, [0x1f, 0x8b, 0x08, 0x00, 0xde, 0xad, 0xbe, 0xef, 0x00, 0x00, 0x07, 0x00, 0x00, 0x00, 0x00]);
        return path;
    }

    private string Gzip(string name, string xml)
    {
        var path = Path.Combine(_dir, name);
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        gzip.Write(Encoding.UTF8.GetBytes(xml));
        return path;
    }

    [Fact]
    public void BrokenJmdictIsReportedOnceAndSearchKeepsWorking()
    {
        var dictionary = new JapaneseDictionary(Broken("JMdict_e.gz"), Path.Combine(_dir, "bridge.cache"));

        Assert.Empty(dictionary.ByEnglish("bird"));
        Assert.NotNull(dictionary.LoadError);

        // 2回目は組み直さない（落ちたことを覚えている）
        Assert.Empty(dictionary.ByReading("とり"));
        Assert.Empty(dictionary.ByKana("とり"));
    }

    [Fact]
    public void BrokenKanjidicDoesNotThrow()
    {
        var readings = new KanjiReadings(Broken("kanjidic2.xml.gz"));

        Assert.Empty(readings.Of("鳥"));
        Assert.Empty(readings.Of("鳥"));
    }

    [Fact]
    public void IndexIsKeptWhenCacheCannotBeWritten()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <JMdict>
            <entry><k_ele><keb>鳥</keb></k_ele><r_ele><reb>とり</reb></r_ele><sense><gloss>bird</gloss></sense></entry>
            </JMdict>
            """;
        var source = Gzip("JMdict_e.gz", xml);

        // 控えを置くフォルダの名前に、ファイルが既に居座っている——書けない
        var blocker = Path.Combine(_dir, "blocked");
        File.WriteAllText(blocker, "not a folder");
        var dictionary = new JapaneseDictionary(source, Path.Combine(blocker, "bridge.cache"));

        Assert.Contains("鳥", dictionary.ByEnglish("bird"));
        Assert.Null(dictionary.LoadError);
        Assert.NotNull(dictionary.CacheSaveError);
    }
}
