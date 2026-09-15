using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class SearchQueryTests
{
    private static SearchHaystack Hay(string name, string body = "", string paths = "", string tags = "", string reading = "")
        => SearchHaystack.FromValues(
            new Dictionary<SearchField, string[]>
            {
                [SearchField.Name] = [name],
                [SearchField.Main] = [body],
                [SearchField.Path] = [paths],
                [SearchField.Tag] = [tags],
            },
            reading);

    private static bool Match(string query, SearchHaystack hay, bool body = false, bool paths = false)
    {
        var targets = new HashSet<SearchField>(SearchOptions.DefaultTargets);
        if (body)
        {
            targets.Add(SearchField.Main);
        }

        if (paths)
        {
            targets.Add(SearchField.Path);
        }

        return SearchQuery.Matches(SearchQuery.Parse(query), hay, new SearchOptions { Targets = targets });
    }

    private static bool MatchWith(string query, SearchHaystack hay, SearchOptions options)
        => SearchQuery.Matches(SearchQuery.Parse(query), hay, options);

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

    /// <summary>日本語入力では全角のまま打たれるのが普通。記号は1字ずつ NFKC で畳んでから読む。</summary>
    [Fact]
    public void FullWidthPunctuationWorksTheSame()
    {
        var hay = Hay("冬の衣装");

        Assert.True(Match("衣装　（夏　OR　冬）", hay));
        Assert.False(Match("衣装　－冬", hay));
        Assert.True(Match("衣装 ＯＲ 水着", hay));
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

    // ---- 対象と前置き（2026-09-16） ----

    /// <summary>BOOTHタグは絞り込みの条件で探す物なので、既定の対象に入れない（ユーザ判断 2026-09-16）。</summary>
    [Fact]
    public void TagsAreNotSearchedByDefault()
    {
        var hay = Hay("指輪", tags: "アクセサリー");

        Assert.False(Match("アクセサリー", hay));
        Assert.True(Match("tag:アクセサリー", hay));
    }

    /// <summary>前置きがあれば、対象の切り替えで切っている所でも探し、ほかの対象では探さない。</summary>
    [Fact]
    public void PrefixLimitsTheTarget()
    {
        var hay = Hay("指輪", paths: @"D:\models\Sig_Ring.zip");

        Assert.True(Match("path:sig_ring", hay));
        Assert.False(Match("path:指輪", hay));
        Assert.True(Match("name:指輪", hay));
    }

    /// <summary>全角のコロンも前置きとして読む。</summary>
    [Fact]
    public void FullWidthColonWorksForPrefix()
        => Assert.True(Match("ｐａｔｈ：sig", Hay("指輪", paths: "Sig_Ring.zip")));

    /// <summary>決まった名前でなければ前置きにしない。「Re:Zero」や URL を対象の指定と取り違えない。</summary>
    [Fact]
    public void UnknownPrefixIsPartOfTheWord()
        => Assert.True(Match("re:zero", Hay("Re:Zero コラボ")));

    /// <summary>二重引用符の中の「name:」は前置きとして読まない（ユーザ判断 2026-09-16）。</summary>
    [Fact]
    public void PrefixInsideQuotesIsLiteral()
    {
        Assert.True(Match("\"name:x\"", Hay("tag name:x の説明")));
        Assert.False(Match("\"name:x\"", Hay("x")));
    }

    [Fact]
    public void PrefixAppliesToPhraseAndGroup()
    {
        var hay = Hay("夏 セット", tags: "冬");

        Assert.True(Match("name:\"夏 セット\"", hay));
        Assert.True(Match("tag:(夏 OR 冬)", hay));
        Assert.False(Match("tag:(夏 OR 春)", hay));
    }

    [Fact]
    public void NegatedPrefix()
    {
        var hay = Hay("有料の衣装", tags: "無料");

        Assert.True(Match("衣装 -name:無料", hay));
        Assert.False(Match("衣装 -tag:無料", hay));
    }

    /// <summary>打ちかけの「name:」は落とす。どこにも当たらず0件になるのを避ける。</summary>
    [Fact]
    public void DanglingPrefixIsIgnored()
        => Assert.True(Match("衣装 name:", Hay("冬の衣装")));

    // ---- 区別の切り替え（2026-09-16） ----

    [Fact]
    public void CaseSensitiveWhenAsked()
    {
        var options = SearchOptions.Default with { CaseSensitive = true };

        Assert.False(MatchWith("KUUTA", Hay("kuuta"), options));
        Assert.True(MatchWith("Kuuta", Hay("Kuuta"), options));
    }

    [Fact]
    public void WidthSensitiveWhenAsked()
    {
        var options = SearchOptions.Default with { WidthSensitive = true };

        Assert.False(MatchWith("vrchat", Hay("ＶＲＣｈａｔ想定"), options));
        Assert.True(MatchWith("ＶＲＣｈａｔ", Hay("ＶＲＣｈａｔ想定"), options));
        Assert.True(MatchWith("ｖｒｃｈａｔ", Hay("ＶＲＣｈａｔ想定"), options));
    }

    /// <summary>ひらがなとカタカナは既定で区別し、切り替えで同じに扱う。</summary>
    [Fact]
    public void KanaTypesAreDistinctUnlessAsked()
    {
        var hay = Hay("トリのぬいぐるみ");

        Assert.False(Match("とり", hay));
        Assert.True(MatchWith("とり", hay, SearchOptions.Default with { KanaSensitive = false }));
        Assert.True(MatchWith("トリ", Hay("とりのぬいぐるみ"), SearchOptions.Default with { KanaSensitive = false }));
    }

    [Fact]
    public void KanaInsensitiveWorksWithCaseSensitive()
        => Assert.True(MatchWith(
            "とりKuuta",
            Hay("トリKuuta"),
            SearchOptions.Default with { KanaSensitive = false, CaseSensitive = true }));

    /// <summary>構文の記号は区別の切り替えに関わらず畳む（ユーザ判断「記号も畳みましょう」）。</summary>
    [Fact]
    public void SymbolsAreFoldedEvenWhenWidthSensitive()
        => Assert.False(MatchWith("衣装　－冬", Hay("冬の衣装"), SearchOptions.Default with { WidthSensitive = true }));

    /// <summary>日英変換の英語は英単語の区切りで当てる。top が stop に当たると、関係の無い当たりが増える（2026-09-16 の測定）。</summary>
    [Fact]
    public void WholeWordTermsNeedWordBoundaries()
    {
        var node = new SearchNode.Term("top", "top", WholeWord: true);

        Assert.True(SearchQuery.Matches(node, Hay("Top Hat 帽子"), SearchOptions.Default));
        Assert.True(SearchQuery.Matches(node, Hay("帽子top"), SearchOptions.Default));
        Assert.False(SearchQuery.Matches(node, Hay("Stop sign"), SearchOptions.Default));
        Assert.False(SearchQuery.Matches(node, Hay("topaz"), SearchOptions.Default));
    }

    /// <summary>商品名の読み（造語）は、造語変換を入れたときだけ見る。</summary>
    [Fact]
    public void ReadingsOnlyWhenAsked()
    {
        var hay = Hay("撫で音ギミック", reading: "なでおと");

        Assert.False(Match("なでおと", hay));
        Assert.True(MatchWith("なでおと", hay, SearchOptions.Default with { IncludeReadings = true }));
    }
}
