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
}
