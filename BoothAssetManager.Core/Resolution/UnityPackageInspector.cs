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

    static UnityPackageInspector()
    {
        // ZIPエントリ名のCP932読み取りに必要。登録し忘れると Encoding.GetEncoding(932) が
        // NotSupportedException を投げ、手掛かりが取れないまま静かに空を返すことになる。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static UnityPackageHints Inspect(string zipPath)
    {
        var authors = new List<string>();
        var products = new List<string>();
        var collector = new BoothClueCollector();

        try
        {
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, Encoding.GetEncoding(932));

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

    private static void ReadPackage(
        ZipArchiveEntry entry,
        List<string> authors,
        List<string> products,
        BoothClueCollector collector)
    {
        var pathByGuid = new Dictionary<string, string>(StringComparer.Ordinal);
        var pendingAssets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var pathCount = 0;

        try
        {
            using var entryStream = entry.Open();
            using var gzip = new GZipStream(entryStream, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);

            while (tar.GetNextEntry(copyData: true) is { } tarEntry)
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

                    if (pendingAssets.Remove(guid, out var pending) && IsTextAsset(assetPath))
                    {
                        CollectClues(assetPath, pending, collector);
                    }

                    if (++pathCount > MaxAssetPaths)
                    {
                        return;
                    }
                }
                else if (kind == "asset" && tarEntry.DataStream is not null && tarEntry.Length <= MaxTextAssetBytes)
                {
                    if (pathByGuid.TryGetValue(guid, out var knownPath))
                    {
                        if (IsTextAsset(knownPath))
                        {
                            CollectClues(knownPath, ReadAll(tarEntry.DataStream), collector);
                        }
                    }
                    else if (pendingAssets.Count < 2000)
                    {
                        // pathname がまだ来ていないので保留する（tarの出現順は保証されない）
                        pendingAssets[guid] = ReadAll(tarEntry.DataStream);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or EndOfStreamException)
        {
            // 壊れた unitypackage は手掛かり無しとして扱い、取り込み全体は止めない
        }
    }

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

    private static void CollectClues(string assetPath, byte[] bytes, BoothClueCollector collector)
    {
        var text = TextDecoder.Decode(bytes);
        collector.AddRange(BoothUrlExtractor.ExtractFromText(text, assetPath));
    }

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

    private static byte[] ReadAll(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
