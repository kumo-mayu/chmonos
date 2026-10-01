using System.Diagnostics;
using Chmonos.Core.Search;

namespace Chmonos.Core.Tests;

/// <summary>
/// 同梱の辞書（JMdict・KANJIDIC2）を、一式の間で1回だけ組んで使い回す。
///
/// JMdict は素の XML が63MBあり、索引を組むのに単独で約3秒、ほかの試験と並んで走ると6〜18秒かかる。
/// 前は試験1件ごとに新しい一時フォルダで組み直していて、一式で30回ほど組んでいた
/// （2026-09-30 に測った：辞書を引く試験だけで、1件ずつの時間の合計が約230秒）。
///
/// **使い回してよいのは、組んだ後は誰も書き換えないから。**
/// <see cref="JapaneseDictionary"/> も <see cref="KanjiReadings"/> も、最初に引かれたときに錠の中で組み、
/// その後は読むだけ（引いた結果は毎回新しい配列で返る）。控えのファイルも、ここが1回書いた後は読むだけにする——
/// 控えを書き換える試験・壊す試験は、自分の一時フォルダに自分の控えを作る（<c>PackedIndexTests</c>・<c>LazyReadingsTests</c>）。
///
/// 前の一式が残した控えは読まない（プロセスごとに新しいフォルダ）。組み方を変えたのに古い控えで通ってしまうのを避ける。
/// </summary>
internal static class SharedDictionaries
{
    private const string DirectoryPrefix = "bam-shared-dict-";

    /// <summary>
    /// 前の一式の残りとみなす古さ。一式は2分かからないので、10分触られていなければ、隣で走っている別の一式の物ではない
    /// （<c>Chmonos.App.Tests</c> の一時フォルダと同じ決め方）
    /// </summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    private static readonly string CacheDirectory = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Environment.ProcessId);

    private static readonly Lazy<(JapaneseDictionary Dictionary, TimeSpan BuildTime)> Built = new(Build);

    private static readonly Lazy<SearchBridge> SharedBridge = new(() => new SearchBridge(Japanese));

    private static readonly Lazy<KanjiReadings> SharedReadings = new(() => new KanjiReadings(KanjiPath));

    /// <summary>テストはビルド出力から走るので、そこに配られた辞書を見る。</summary>
    public static string JapanesePath => Path.Combine(AppContext.BaseDirectory, "assets", "JMdict_e.gz");

    public static string KanjiPath => Path.Combine(AppContext.BaseDirectory, "assets", "kanjidic2.xml.gz");

    /// <summary>辞書ファイルが配られているか。無い環境では、辞書を引く試験は飛ばす。</summary>
    public static bool JapaneseAvailable => File.Exists(JapanesePath);

    /// <summary>XML から組んだ索引（控えから読んだ物ではない）。3つの節を全部持っている。</summary>
    public static JapaneseDictionary Japanese => Built.Value.Dictionary;

    /// <summary>XML から組んで控えを書くまでにかかった時間。</summary>
    public static TimeSpan BuildTime => Built.Value.BuildTime;

    /// <summary><see cref="Japanese"/> が組んだときに書いた控え。**読むだけにする。**</summary>
    public static string CachePath => Path.Combine(CacheDirectory, "search-bridge.cache");

    public static SearchBridge Bridge => SharedBridge.Value;

    /// <summary>字の表（控えなしで XML から組む）。</summary>
    public static KanjiReadings Readings => SharedReadings.Value;

    private static (JapaneseDictionary, TimeSpan) Build()
    {
        foreach (var old in Directory.EnumerateDirectories(Path.GetTempPath(), DirectoryPrefix + "*"))
        {
            if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(old) > StaleAfter)
            {
                TryDelete(old);
            }
        }

        Directory.CreateDirectory(CacheDirectory);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDelete(CacheDirectory);

        var dictionary = new JapaneseDictionary(JapanesePath, CachePath);
        var watch = Stopwatch.StartNew();
        if (JapaneseAvailable)
        {
            // 引くと組む。ここで組み切っておけば、この後は誰が先に引いても読むだけになる
            dictionary.ByEnglish("bird");
        }

        watch.Stop();
        return (dictionary, watch.Elapsed);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 消せなくても試験は通る（次の一式の始めにもう一度試す）
        }
    }
}
