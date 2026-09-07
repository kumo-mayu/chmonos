namespace BoothZipInspector.Models;

public sealed class FileBasicInfo
{
    public required string FullPath { get; init; }
    public required string FileName { get; init; }
    public required string Extension { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required DateTime ModifiedAtUtc { get; init; }
    public required string Sha256Hex { get; init; }
}
