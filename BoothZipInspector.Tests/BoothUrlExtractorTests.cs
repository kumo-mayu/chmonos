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

    // ---- ダウンロード元の記録に残す値（作り物の番号） ----

    [Fact]
    public void ReadsDownloadIdAndItemIdFromSignedCdnUrl()
    {
        var parsed = BoothUrlExtractor.TryParseDownloadUrl(
            "https://s9.booth.pm/00000000-1111-2222-3333-444444444444/f/1234567/7654321/作り物_v1.zip?Expires=1&Signature=abc&Key-Pair-Id=xyz");

        Assert.Equal(new BoothDownloadUrl(7654321, "1234567"), parsed);
    }

    [Theory]
    [InlineData("https://booth.pm/downloadables/7654321")]
    [InlineData("https://booth.pm/ja/downloadables/7654321?x=1")]
    public void ReadsDownloadIdFromDownloadablesUrl(string url)
        => Assert.Equal(new BoothDownloadUrl(7654321, null), BoothUrlExtractor.TryParseDownloadUrl(url));

    [Theory]
    [InlineData("https://booth.pm.evil.com/downloadables/7654321")]
    [InlineData("https://example.com/f/1234567/7654321/a.zip")]
    [InlineData("https://accounts.booth.pm/")]
    [InlineData("C:\\Users\\a\\Downloads\\作り物.zip")]
    [InlineData(null)]
    public void DoesNotReadDownloadFromOtherUrls(string? url)
        => Assert.Null(BoothUrlExtractor.TryParseDownloadUrl(url));

    [Theory]
    [InlineData("https://booth.pm/ja/items/1234567", "https://booth.pm/ja/items/1234567")]
    [InlineData("http://booth.pm/ja/items/1234567?utm=x#top", "https://booth.pm/ja/items/1234567")]
    [InlineData("https://Shop-A.booth.pm/items/1234567/edit", "https://shop-a.booth.pm/items/1234567")]
    [InlineData("https://booth.pm/zh-cn/items/1234567", "https://booth.pm/zh-cn/items/1234567")]
    public void NormalizesItemPageUrl(string url, string expected)
        => Assert.Equal(expected, BoothUrlExtractor.TryNormalizeItemPageUrl(url));

    [Theory]
    [InlineData("https://booth.pm.evil.com/ja/items/1234567")]
    [InlineData("https://accounts.booth.pm/items/1234567")]
    [InlineData("https://accounts.booth.pm/library")]
    [InlineData("https://s9.booth.pm/00000000-1111-2222-3333-444444444444/f/1234567/7654321/a.zip")]
    [InlineData("https://example.com/items/1234567")]
    public void DoesNotNormalizeOtherUrls(string url)
        => Assert.Null(BoothUrlExtractor.TryNormalizeItemPageUrl(url));

    // ---- まとめて拾うときは商品ページだけ（計画 26。作り物の番号） ----

    [Theory]
    [InlineData("https://booth.pm/ja/items/1234567", "1234567")]
    [InlineData("https://shop-a.booth.pm/items/1234567?x=1", "1234567")]
    [InlineData("https://booth.pximg.net/c/300x300/00000000-1111-2222-3333-444444444444/i/1234567/a.jpg", null)]
    [InlineData("https://s9.booth.pm/00000000-1111-2222-3333-444444444444/f/1234567/7654321/a.zip", null)]
    [InlineData("https://accounts.booth.pm/items/1234567", null)]
    [InlineData("https://shop-a.booth.pm/", null)]
    public void ItemPageIdCountsOnlyItemPages(string url, string? expected)
        => Assert.Equal(expected, BoothUrlExtractor.TryExtractItemPageId(url));

    [Fact]
    public void FindsAllItemPageIdsInOrderWithoutRepeats()
        => Assert.Equal(
            ["1234568", "1234567"],
            BoothUrlExtractor.FindItemPageIdsIn(
                "（https://booth.pm/ja/items/1234568）と https://shop-a.booth.pm/items/1234567、https://booth.pm/en/items/1234568 と絵 https://booth.pximg.net/00000000-1111-2222-3333-444444444444/i/1234569/a.jpg"));
}
