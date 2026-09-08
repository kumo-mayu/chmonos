using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class SearchQueryTests
{
    private static SearchHaystack Hay(string primary, string body = "", string paths = "")
        => new()
        {
            Primary = SearchQuery.Normalize(primary),
            Body = SearchQuery.Normalize(body),
            Paths = SearchQuery.Normalize(paths),
        };

    private static bool Match(string query, SearchHaystack hay, bool body = false, bool paths = false)
        => SearchQuery.Matches(SearchQuery.Parse(query), hay, body, paths);

    [Fact]
    public void EmptyQueryMatchesEverything()
        => Assert.True(Match("   ", Hay("なんでも")));

    [Fact]
    public void SpaceMeansAnd()
    {
        var hay = Hay("夏の衣装セット");

        Assert.True(Match("夏 衣装", hay));
        Assert.False(Match("夏 冬", hay));
    }

    [Fact]
    public void MinusExcludes()
    {
        Assert.False(Match("衣装 -無料", Hay("無料の衣装")));
        Assert.True(Match("衣装 -無料", Hay("有料の衣装")));
    }

    /// <summary>語の途中のハイフンは除外の印にしない。商品名に普通に出てくる。</summary>
    [Fact]
    public void HyphenInsideWordIsPartOfTheWord()
        => Assert.True(Match("kuuta-3d", Hay("Kuuta-3D モデル")));

    [Fact]
    public void QuotedTextIsOnePhrase()
    {
        Assert.True(Match("\"夏セット\"", Hay("夏セットの中身")));
        Assert.False(Match("\"夏セット\"", Hay("夏 セット")));
    }

    [Fact]
    public void OrTakesEitherSide()
    {
        Assert.True(Match("夏 OR 冬", Hay("冬の服")));
        Assert.False(Match("夏 OR 冬", Hay("春の服")));
    }

    /// <summary>括弧が無いと「衣装かつ夏」または「冬」。設計文書の説明どおりに読む。</summary>
    [Fact]
    public void AndBindsTighterThanOr()
    {
        var node = SearchQuery.Parse("衣装 夏 OR 冬");

        var or = Assert.IsType<SearchNode.Or>(node);
        Assert.Equal(2, or.Parts.Count);
        Assert.IsType<SearchNode.And>(or.Parts[0]);
        Assert.IsType<SearchNode.Term>(or.Parts[1]);
    }

    [Fact]
    public void ParenthesesChangeGrouping()
    {
        var hay = Hay("冬の衣装");

        Assert.True(Match("衣装 (夏 OR 冬)", hay));
        Assert.False(Match("水着 (夏 OR 冬)", hay));
    }

    /// <summary>日本語入力では全角のまま打たれるのが普通。NFKCで畳んでから読む。</summary>
    [Fact]
    public void FullWidthPunctuationWorksTheSame()
    {
        var hay = Hay("冬の衣装");

        Assert.True(Match("衣装　（夏　OR　冬）", hay));
        Assert.False(Match("衣装　－冬", hay));
    }

    [Fact]
    public void FullWidthAndHalfWidthLettersMatchEachOther()
        => Assert.True(Match("vrchat", Hay("ＶＲＣｈａｔ想定")));

    [Fact]
    public void MatchingIgnoresCase()
        => Assert.True(Match("KUUTA", Hay("kuuta")));

    /// <summary>「」は商品名にそのまま出てくるので、フレーズの印にしない。</summary>
    [Fact]
    public void JapaneseCornerBracketsAreLiteral()
        => Assert.True(Match("「タマクラゲ」", Hay("アクセサリー「タマクラゲ」")));

    [Fact]
    public void BodyIsSearchedOnlyWhenAsked()
    {
        var hay = Hay("指輪", body: "この商品は夏向けです");

        Assert.False(Match("夏", hay));
        Assert.True(Match("夏", hay, body: true));
    }

    [Fact]
    public void PathsAreSearchedOnlyWhenAsked()
    {
        var hay = Hay("指輪", paths: @"D:\storage\VRChat_model\Sig_Ring_07.zip");

        Assert.False(Match("sig_ring", hay));
        Assert.True(Match("sig_ring", hay, paths: true));
    }

    /// <summary>打ちかけの入力でも結果が出続ける。閉じ忘れで空になると入力中に画面が消える。</summary>
    [Fact]
    public void UnclosedQuoteStillSearches()
        => Assert.True(Match("\"夏セット", Hay("夏セットの中身")));

    [Fact]
    public void UnclosedParenthesisStillSearches()
        => Assert.True(Match("衣装 (夏 OR 冬", Hay("冬の衣装")));

    [Fact]
    public void DanglingOrIsIgnored()
        => Assert.True(Match("衣装 OR", Hay("冬の衣装")));

    [Fact]
    public void LoneMinusIsIgnored()
        => Assert.True(Match("衣装 -", Hay("冬の衣装")));

    [Fact]
    public void NegationCanApplyToAGroup()
    {
        Assert.False(Match("衣装 -(夏 OR 冬)", Hay("冬の衣装")));
        Assert.True(Match("衣装 -(夏 OR 冬)", Hay("春の衣装")));
    }
}
