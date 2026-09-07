using BoothAssetManager.Core.Booth;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class BoothItemMapperTests
{
    /// <summary>実際の商品JSONから、使う項目だけを同じ形で抜き出したもの。</summary>
    private const string RealisticJson = """
        {
          "id": 9907001,
          "name": "真・アバターペンシステム",
          "description": "アバターに組み込むペンシステムです。",
          "is_adult": false,
          "is_end_of_sale": false,
          "is_sold_out": false,
          "published_at": "2025-11-17T20:00:10.000+09:00",
          "price": "¥ 2,500",
          "wish_lists_count": 50158,
          "url": "https://booth.pm/ja/items/9907001",
          "tags": [ { "name": "衣装", "url": "https://booth.pm/ja/items?tags%5B%5D=x" }, { "name": "VRChat" } ],
          "category": {
            "id": 215,
            "name": "3Dツール・システム",
            "parent": { "name": "3Dモデル", "url": "https://booth.pm/ja/browse/x" }
          },
          "shop": {
            "uuid": "11d082f4-13bc-482b-b48f-07e27703a8aa",
            "name": "Sample Gates",
            "subdomain": "samplerin",
            "thumbnail_url": "https://booth.pximg.net/c/48x48/users/1/icon.jpg",
            "url": "https://samplerin.booth.pm/"
          },
          "embeds": [],
          "images": [
            { "caption": null, "original": "https://booth.pximg.net/a/i/9907001/one_base_resized.jpg" },
            { "caption": "裏面", "original": "https://booth.pximg.net/a/i/9907001/two_base_resized.jpg" }
          ],
          "variations": [
            { "id": 12826082, "name": null, "price": 2500, "status": "addable_to_cart", "type": "digital" },
            { "id": 12826083, "name": "Quest版", "price": 1800, "status": "addable_to_cart", "type": "digital" }
          ]
        }
        """;

    private static readonly DateTimeOffset FetchedAt = new(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MapsBasicFields()
    {
        var block = BoothItemMapper.Map(RealisticJson, FetchedAt);

        Assert.Equal("真・アバターペンシステム", block.Name);
        Assert.Equal("アバターに組み込むペンシステムです。", block.Description);
        Assert.False(block.IsAdult);
        Assert.Equal(FetchedAt, block.FetchedAt);
        Assert.Equal(new DateTimeOffset(2025, 11, 17, 20, 0, 10, TimeSpan.FromHours(9)), block.PublishedAt);
    }

    /// <summary>トップの price は整形済み文字列なので、表示用としてそのまま持つ。</summary>
    [Fact]
    public void KeepsFormattedPriceAsText()
    {
        var block = BoothItemMapper.Map(RealisticJson, FetchedAt);

        Assert.Equal("¥ 2,500", block.PriceText);
    }

    /// <summary>件数の項目名は wish_lists_count（複数形）。単数形で読むと常に0になる。</summary>
    [Fact]
    public void ReadsWishListsCount()
    {
        var block = BoothItemMapper.Map(RealisticJson, FetchedAt);

        Assert.Equal(50158, block.WishListsCount);
    }

    [Fact]
    public void MapsTagsToNames()
    {
        var block = BoothItemMapper.Map(RealisticJson, FetchedAt);

        Assert.Equal(["衣装", "VRChat"], block.Tags);
    }

    [Fact]
    public void MapsCategoryWithParentName()
    {
        var block = BoothItemMapper.Map(RealisticJson, FetchedAt);

        Assert.NotNull(block.Category);
        Assert.Equal(215, block.Category.Id);
        Assert.Equal("3Dツール・システム", block.Category.Name);
        Assert.Equal("3Dモデル", block.Category.ParentName);
    }

    [Fact]
    public void MapsShop()
    {
        var block = BoothItemMapper.Map(RealisticJson, FetchedAt);

        Assert.NotNull(block.Shop);
        Assert.Equal("Sample Gates", block.Shop.Name);
        Assert.Equal("samplerin", block.Shop.Subdomain);
    }

    [Fact]
    public void MapsImagesInOrder()
    {
        var block = BoothItemMapper.Map(RealisticJson, FetchedAt);

        Assert.Equal(2, block.Images.Count);
        Assert.EndsWith("one_base_resized.jpg", block.Images[0].OriginalUrl);
        Assert.Equal("裏面", block.Images[1].Caption);
    }

    /// <summary>単一バリエーションの商品では name が null になる。落とさずそのまま保持する。</summary>
    [Fact]
    public void MapsVariationsIncludingNullName()
    {
        var block = BoothItemMapper.Map(RealisticJson, FetchedAt);

        Assert.Equal(2, block.Variations.Count);
        Assert.Equal(12826082, block.Variations[0].Id);
        Assert.Null(block.Variations[0].Name);
        Assert.Equal(2500, block.Variations[0].Price);
        Assert.Equal("Quest版", block.Variations[1].Name);
    }

    [Fact]
    public void ReadsItemId()
    {
        Assert.Equal("9907001", BoothItemMapper.ReadItemId(RealisticJson));
    }

    /// <summary>BOOTHが項目を減らしても落ちないこと。</summary>
    [Fact]
    public void SurvivesMissingFields()
    {
        var block = BoothItemMapper.Map("""{"id":1}""", FetchedAt);

        Assert.Null(block.Name);
        Assert.Null(block.Category);
        Assert.Null(block.Shop);
        Assert.Empty(block.Tags);
        Assert.Empty(block.Images);
        Assert.Empty(block.Variations);
        Assert.Equal(0, block.WishListsCount);
    }

    [Fact]
    public void KeepsSectionsPassedIn()
    {
        var sections = new[]
        {
            new Core.Models.H2Section { Heading = "利用規約", NormalizedHeading = "利用規約", Text = "再配布禁止" },
        };

        var block = BoothItemMapper.Map(RealisticJson, FetchedAt, sections);

        Assert.Single(block.H2Sections);
        Assert.Equal("利用規約", block.H2Sections[0].Heading);
    }
}
