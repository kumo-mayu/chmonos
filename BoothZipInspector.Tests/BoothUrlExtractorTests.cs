using BoothZipInspector;
using BoothZipInspector.Models;
using Xunit;

namespace BoothZipInspector.Tests;

public class BoothUrlExtractorTests
{
    [Theory]
    [InlineData("https://booth.pm/ja/items/1234567", "1234567")]
    [InlineData("https://booth.pm/en/items/7654321", "7654321")]
    public void ExtractsItemIdFromStandardDomain(string url, string expectedId)
    {
        var id = BoothUrlExtractor.TryExtractItemId(url);
        Assert.Equal(expectedId, id);
    }

    [Fact]
    public void ExtractsItemIdFromShopSubdomain()
    {
        var id = BoothUrlExtractor.TryExtractItemId("https://shop-name.booth.pm/items/9999999");
        Assert.Equal("9999999", id);
    }

    [Theory]
    [InlineData("https://example.com/items/1234567")]
    [InlineData("https://notbooth.pm/items/1234567")]
    [InlineData("https://booth.pm.evil.com/ja/items/1234567")]
    public void DoesNotTreatNonBoothUrlAsItem(string url)
    {
        var id = BoothUrlExtractor.TryExtractItemId(url);
        Assert.Null(id);
    }

    [Fact]
    public void ExtractsItemIdFromDistributionCdnUrl()
    {
        // Chrome/Edge が Zone.Identifier の HostUrl に書く配布CDN URL（署名付きクエリ付き）
        var url = "https://s6.booth.pm/c80ffe79-d9d7-4481-bc64-40d80bcd71e6/f/5813187/7905648/Kipfel_1.2.0.zip?Expires=1&Signature=x";
        Assert.Equal("5813187", BoothUrlExtractor.TryExtractItemId(url));

        var clue = BoothUrlExtractor.Classify(url, "Zone.Identifier");
        Assert.NotNull(clue);
        Assert.Equal(BoothClueKind.ItemUrl, clue!.Kind);
        Assert.Equal("5813187", clue.ItemId);
    }

    [Fact]
    public void DoesNotTreatOtherCdnPathsAsItem()
    {
        Assert.Null(BoothUrlExtractor.TryExtractItemId("https://s6.booth.pm/some/other/path/file.zip"));
        Assert.Null(BoothUrlExtractor.TryExtractItemId("https://booth.pximg.net/c/300x300/i/5813187/x.jpg"));
    }

    [Fact]
    public void ClassifiesShopBaseUrl()
    {
        var clue = BoothUrlExtractor.Classify("https://shop-name.booth.pm/", "shop.url");
        Assert.NotNull(clue);
        Assert.Equal(BoothClueKind.ShopUrl, clue!.Kind);
    }

    [Fact]
    public void ExtractsFromTextWithMultipleUrls()
    {
        var text = "参考: https://booth.pm/ja/items/1234567 とショップ https://shop-name.booth.pm/ です。";
        var clues = BoothUrlExtractor.ExtractFromText(text, "note.txt");

        Assert.Equal(2, clues.Count);
        Assert.Contains(clues, c => c.Kind == BoothClueKind.ItemUrl && c.ItemId == "1234567");
        Assert.Contains(clues, c => c.Kind == BoothClueKind.ShopUrl);
    }

    [Fact]
    public void CollectorDeduplicatesSameItemIdFromMultipleSources()
    {
        var collector = new BoothClueCollector();
        collector.Add(new BoothClue { Kind = BoothClueKind.ItemUrl, Url = "https://booth.pm/ja/items/1234567", ItemId = "1234567", SourcePath = "a.txt" });
        collector.Add(new BoothClue { Kind = BoothClueKind.ItemUrl, Url = "https://shop-name.booth.pm/items/1234567", ItemId = "1234567", SourcePath = "b.txt" });

        Assert.Single(collector.Clues);
        Assert.Equal("a.txt", collector.Clues[0].SourcePath);
    }

    [Fact]
    public void CollectorDeduplicatesSameUrl()
    {
        var collector = new BoothClueCollector();
        collector.Add(new BoothClue { Kind = BoothClueKind.ShopUrl, Url = "https://shop-name.booth.pm/", SourcePath = "a.txt" });
        collector.Add(new BoothClue { Kind = BoothClueKind.ShopUrl, Url = "https://shop-name.booth.pm/", SourcePath = "b.txt" });

        Assert.Single(collector.Clues);
        Assert.Equal("a.txt", collector.Clues[0].SourcePath);
    }

    // 日本語の文では URL の直後に空白を置かずに全角の括弧・句読点・語が続く（点検 2026-09-23）
    [Theory]
    [InlineData("（https://booth.pm/ja/items/1234567）")]
    [InlineData("配布元：https://booth.pm/ja/items/1234567。")]
    [InlineData("https://booth.pm/ja/items/1234567です")]
    [InlineData("購入は「https://booth.pm/ja/items/1234567」から")]
    [InlineData("【https://booth.pm/ja/items/1234567】、よろしく")]
    [InlineData("詳しくは https://shop-a.booth.pm/items/1234567、を見てください")]
    public void FindsItemIdFollowedByJapanesePunctuation(string text)
        => Assert.Equal("1234567", BoothUrlExtractor.FindItemIdIn(text));

    [Fact]
    public void DoesNotTakeLeadingDigitsOfLongerId()
        => Assert.Equal("12345678", BoothUrlExtractor.FindItemIdIn("https://booth.pm/ja/items/12345678です"));

    [Fact]
    public void ClassifyCutsSentenceAfterId()
    {
        var clues = BoothUrlExtractor.ExtractFromText("配布：https://booth.pm/ja/items/1234567です", "readme.txt");

        var clue = Assert.Single(clues);
        Assert.Equal("1234567", clue.ItemId);
        Assert.Equal("https://booth.pm/ja/items/1234567", clue.Url);
    }

    [Theory]
    [InlineData("ショップ（https://shop-a.booth.pm）へ", "shop-a")]
    [InlineData("https://shop-a.booth.pmです", "shop-a")]
    public void FindsShopFollowedByJapanese(string text, string expected)
        => Assert.Equal(expected, BoothUrlExtractor.FindShopSubdomainIn(text));

    [Fact]
    public void ShopDoesNotMatchLongerHost()
        => Assert.Null(BoothUrlExtractor.FindShopSubdomainIn("https://shop-a.booth.pm.evil.com/"));
}
