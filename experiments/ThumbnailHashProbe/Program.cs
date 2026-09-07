// ThumbnailHashProbe
// ZIP内の画像（main.png / BOOTH_thumbnail_*.png など）を dHash(64bit) 化し、
// BOOTH 商品JSON (https://booth.pm/ja/items/{id}.json) の商品画像とハミング距離で照合する実験ツール。
//
// 使い方:
//   dotnet run --project experiments/ThumbnailHashProbe -- <zip> <entryNameOrSuffix> <itemId|-> [<zip> <entry> <itemId> ...]
//     itemId に "-" を渡すとローカル画像のハッシュだけを表示する。
//   dotnet run --project experiments/ThumbnailHashProbe -- --scan <zip>
//     ZIP内のサムネイル候補（main/thumb/booth/sample/preview/cover を含む画像）を列挙する。
//
// 実測（本リポジトリの調査時）:
//   一致する商品 → 距離 0〜2、無関係な商品 → 距離 21〜37。閾値 10 で明確に分離できた。
//
// 依存: SixLabors.ImageSharp 3.1.x (Apache-2.0)。4.x はライセンス条件が変わるため固定している。

using System.IO.Compression;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Console.OutputEncoding = Encoding.UTF8;

if (args.Length >= 2 && args[0] == "--scan")
{
    using var zip = ZipFile.Open(args[1], ZipArchiveMode.Read, Encoding.GetEncoding(932));
    foreach (var e in zip.Entries.Where(IsThumbnailCandidate))
        Console.WriteLine($"{e.FullName}  ({e.Length:N0} bytes)");
    return 0;
}

if (args.Length < 3 || args.Length % 3 != 0)
{
    Console.Error.WriteLine("usage: ThumbnailHashProbe <zip> <entry> <itemId|-> [...]   |   --scan <zip>");
    return 1;
}

using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) BoothZipInspector-experiment");

for (var i = 0; i < args.Length; i += 3)
{
    var zipPath = args[i];
    var entryName = args[i + 1];
    var itemId = args[i + 2];
    Console.WriteLine($"################ {Path.GetFileName(zipPath)} :: {entryName} vs item {itemId}");

    ulong localHash;
    using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Read, Encoding.GetEncoding(932)))
    {
        var entry = zip.GetEntry(entryName)
                    ?? zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(entryName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            Console.WriteLine("  entry not found");
            continue;
        }
        using var ms = new MemoryStream();
        using (var es = entry.Open()) es.CopyTo(ms);
        ms.Position = 0;
        localHash = DHash(ms);
    }
    Console.WriteLine($"  local dHash = {localHash:X16}");
    if (itemId == "-") continue;

    var json = await http.GetStringAsync($"https://booth.pm/ja/items/{itemId}.json");
    using var doc = JsonDocument.Parse(json);
    var urls = new List<string>();
    foreach (var image in doc.RootElement.GetProperty("images").EnumerateArray())
        if (image.TryGetProperty("resized", out var resized) && resized.ValueKind == JsonValueKind.String)
            urls.Add(resized.GetString()!);
    urls = urls.Distinct().Take(12).ToList();
    Console.WriteLine($"  item images: {urls.Count}");

    var best = int.MaxValue;
    foreach (var url in urls)
    {
        try
        {
            await using var stream = await http.GetStreamAsync(url);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            var h = DHash(ms);
            var d = BitOperations.PopCount(localHash ^ h);
            best = Math.Min(best, d);
            Console.WriteLine($"    dist={d,2}  {h:X16}  {url[(url.LastIndexOf('/') + 1)..]}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ERR {ex.Message}");
        }
    }
    var verdict = best <= 10 ? "MATCH" : best <= 16 ? "weak" : "no match";
    Console.WriteLine($"  BEST distance = {best}  ({verdict})");
}
return 0;

// 9x8 グレースケールに縮小し、隣接ピクセルの明暗差で 64bit を作る（dHash）
static ulong DHash(Stream s)
{
    using var img = Image.Load<L8>(s);
    img.Mutate(x => x.Resize(9, 8));
    ulong hash = 0;
    var bit = 0;
    for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
        {
            if (img[x, y].PackedValue < img[x + 1, y].PackedValue) hash |= 1UL << bit;
            bit++;
        }
    return hash;
}

static bool IsThumbnailCandidate(ZipArchiveEntry e)
{
    if (string.IsNullOrEmpty(e.Name)) return false;
    var ext = Path.GetExtension(e.Name).ToLowerInvariant();
    if (ext is not (".png" or ".jpg" or ".jpeg")) return false;
    var n = e.Name.ToLowerInvariant();
    return n.Contains("main") || n.Contains("thumb") || n.Contains("booth") || n.Contains("sample")
        || n.Contains("preview") || n.Contains("cover") || e.FullName.Count(c => c == '/') <= 1;
}
