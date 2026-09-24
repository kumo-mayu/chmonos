using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using BoothZipInspector;
using BoothZipInspector.Models;

namespace BoothAssetManager.Core.Resolution;

public sealed class UnityPackageHints
{
    /// <summary><c>Assets/&lt;作者&gt;/...</c> の作者にあたる部分。ショップ名との照合に使う。</summary>
    public IReadOnlyList<string> AuthorNamespaces { get; init; } = [];

    /// <summary><c>Assets/&lt;作者&gt;/&lt;商品&gt;</c> の商品にあたる部分。</summary>
    public IReadOnlyList<string> ProductNamespaces { get; init; } = [];

    /// <summary>同梱テキスト（Readme等）から見つかったBOOTHの手掛かり。</summary>
    public IReadOnlyList<BoothClue> Clues { get; init; } = [];

    public bool IsEmpty => AuthorNamespaces.Count == 0 && ProductNamespaces.Count == 0 && Clues.Count == 0;
}

/// <summary>
/// ZIP内の <c>.unitypackage</c> を、ディスクへ展開せずメモリ上で読む。
/// <c>.unitypackage</c> は tar.gz で、各アセットが <c>&lt;guid&gt;/pathname</c>（Unity上のパス）と
/// <c>&lt;guid&gt;/asset</c>（本体）に分かれている。tarは逐次読みなので出現順に依存しないよう guid で突き合わせる。
///
/// 作者名前空間は実測で有効な手掛かりだった（未解決だった5本すべてで
/// MOCHIYAMA / PLUSONE / Kaerimichi / TinmeshiTei / FREYSIA が取れた）。
/// ただしこれ自体は商品IDではないので、検索で出した候補を検証する材料として使う。
/// </summary>
public static class UnityPackageInspector
{
    private const long MaxTextAssetBytes = 2 * 1024 * 1024;
    private const int MaxAssetPaths = 20000;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".url", ".json", ".html", ".htm", ".xml", ".yaml", ".yml", ".asset",
    };

    public static UnityPackageHints Inspect(string zipPath)
    {
        var authors = new List<string>();
        var products = new List<string>();
        var collector = new BoothClueCollector();

        try
        {
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, ZipNameEncoding.Instance);

            foreach (var entry in archive.Entries)
            {
                if (!entry.Name.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ReadPackage(entry, authors, products, collector);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new UnityPackageHints();
        }

        return new UnityPackageHints
        {
            AuthorNamespaces = authors.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ProductNamespaces = products.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Clues = collector.Clues,
        };
    }

    /// <summary>pathname より先に来た本体を、中を見ずに取っておく大きさ（これより大きい物は頭が文字に見えるときだけ）。</summary>
    private const int KeepWithoutLookingBytes = 16 * 1024;

    /// <summary>頭が文字に見えるかを見る長さ。</summary>
    private const int TextProbeBytes = 512;

    /// <summary>
    /// pathname を待って取っておく本体の合計の上限。超えた分は取っておかず、要ると分かったら読み直す（<see cref="ReadAgain"/>）。
    /// </summary>
    private const long PendingBudgetBytes = 32L * 1024 * 1024;

    /// <summary>元の作りで、pathname を待って取っておいた本体の数の上限。これを超えた本体は手掛かりに使わない（結果を変えないために守る）。</summary>
    private const int MaxPendingAssets = 2000;

    /// <summary>
    /// 1つの unitypackage を読む。
    ///
    /// **pathname より先に来た本体を、2MB までの物は全部メモリに写して取っておいていた**（最大2000件）。
    /// 何の本体かは pathname が来るまで分からないので、画像やモデルの本体まで写し、しかも一旦書き溜めてから配列へ写し直していた
    /// （作り物の unitypackage で割り当て 761MB）。手掛かりに使うのは文章の本体だけなので：
    /// <list type="bullet">
    /// <item>取っておくのは小さい物（16KB 以下）か、頭が文字に見える物だけ。合計 32MB まで</item>
    /// <item>取っておかなかった本体が、後から来た pathname で文章と分かったら、その本体だけ2周目で読み直す</item>
    /// <item>本体は大きさの分だけの配列へ直に読む（書き溜めを挟まない）</item>
    /// </list>
    /// **見つかる手掛かりとその並びは前と同じにする**：どの本体を使うか（取っておける数の上限を含む）は前の作りの決まりのまま数え、
    /// 手掛かりは出てきた順に並べてから渡す（読み直した物も元の位置に入る）。
    /// </summary>
    private static void ReadPackage(
        ZipArchiveEntry entry,
        List<string> authors,
        List<string> products,
        BoothClueCollector collector)
    {
        var pathByGuid = new Dictionary<string, string>(StringComparer.Ordinal);

        // 前の作りで「取っておいた」本体の guid（数の上限はこれで数える）。中身を持っているのは kept だけ
        var waiting = new HashSet<string>(StringComparer.Ordinal);
        var kept = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long keptBytes = 0;

        // 手掛かりを出てきた順に。読み直す物は場所だけ取っておき、2周目で埋める
        var found = new List<IReadOnlyList<BoothClue>?>();
        var readAgain = new Dictionary<string, (int Slot, string AssetPath)>(StringComparer.Ordinal);
        var pathCount = 0;

        try
        {
            using var entryStream = entry.Open();
            using var gzip = new GZipStream(entryStream, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);

            // **中身を写さずに流して読む**（copyData: false）。写す指定だと、4K テクスチャのような大きな本体まで
            // 1件ずつ丸ごとメモリに写してから次へ進み、1GB の unitypackage で約2GBを確保して作業セットが約0.9GB増えた（実測）。
            // 欲しいのは pathname と小さな文章だけで、どちらもその場で読み切るので、写す必要は無い
            while (tar.GetNextEntry(copyData: false) is { } tarEntry)
            {
                var parts = tarEntry.Name.TrimStart('.', '/').Split('/');
                if (parts.Length < 2)
                {
                    continue;
                }

                var guid = parts[0];
                var kind = parts[^1];

                if (kind == "pathname" && tarEntry.DataStream is not null)
                {
                    using var reader = new StreamReader(tarEntry.DataStream, Encoding.UTF8, false, 1024, leaveOpen: true);
                    var assetPath = reader.ReadLine()?.Trim();
                    if (string.IsNullOrEmpty(assetPath))
                    {
                        continue;
                    }

                    AddNamespaces(assetPath, authors, products);
                    pathByGuid[guid] = assetPath;

                    if (waiting.Remove(guid))
                    {
                        var hasBytes = kept.Remove(guid, out var pending);
                        keptBytes -= pending?.Length ?? 0;
                        if (IsTextAsset(assetPath))
                        {
                            if (hasBytes)
                            {
                                found.Add(CluesOf(assetPath, pending!));
                            }
                            else
                            {
                                readAgain[guid] = (found.Count, assetPath);
                                found.Add(null);
                            }
                        }
                    }

                    if (++pathCount > MaxAssetPaths)
                    {
                        break;
                    }
                }
                else if (kind == "asset" && tarEntry.DataStream is not null && tarEntry.Length <= MaxTextAssetBytes)
                {
                    if (pathByGuid.TryGetValue(guid, out var knownPath))
                    {
                        if (IsTextAsset(knownPath))
                        {
                            found.Add(CluesOf(knownPath, ReadUpTo(tarEntry.DataStream, (int)tarEntry.Length)));
                        }
                    }
                    else if (waiting.Count < MaxPendingAssets)
                    {
                        // pathname がまだ来ていないので保留する（tarの出現順は保証されない）。同じ guid なら後の物に差し替える
                        waiting.Add(guid);
                        if (kept.Remove(guid, out var replaced))
                        {
                            keptBytes -= replaced.Length;
                        }

                        if (TryKeep(tarEntry.DataStream, (int)tarEntry.Length, keptBytes) is { } bytes)
                        {
                            kept[guid] = bytes;
                            keptBytes += bytes.Length;
                        }
                    }
                }
            }
        }
        // TarReader は壊れた頭を InvalidDataException だけでなく、OverflowException（base-256 の大きさが桁あふれ）・
        // InvalidOperationException（GNU の長い名前の大きさが長すぎる）でも知らせる（作り物の tar で確かめた）。
        // 数の欄の読み方次第で FormatException・ArgumentOutOfRangeException も出うる。
        // 受けないと、壊れた unitypackage 1つで取り込みの解決が止まっていた（点検 2026-09-23）
        catch (Exception exception) when (IsBrokenPackage(exception))
        {
            // 壊れた unitypackage は、そこまでに読めた手掛かりだけで扱い、取り込み全体は止めない
        }

        if (readAgain.Count > 0)
        {
            ReadAgain(entry, readAgain, found);
        }

        foreach (var clues in found)
        {
            if (clues is not null)
            {
                collector.AddRange(clues);
            }
        }
    }

    private static bool IsBrokenPackage(Exception exception)
        => exception is InvalidDataException or IOException or FormatException
            or ArgumentException or ArithmeticException or InvalidOperationException;

    /// <summary>
    /// 1周目で取っておかなかった文章の本体を、2周目で読み直して元の位置に入れる。
    /// 使うのは1周目と同じ本体（その guid の pathname より前に来た最後の本体）。
    /// </summary>
    private static void ReadAgain(
        ZipArchiveEntry entry,
        Dictionary<string, (int Slot, string AssetPath)> wanted,
        List<IReadOnlyList<BoothClue>?> found)
    {
        var bodies = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var left = wanted.Count;

        try
        {
            using var entryStream = entry.Open();
            using var gzip = new GZipStream(entryStream, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);

            while (left > 0 && tar.GetNextEntry(copyData: false) is { } tarEntry)
            {
                var parts = tarEntry.Name.TrimStart('.', '/').Split('/');
                if (parts.Length < 2 || !wanted.TryGetValue(parts[0], out var place))
                {
                    continue;
                }

                var guid = parts[0];
                if (parts[^1] == "asset" && tarEntry.DataStream is not null && tarEntry.Length <= MaxTextAssetBytes)
                {
                    bodies[guid] = ReadUpTo(tarEntry.DataStream, (int)tarEntry.Length);
                }
                else if (parts[^1] == "pathname")
                {
                    if (bodies.Remove(guid, out var body))
                    {
                        found[place.Slot] = CluesOf(place.AssetPath, body);
                    }

                    wanted.Remove(guid);
                    left--;
                }
            }
        }
        catch (Exception exception) when (IsBrokenPackage(exception))
        {
            // 1周目で読めた所までは2周目でも読める。ここで壊れていたら読み直せた分だけ使う
        }
    }

    /// <summary>
    /// pathname を待つ本体を取っておくか決めて読む。小さい物はそのまま、大きい物は頭が文字に見えるときだけ。
    /// 合計の上限を超えるなら取っておかない（要ると分かったら読み直す）。取っておかないときは null（残りは TarReader が読み飛ばす）。
    /// </summary>
    private static byte[]? TryKeep(Stream stream, int length, long keptBytes)
    {
        if (keptBytes + length > PendingBudgetBytes)
        {
            return null;
        }

        if (length <= KeepWithoutLookingBytes)
        {
            return ReadUpTo(stream, length);
        }

        // 頭は小さな入れ物に読んで見る。本体の大きさの配列は、取っておくと決めてから作る
        var probe = new byte[TextProbeBytes];
        var head = ReadInto(stream, probe, 0, TextProbeBytes);
        if (!LooksLikeText(probe.AsSpan(0, head)))
        {
            return null;
        }

        var buffer = new byte[length];
        probe.AsSpan(0, head).CopyTo(buffer);
        var total = head + ReadInto(stream, buffer, head, length - head);
        return total == length ? buffer : buffer[..total];
    }

    /// <summary>
    /// 文章に見えるか。UTF-16 の印があるか、頭に 0 のバイトが無ければ文章とみなす（画像・モデル・バイナリの .asset は頭に 0 を含む）。
    /// 外れても結果は変わらない（取っておかなかった文章は読み直す）。外れて損をするのは読み直す手間だけ。
    /// </summary>
    private static bool LooksLikeText(ReadOnlySpan<byte> head)
        => head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || head.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) || !head.Contains((byte)0);

    private static void AddNamespaces(string assetPath, List<string> authors, List<string> products)
    {
        var segments = assetPath.Split('/');
        if (segments.Length < 2 || !string.Equals(segments[0], "Assets", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        authors.Add(segments[1]);
        if (segments.Length >= 3)
        {
            products.Add(segments[2]);
        }
    }

    private static IReadOnlyList<BoothClue> CluesOf(string assetPath, byte[] bytes)
        => [.. BoothUrlExtractor.ExtractFromText(TextDecoder.Decode(bytes), assetPath)];

    private static bool IsTextAsset(string assetPath)
    {
        if (TextExtensions.Contains(Path.GetExtension(assetPath)))
        {
            return true;
        }

        var name = Path.GetFileName(assetPath);
        return name.Contains("readme", StringComparison.OrdinalIgnoreCase)
            || name.Contains("read me", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 本体を大きさの分だけの配列へ直に読む（前は書き溜め（MemoryStream）に写してから配列へ写し直していた）。
    /// 途中で尽きた（壊れた tar）ら、読めた所までを返す（前の作りと同じく、読めた分で手掛かりを探す）。
    /// </summary>
    private static byte[] ReadUpTo(Stream stream, int length)
    {
        var buffer = new byte[length];
        var read = ReadInto(stream, buffer, 0, length);
        return read == length ? buffer : buffer[..read];
    }

    private static int ReadInto(Stream stream, byte[] buffer, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = stream.Read(buffer, offset + total, count - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
