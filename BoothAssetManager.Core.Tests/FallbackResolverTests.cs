using BoothAssetManager.Core.Resolution;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class FallbackResolverTests
{
    /// <summary>
    /// 検索ページには推薦枠やヘッダのリンクも並ぶ。商品カードの data-product-id を使わずに
    /// /items/{id} を拾うと、どの検索語でも同じ商品が返ってしまう（実測で確認した不具合）。
    /// </summary>
    [Fact]
    public void PrefersProductCardIdsOverPlainItemLinks()
    {
        const string html = """
            <a href="/ja/items/9999999">おすすめ枠</a>
            <li class="item-card" data-product-id="6571299"><a href="/ja/items/6571299">結果1</a></li>
            <li class="item-card" data-product-id="6537462"><a href="/ja/items/6537462">結果2</a></li>
            """;

        Assert.Equal(["6571299", "6537462"], FallbackResolver.ExtractSearchResultIds(html));
    }

    [Fact]
    public void FallsBackToItemLinksWhenNoProductCards()
    {
        const string html = """<a href="/ja/items/5813187">商品</a><a href="/ja/items/5813187">同じ商品</a>""";

        Assert.Equal(["5813187"], FallbackResolver.ExtractSearchResultIds(html));
    }

    [Fact]
    public void ReadsNameAndShopFromProductCards()
    {
        const string html = """
            <li class="item-card l-card" data-product-id="111" data-product-name="練習用ポーズ集12" data-product-brand="posepose">
            <li class="item-card l-card" data-product-id="222" data-product-name="練習用ポーズ集13 &amp; おまけ" data-product-brand="posepose">
            """;

        var cards = FallbackResolver.ExtractSearchCards(html);

        Assert.Equal(["111", "222"], cards.Select(card => card.ItemId));
        Assert.Equal("練習用ポーズ集13 & おまけ", cards[1].Name);
        Assert.Equal("posepose", cards[1].ShopSubdomain);
    }

    /// <summary>商品名の版番号の中の数字は、シリーズの番号として数えない（2026-09-29）。</summary>
    [Fact]
    public void RerankIgnoresNumbersInsideVersionStrings()
    {
        var cards = new[]
        {
            new FallbackResolver.SearchCard("111", "サンプルモデル ver2.1.0", "other"),
            new FallbackResolver.SearchCard("222", "撫でるサンプルギミック", "maker"),
        };

        Assert.Equal("222", FallbackResolver.Rerank(cards, @"C:\dl\撫でるサンプルギミック1_01.zip")[0].ItemId);
    }

    /// <summary>属性の名前は途中で切られているので、カードの見出しの名前を使う（2026-09-29）。</summary>
    [Fact]
    public void ReadsTheUntruncatedNameFromTheCardTitle()
    {
        const string html = """
            <li class="item-card l-card" data-product-id="111" data-product-name="Sample Avatar - Long Na..." data-product-brand="maker">
              <div class="item-card__title"><a class="item-card__title-anchor--multiline nav" href="https://booth.pm/ja/items/111">Sample Avatar - Long Name &amp; Add-on</a></div>
            </li>
            <li class="item-card l-card" data-product-id="222" data-product-name="見出しの無いカード" data-product-brand="maker">
            </li>
            """;

        var cards = FallbackResolver.ExtractSearchCards(html);

        Assert.Equal("Sample Avatar - Long Name & Add-on", cards[0].Name);
        Assert.Equal("見出しの無いカード", cards[1].Name);
    }

    /// <summary>連番のシリーズ物は、番号の合うものを先に。BOOTHの並びでは十数位に沈んでいた。</summary>
    [Fact]
    public void RerankPutsTheMatchingSeriesNumberFirst()
    {
        var cards = new[]
        {
            new FallbackResolver.SearchCard("111", "練習用ポーズ集12", "posepose"),
            new FallbackResolver.SearchCard("222", "練習用ポーズ集13", "posepose"),
        };

        Assert.Equal("222", FallbackResolver.Rerank(cards, @"C:\dl\練習用ポーズ集13.zip")[0].ItemId);
    }

    /// <summary>ファイル名に付いたショップ名も手掛かりにする。点が同じならBOOTHの並びを保つ。</summary>
    [Fact]
    public void RerankUsesTheShopInTheFileNameAndKeepsBoothOrderOnTies()
    {
        var cards = new[]
        {
            new FallbackResolver.SearchCard("111", "Hair A", "someone"),
            new FallbackResolver.SearchCard("222", "Hair B", "sampleflow"),
            new FallbackResolver.SearchCard("333", "Hair C", "other"),
        };

        Assert.Equal(["222", "111", "333"], FallbackResolver.Rerank(cards, @"C:\dl\sampleflow_hair.zip").Select(card => card.ItemId));
    }

    /// <summary>引き直しは、まず特徴のある1語。検索語と同じなら引き直さない。</summary>
    [Fact]
    public void RetriesWithTheMostDistinctiveTokenFirst()
    {
        Assert.Equal(["Caramel"], FallbackResolver.RetryQueries(@"C:\dl\Braid_Caramel.zip", "Braid Caramel", null));
        Assert.Empty(FallbackResolver.RetryQueries(@"C:\dl\Kipfel_1.2.0.zip", "Kipfel", null));
    }

    [Fact]
    public void ReturnsNothingForHtmlWithoutItems()
    {
        Assert.Empty(FallbackResolver.ExtractSearchResultIds("<html><body>該当なし</body></html>"));
    }

    private static UnityPackageHints Hints(string? author = null, string? product = null) => new()
    {
        AuthorNamespaces = author is null ? [] : [author],
        ProductNamespaces = product is null ? [] : [product],
    };

    /// <summary>作者名前空間とショップが一致すれば裏付けありとみなす。</summary>
    [Fact]
    public void ScoresAuthorNamespaceMatchingShop()
    {
        var candidate = FallbackResolver.Score(
            "7841391", "【オリジナル3Dモデル】Wendy -ウェンディ-", "かえりみち", "kaerimichi",
            "Wendy", Hints(author: "Kaerimichi", product: "Wendy"), rank: 1);

        Assert.True(candidate.IsStrong);
        Assert.Contains("unitypackageの作者名前空間がショップ名と一致", candidate.Reasons);
    }

    /// <summary>記号や大文字小文字の違いは無視して照合する。</summary>
    [Fact]
    public void MatchesShopNameIgnoringSymbolsAndCase()
    {
        var candidate = FallbackResolver.Score(
            "1", "商品", "SHOP HEILON", "shopheilon",
            "商品", Hints(author: "SHOP_HEILON"), rank: 5);

        Assert.Contains("unitypackageの作者名前空間がショップ名と一致", candidate.Reasons);
    }

    /// <summary>番号違いの同シリーズを区別できること（FREYSIA No.101 と No.112）。</summary>
    [Fact]
    public void RanksMatchingNumberHigherWithinTheSameShop()
    {
        var hints = Hints(author: "FREYSIA", product: "FREYSIA");

        var matching = FallbackResolver.Score(
            "6142784", "FREYSIA💍No.101", "FREYSIA", "freysia", "FREYSIA", hints, rank: 1,
            significantNumbers: ["101"]);
        var other = FallbackResolver.Score(
            "6777694", "FREYSIA💍No.112", "FREYSIA", "freysia", "FREYSIA", hints, rank: 2,
            significantNumbers: ["101"]);

        Assert.True(matching.Score > other.Score);
        Assert.Contains("ファイル名の番号が商品名と一致", matching.Reasons);
    }

    /// <summary>同梱テキストに商品URLが直接あった場合は、それだけで裏付けとして扱う。</summary>
    [Fact]
    public void TreatsDirectUrlAsStrongEvidence()
    {
        var candidate = FallbackResolver.Score(
            "5316535", "なめらか心音ギミック", "beko", "bekoshop",
            "HeartBeatGimmick", Hints(), rank: 3, fromDirectUrl: true);

        Assert.True(candidate.IsStrong);
        Assert.Contains("同梱テキストに商品URLが直接書かれていた", candidate.Reasons);
    }

    /// <summary>無関係な商品には点が付かないこと。</summary>
    [Fact]
    public void GivesNoPointsToUnrelatedCandidate()
    {
        var candidate = FallbackResolver.Score(
            "5408028", "【VRChat】Dynamic Gesture", "Triturbo", "triturbo",
            "Kipfel", Hints(author: "MOCHIYAMA"), rank: 2);

        Assert.Equal(0, candidate.Score);
        Assert.False(candidate.IsStrong);
    }

    /// <summary>
    /// 読みの一致は、字面で商品名と一致しなかったときの代わり。字面でも一致した商品に重ねない（2026-09-29）。
    /// 重ねると、ファイル名の語を字面と別表記の両方で名前に持つ別の商品が、字面だけ一致する正解の上に来た。
    /// </summary>
    [Fact]
    public void ReadingMatchDoesNotStackOnNameMatch()
    {
        var both = FallbackResolver.Score(
            "111", "Hoshizora 星空ドーム", null, "shopa", "Hoshizora", new UnityPackageHints(), rank: 1, readingMatch: "星空");
        var readingOnly = FallbackResolver.Score(
            "222", "星空ドーム", null, "shopb", "Hoshizora", new UnityPackageHints(), rank: 1, readingMatch: "星空");
        var nameOnly = FallbackResolver.Score(
            "333", "Hoshizora Dome", null, "shopc", "Hoshizora", new UnityPackageHints(), rank: 1);

        Assert.Equal(2, both.Score);
        Assert.Equal(2, readingOnly.Score);
        Assert.Equal(nameOnly.Score, both.Score);
        Assert.Contains(readingOnly.Reasons, reason => reason.StartsWith("ファイル名が商品名と読みで一致", StringComparison.Ordinal));
    }
}
