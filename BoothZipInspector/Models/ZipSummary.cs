namespace BoothZipInspector.Models;

public sealed class ZipSummary
{
    public required int EntryCount { get; init; }
    public required long TotalUncompressedSize { get; init; }
    public required long TotalCompressedSize { get; init; }
    public required IReadOnlyList<ZipEntryInfo> Files { get; init; }
}
