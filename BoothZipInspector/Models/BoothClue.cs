namespace BoothZipInspector.Models;

public enum BoothClueKind
{
    ItemUrl,
    ShopUrl,
    OtherBoothUrl,
}

public sealed class BoothClue
{
    public required BoothClueKind Kind { get; init; }
    public required string Url { get; init; }
    public string? ItemId { get; init; }
    public required string SourcePath { get; init; }
}
