namespace BoothZipInspector.Models;

public sealed class ZipEntryInfo
{
    public required string RelativePath { get; init; }
    public required long UncompressedSize { get; init; }
    public required long CompressedSize { get; init; }
}
