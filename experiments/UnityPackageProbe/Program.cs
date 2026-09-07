// UnityPackageProbe
// ZIP内の .unitypackage を「ディスクへ展開せず」メモリ上で読み取り、
//   1. アセットパスの名前空間 (Assets/<作者>/<商品>/...) を集計する
//   2. 同梱テキスト（Readme.txt / .md / .url など）の本文から BOOTH URL・作者サイトURL を抽出する
// 実験ツール。
//
// 使い方:
//   dotnet run --project experiments/UnityPackageProbe -- "C:\path\to\a.zip" ["C:\path\to\b.zip" ...]
//   オプション: --all   すべてのアセットパスを表示
//
// .unitypackage は tar.gz。各アセットは <guid>/pathname に Unity 側のパス、<guid>/asset に本体を持つ。
// tar は逐次読みなので、pathname と asset の出現順に依存しないよう guid 単位で突き合わせる。
// .NET 8 標準の System.Formats.Tar を使うため追加パッケージは不要（ZIPエントリ名のShift-JIS対応にのみ CodePages を使用）。

using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Console.OutputEncoding = Encoding.UTF8;

var showAll = args.Contains("--all");
var zipPaths = args.Where(a => !a.StartsWith("--")).ToArray();
if (zipPaths.Length == 0)
{
    Console.Error.WriteLine("usage: UnityPackageProbe [--all] <zip> [<zip> ...]");
    return 1;
}

var urlRegex = new Regex(@"https?://[^\s""'<>\]\)]+", RegexOptions.IgnoreCase);
var boothItemRegex = new Regex(@"^https?://(?:[a-z0-9-]+\.)?booth\.pm(?:/[a-z]{2}(?:-[a-z]{2})?)?/items/(\d+)", RegexOptions.IgnoreCase);
var textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt", ".md", ".url", ".json", ".html", ".htm", ".xml", ".yaml", ".yml", ".asset" };
const long maxTextAsset = 2 * 1024 * 1024;

foreach (var zipPath in zipPaths)
{
    Console.WriteLine($"################ {Path.GetFileName(zipPath)}");
    using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Read, Encoding.GetEncoding(932));

    var packages = zip.Entries.Where(e => e.Name.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)).ToList();
    if (packages.Count == 0)
    {
        Console.WriteLine("  (unitypackage なし)");
        continue;
    }

    foreach (var entry in packages)
    {
        Console.WriteLine($"  [unitypackage] {entry.FullName}  ({entry.Length:N0} bytes)");
        var result = ReadPackage(entry, textExtensions, maxTextAsset);
        if (result is null) continue;

        Console.WriteLine($"    assets = {result.Paths.Count}");
        foreach (var g in result.Paths.Select(p => string.Join("/", p.Split('/').Take(3))).GroupBy(x => x).OrderByDescending(g => g.Count()).Take(6))
            Console.WriteLine($"    {g.Count(),6}  {g.Key}");

        var authors = result.Paths.Select(p => p.Split('/')).Where(s => s.Length >= 2 && s[0] == "Assets").Select(s => s[1]).Distinct().Take(5);
        Console.WriteLine($"    author-namespace candidates: {string.Join(" | ", authors)}");

        // テキストアセットからURLを抽出
        var found = new List<(string path, string url, string? itemId)>();
        foreach (var (path, text) in result.Texts)
            foreach (Match m in urlRegex.Matches(text))
            {
                var url = m.Value.TrimEnd('.', ',', ';', ')', ']', '"', '\'');
                var idMatch = boothItemRegex.Match(url);
                found.Add((path, url, idMatch.Success ? idMatch.Groups[1].Value : null));
            }
        var distinct = found.GroupBy(f => f.url).Select(g => g.First()).ToList();
        if (distinct.Count == 0)
            Console.WriteLine($"    text assets read: {result.Texts.Count}, URLs: (none)");
        else
        {
            Console.WriteLine($"    text assets read: {result.Texts.Count}, URLs: {distinct.Count}");
            foreach (var f in distinct.OrderByDescending(f => f.itemId is not null).Take(12))
                Console.WriteLine($"      {(f.itemId is null ? "      " : "item " + f.itemId),-13} {f.url}   <- {f.path}");
        }

        if (showAll)
            foreach (var p in result.Paths) Console.WriteLine($"      {p}");
    }
}
return 0;

static PackageResult? ReadPackage(ZipArchiveEntry entry, HashSet<string> textExtensions, long maxTextAsset)
{
    var paths = new List<string>();
    var pathByGuid = new Dictionary<string, string>();
    var pendingAssets = new Dictionary<string, byte[]>();   // asset が pathname より先に出た場合の保留
    var texts = new List<(string path, string text)>();

    try
    {
        using var entryStream = entry.Open();
        using var gzip = new GZipStream(entryStream, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);

        TarEntry? t;
        while ((t = tar.GetNextEntry(copyData: true)) is not null)
        {
            var parts = t.Name.TrimStart('.', '/').Split('/');
            if (parts.Length < 2) continue;
            var guid = parts[0];
            var kind = parts[^1];

            if (kind == "pathname" && t.DataStream is not null)
            {
                var reader = new StreamReader(t.DataStream, Encoding.UTF8, false, 1024, leaveOpen: true);
                var line = reader.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                paths.Add(line);
                pathByGuid[guid] = line;
                if (pendingAssets.Remove(guid, out var bytes) && IsText(line, textExtensions))
                    texts.Add((line, BoothZipInspectorLike.Decode(bytes)));
            }
            else if (kind == "asset" && t.DataStream is not null && t.Length <= maxTextAsset)
            {
                if (pathByGuid.TryGetValue(guid, out var known))
                {
                    if (IsText(known, textExtensions))
                        texts.Add((known, BoothZipInspectorLike.Decode(ReadAll(t.DataStream))));
                }
                else
                {
                    // まだ pathname を見ていない。小さいものだけ保留する
                    pendingAssets[guid] = ReadAll(t.DataStream);
                    if (pendingAssets.Count > 2000) pendingAssets.Remove(pendingAssets.Keys.First());
                }
            }
            if (paths.Count > 20000) break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"    ERROR {ex.GetType().Name}: {ex.Message}");
        return null;
    }
    return new PackageResult(paths, texts);
}

static bool IsText(string path, HashSet<string> textExtensions)
{
    var ext = Path.GetExtension(path);
    if (textExtensions.Contains(ext)) return true;
    var name = Path.GetFileName(path);
    return name.Contains("readme", StringComparison.OrdinalIgnoreCase) || name.Contains("read me", StringComparison.OrdinalIgnoreCase);
}

static byte[] ReadAll(Stream s)
{
    using var ms = new MemoryStream();
    s.CopyTo(ms);
    return ms.ToArray();
}

sealed record PackageResult(List<string> Paths, List<(string path, string text)> Texts);

// 本体の TextDecoder と同じ方針: BOM → UTF-8 厳密 → Shift-JIS
static class BoothZipInspectorLike
{
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(932).GetString(bytes); }
    }
}
