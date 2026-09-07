using BoothIdResolver;
using BoothZipInspector.Models;
using Xunit;

namespace BoothIdResolver.Tests;

public class IdResolverTests
{
    private static BoothClue ItemClue(string itemId, string source = "test.url") => new()
    {
        Kind = BoothClueKind.ItemUrl,
        Url = $"https://booth.pm/ja/items/{itemId}",
        ItemId = itemId,
        SourcePath = source,
    };

    [Fact]
    public void PrefersZoneIdentifierOverZipClues()
    {
        var zone = new ZoneIdentifierInfo { Found = true, BoothItemId = "1111111" };
        var clues = new[] { ItemClue("2222222") };

        var result = IdResolver.Resolve(zone, clues);

        Assert.Single(result);
        Assert.Equal("1111111", result[0].ItemId);
        Assert.Equal("Zone.Identifier", result[0].Source);
    }

    [Fact]
    public void FallsBackToZipCluesWhenZoneNotFound()
    {
        var zone = ZoneIdentifierInfo.NotFound();
        var clues = new[] { ItemClue("5813187") };

        var result = IdResolver.Resolve(zone, clues);

        Assert.Single(result);
        Assert.Equal("5813187", result[0].ItemId);
        Assert.Equal("ZIP内テキスト", result[0].Source);
    }

    [Fact]
    public void ExcludesKnownDependencyLikeLilToon()
    {
        var zone = ZoneIdentifierInfo.NotFound();
        var clues = new[] { ItemClue("3087170", "lilToon - lilLab - BOOTH.url") };

        var result = IdResolver.Resolve(zone, clues);

        Assert.Empty(result);
    }

    [Fact]
    public void ReturnsBothCandidatesWhenAmbiguousAndDoesNotAutoSelect()
    {
        var zone = ZoneIdentifierInfo.NotFound();
        var clues = new[] { ItemClue("6571299", "a.url"), ItemClue("7841391", "b.url") };

        var result = IdResolver.Resolve(zone, clues);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.ItemId == "6571299");
        Assert.Contains(result, r => r.ItemId == "7841391");
    }

    [Fact]
    public void ReturnsEmptyWhenNothingFound()
    {
        var zone = ZoneIdentifierInfo.NotFound();
        var result = IdResolver.Resolve(zone, Array.Empty<BoothClue>());

        Assert.Empty(result);
    }

    [Fact]
    public void DeduplicatesRepeatedItemIdFromZipClues()
    {
        var zone = ZoneIdentifierInfo.NotFound();
        var clues = new[] { ItemClue("5813187", "a.url"), ItemClue("5813187", "b.url") };

        var result = IdResolver.Resolve(zone, clues);

        Assert.Single(result);
    }

    [Theory]
    [InlineData("5813187", "https://booth.pm/ja/items/5813187")]
    [InlineData("1", "https://booth.pm/ja/items/1")]
    public void BuildsItemUrl(string id, string expected)
    {
        Assert.Equal(expected, IdResolver.ToItemUrl(id));
    }
}
