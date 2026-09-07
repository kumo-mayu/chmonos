namespace BoothZipInspector.Models;

public sealed class ZoneIdentifierInfo
{
    public bool Found { get; init; }
    public string? ZoneId { get; init; }
    public string? ReferrerUrl { get; init; }
    public string? HostUrl { get; init; }
    public string? BoothItemId { get; init; }

    public static ZoneIdentifierInfo NotFound() => new() { Found = false };
}
