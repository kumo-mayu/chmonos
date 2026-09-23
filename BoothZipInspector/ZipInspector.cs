using System.IO.Compression;
using System.Text;
using BoothZipInspector.Models;

namespace BoothZipInspector;

public sealed class ZipInspectionResult
{
    public required ZipSummary Summary { get; init; }
    public required IReadOnlyList<BoothClue> Clues { get; init; }
}

/// <summary>
/// ZIPアーカイブをディスクへ展開せずに読み取り、一覧とBOOTH手掛かりを収集する。
/// </summary>
public static class ZipInspector
{
    private const long MaxTextEntrySize = 2 * 1024 * 1024; // 2 MiB

    private static readonly HashSet<string> TextCandidateExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".html", ".htm", ".url", ".xml", ".yaml", ".yml",
    };

    static ZipInspector()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static ZipInspectionResult Inspect(string zipPath)
    {
        // UTF-8フラグが立っていないエントリ名は、厳密な UTF-8 として読めればそれ（Mac の圧縮）、駄目なら Shift-JIS。
        // (BOOTH配布ZIPは日本語ファイル名がShift-JISで格納されていることが多い。ZipNameEncoding)
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, ZipNameEncoding.Instance);

        var files = new List<ZipEntryInfo>();
        var collector = new BoothClueCollector();
        long totalUncompressed = 0;
        long totalCompressed = 0;

        foreach (var entry in archive.Entries)
        {
            // ディレクトリエントリはNameが空でFullNameが"/"で終わる
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            files.Add(new ZipEntryInfo
            {
                RelativePath = entry.FullName,
                UncompressedSize = entry.Length,
                CompressedSize = entry.CompressedLength,
            });
            totalUncompressed += entry.Length;
            totalCompressed += entry.CompressedLength;

            if (IsTextCandidate(entry))
            {
                var text = TryReadEntryText(entry);
                if (text is not null)
                {
                    collector.AddRange(BoothUrlExtractor.ExtractFromText(text, entry.FullName));
                }
            }
        }

        var summary = new ZipSummary
        {
            EntryCount = files.Count,
            TotalUncompressedSize = totalUncompressed,
            TotalCompressedSize = totalCompressed,
            Files = files,
        };

        return new ZipInspectionResult
        {
            Summary = summary,
            Clues = collector.Clues,
        };
    }

    private static bool IsTextCandidate(ZipArchiveEntry entry)
    {
        var ext = Path.GetExtension(entry.Name);
        return TextCandidateExtensions.Contains(ext) && entry.Length <= MaxTextEntrySize;
    }

    private static string? TryReadEntryText(ZipArchiveEntry entry)
    {
        try
        {
            using var entryStream = entry.Open();
            using var memoryStream = new MemoryStream();
            entryStream.CopyTo(memoryStream);
            return TextDecoder.Decode(memoryStream.ToArray());
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
