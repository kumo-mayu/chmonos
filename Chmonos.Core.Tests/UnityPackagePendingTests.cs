using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Chmonos.Core.Resolution;
using BoothZipInspector;
using BoothZipInspector.Models;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// unitypackage の中で pathname より先に来た本体の扱い（2026-09-24）。前は 2MB までの本体を全部メモリに写して取っておいた。
/// 今は小さい物か頭が文字に見える物だけを取っておき、要ると分かった物は2周目で読み直す。
/// **手掛かり・作者と商品の名前空間は、前の作り（ここに写した物）と並びまで同じ**になることを確かめる。
/// </summary>
public sealed class UnityPackagePendingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-upp-" + Guid.NewGuid().ToString("N"));

    public UnityPackagePendingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static byte[] Text(string text, int padTo = 0)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return padTo <= bytes.Length ? bytes : [.. bytes, .. Enumerable.Repeat((byte)' ', padTo - bytes.Length)];
    }

    /// <summary>頭に 0 を含む本体（画像・バイナリの .asset）。途中に URL の文字を埋める。</summary>
    private static byte[] Binary(int size, string? inside = null)
    {
        var bytes = new byte[size];
        new Random(size).NextBytes(bytes);
        bytes[0] = 0;
        bytes[1] = 0;
        if (inside is not null)
        {
            Encoding.ASCII.GetBytes(inside).CopyTo(bytes, size / 2);
        }

        return bytes;
    }

    /// <summary>tar の項目を並べた順に書いた unitypackage を1つ入れた zip。</summary>
    private string Package(IEnumerable<(string Name, byte[] Data)> entries)
    {
        using var tarBytes = new MemoryStream();
        using (var gzip = new GZipStream(tarBytes, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Ustar, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(data) });
            }
        }

        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        using var entry = zip.CreateEntry("Pack.unitypackage", CompressionLevel.NoCompression).Open();
        entry.Write(tarBytes.ToArray());
        return path;
    }

    private static (string Name, byte[] Data) PathName(string guid, string assetPath) => ($"{guid}/pathname", Text(assetPath));

    private static (string Name, byte[] Data) Asset(string guid, byte[] data) => ($"{guid}/asset", data);

    private static void AssertSameAsBefore(string zip)
    {
        var now = UnityPackageInspector.Inspect(zip);
        var before = BeforeInspect(zip);

        Assert.Equal(before.Authors, now.AuthorNamespaces);
        Assert.Equal(before.Products, now.ProductNamespaces);
        Assert.Equal(
            before.Clues.Select(clue => (clue.Kind, clue.Url, clue.ItemId, clue.SourcePath)),
            now.Clues.Select(clue => (clue.Kind, clue.Url, clue.ItemId, clue.SourcePath)));
    }

    [Fact]
    public void FindsTheSameCluesWhenBodiesComeBeforeTheirNames()
    {
        var zip = Package(
        [
            Asset("g1", Text("購入 https://booth.pm/ja/items/1001")),                         // 小さい文章
            Asset("g2", Text("詳しくは https://booth.pm/ja/items/1002 へ", padTo: 100_000)),   // 大きい文章（頭が文字）
            Asset("g3", Text("\0https://booth.pm/ja/items/1003", padTo: 200_000)),       // 頭に 0 がある大きい .asset（取っておかず、読み直す）
            Asset("g4", Binary(300_000, "https://booth.pm/ja/items/1004")),                  // 大きいバイナリの画像（使わない）
            PathName("g4", "Assets/Shop/Item/Textures/a.png"),
            PathName("g3", "Assets/Shop/Item/Data/settings.asset"),
            PathName("g2", "Assets/Shop/Item/Readme_long.txt"),
            PathName("g1", "Assets/Shop/Item/Readme.txt"),
            PathName("g5", "Assets/Other/Thing/later.md"),
            Asset("g5", Text("https://booth.pm/ja/items/1005")),                              // 名前が先に来た本体
        ]);

        AssertSameAsBefore(zip);
        Assert.Equal(
            ["1003", "1002", "1001", "1005"],
            UnityPackageInspector.Inspect(zip).Clues.Select(clue => clue.ItemId));
    }

    /// <summary>取っておく合計の上限（32MB）を超えた文章も、読み直して同じ手掛かりになる。</summary>
    [Fact]
    public void RereadsTextBodiesBeyondTheBudget()
    {
        var entries = new List<(string, byte[])>();
        for (var index = 0; index < 20; index++)
        {
            entries.Add(Asset($"t{index}", Text($"https://booth.pm/ja/items/{2000 + index}", padTo: 1_900_000)));
        }

        for (var index = 19; index >= 0; index--)
        {
            entries.Add(PathName($"t{index}", $"Assets/Shop/Item/Doc{index}.txt"));
        }

        var zip = Package(entries);

        AssertSameAsBefore(zip);
        Assert.Equal(20, UnityPackageInspector.Inspect(zip).Clues.Count);
    }

    /// <summary>取っておける数の上限（2000件）を超えた本体を使わないのも前と同じ。</summary>
    [Fact]
    public void KeepsTheOldLimitOnHowManyBodiesWait()
    {
        var entries = new List<(string, byte[])>();
        for (var index = 0; index < 2001; index++)
        {
            entries.Add(Asset($"n{index}", Text($"https://booth.pm/ja/items/{3000 + index}")));
        }

        entries.Add(PathName("n2000", "Assets/Shop/Item/Over.txt"));
        entries.Add(PathName("n0", "Assets/Shop/Item/First.txt"));

        var zip = Package(entries);

        AssertSameAsBefore(zip);
        Assert.Equal(["3000"], UnityPackageInspector.Inspect(zip).Clues.Select(clue => clue.ItemId));
    }

    // ── 前の作り（2026-09-23 の UnityPackageInspector の読み方）をそのまま写した物 ──

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".url", ".json", ".html", ".htm", ".xml", ".yaml", ".yml", ".asset",
    };

    private static bool IsTextAsset(string assetPath)
        => TextExtensions.Contains(Path.GetExtension(assetPath))
            || Path.GetFileName(assetPath).Contains("readme", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(assetPath).Contains("read me", StringComparison.OrdinalIgnoreCase);

    private static (List<string> Authors, List<string> Products, IReadOnlyList<BoothClue> Clues) BeforeInspect(string zipPath)
    {
        var authors = new List<string>();
        var products = new List<string>();
        var collector = new BoothClueCollector();
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, ZipNameEncoding.Instance);
        foreach (var entry in archive.Entries.Where(entry => entry.Name.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)))
        {
            var pathByGuid = new Dictionary<string, string>(StringComparer.Ordinal);
            var pendingAssets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using var gzip = new GZipStream(entry.Open(), CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            while (tar.GetNextEntry(copyData: false) is { } tarEntry)
            {
                var parts = tarEntry.Name.TrimStart('.', '/').Split('/');
                var guid = parts[0];
                if (parts[^1] == "pathname")
                {
                    using var reader = new StreamReader(tarEntry.DataStream!, Encoding.UTF8, false, 1024, leaveOpen: true);
                    var assetPath = reader.ReadLine()!.Trim();
                    var segments = assetPath.Split('/');
                    if (segments.Length >= 2 && segments[0] == "Assets")
                    {
                        authors.Add(segments[1]);
                        if (segments.Length >= 3)
                        {
                            products.Add(segments[2]);
                        }
                    }

                    pathByGuid[guid] = assetPath;
                    if (pendingAssets.Remove(guid, out var pending) && IsTextAsset(assetPath))
                    {
                        collector.AddRange(BoothUrlExtractor.ExtractFromText(TextDecoder.Decode(pending), assetPath));
                    }
                }
                else if (parts[^1] == "asset" && tarEntry.Length <= 2 * 1024 * 1024)
                {
                    using var memory = new MemoryStream();
                    if (pathByGuid.TryGetValue(guid, out var knownPath))
                    {
                        if (IsTextAsset(knownPath))
                        {
                            tarEntry.DataStream!.CopyTo(memory);
                            collector.AddRange(BoothUrlExtractor.ExtractFromText(TextDecoder.Decode(memory.ToArray()), knownPath));
                        }
                    }
                    else if (pendingAssets.Count < 2000)
                    {
                        tarEntry.DataStream!.CopyTo(memory);
                        pendingAssets[guid] = memory.ToArray();
                    }
                }
            }
        }

        return (
            authors.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            products.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            collector.Clues);
    }
}
