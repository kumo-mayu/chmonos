using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Resolution;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 自動検索の再調整（2026-09-29）。「商品名_アバター名」のファイルで、アバター名が AND 検索を0件にしていた。
/// 名前はすべて作り物。
/// </summary>
public class AvatarTokenQueryTests(RegistryCandidateBridgeFixture dictionary) : IClassFixture<RegistryCandidateBridgeFixture>
{
    private const string Avatar = "3Dキャラクター";

    private static AvatarRegistryEntry Entry(string id, string boothName, string category = Avatar) => new()
    {
        ItemId = id,
        BoothName = boothName,
        Category = category,
    };

    /// <summary>英字の表記を持つアバター・かなだけのアバター・漢字だけのアバター・衣装の項目。</summary>
    private static AvatarRegistry Registry() => new()
    {
        Entries =
        [
            Entry("101", "オリジナル3Dモデル「もふりん」-Mofurin-"),
            Entry("102", "オリジナル3Dモデル「ささなみ」"),
            Entry("103", "オリジナル3Dモデル「星羅」"),
            Entry("104", "【もふりん対応】Cape", category: "3D衣装"),
        ],
    };

    private AvatarTokens Tokens() => AvatarTokens.From(Registry(), dictionary.Readings);

    [Theory]
    [InlineData("RibbonCape_Mofurin.zip", "Ribbon Cape")]
    [InlineData("Mofurin_RibbonCape_v1.0.zip", "Ribbon Cape")]
    [InlineData("RibbonCape_もふりん用.zip", "Ribbon Cape")]
    public void DropsAvatarNamesFromTheQuery(string fileName, string expected)
        => Assert.Equal(expected, FileNameQuery.ToSearchQuery(fileName, Tokens().IsAvatarName));

    /// <summary>登録簿の名前がかなや漢字だけでも、ファイル名のローマ字で当てる。</summary>
    [Theory]
    [InlineData("RibbonCape_Sasanami.zip", "Ribbon Cape")]
    [InlineData("RibbonCape_Seira.zip", "Ribbon Cape")]
    public void MatchesAvatarNamesByTheirRomanizedReading(string fileName, string expected)
        => Assert.Equal(expected, FileNameQuery.ToSearchQuery(fileName, Tokens().IsAvatarName));

    /// <summary>アバターそのものの zip は名前で引くしかない。</summary>
    [Fact]
    public void KeepsTheNameWhenNothingElseIsLeft()
        => Assert.Equal("Mofurin", FileNameQuery.ToSearchQuery("Mofurin_v1.0.zip", Tokens().IsAvatarName));

    /// <summary>衣装の項目の名前（Cape）は商品名の語なので落とさない。</summary>
    [Fact]
    public void KeepsNamesOfItemsThatAreNotAvatars()
        => Assert.False(Tokens().IsAvatarName("Cape"));

    /// <summary>ローマ字の読みでも、名前の一部には当てない（2字の読みや、長い名前の途中）。</summary>
    [Theory]
    [InlineData("Sasa")]
    [InlineData("Ribbon")]
    [InlineData("Mo")]
    public void DoesNotTakeOrdinaryWordsForAvatarNames(string token)
        => Assert.False(Tokens().IsAvatarName(token));

    [Fact]
    public void RetriesWithAProductWordRatherThanTheLongerAvatarName()
    {
        Assert.Equal("Mofurin", FileNameQuery.MostDistinctiveToken("Mofurin_Cape_Ribbon.zip"));
        Assert.Equal("Cape", FileNameQuery.MostDistinctiveToken("Mofurin_Cape_Ribbon.zip", Tokens().IsAvatarName));
    }

    /// <summary>
    /// アバターだけが違う同じ作者の商品は、商品名の語では決まらない。ファイル名のアバターを名前に出す方を上げる。
    /// 英字のファイル名（Mofurin）と、かなの商品名（【もふりん専用】）も同じアバターとして結ぶ。
    /// </summary>
    [Fact]
    public void RerankPrefersTheCardForTheSameAvatar()
    {
        IReadOnlyList<FallbackResolver.SearchCard> cards =
        [
            new("201", "【ささなみ専用】Ribbon Cape", "maker"),
            new("202", "【もふりん専用】Ribbon Cape", "maker"),
        ];

        var ranked = FallbackResolver.Rerank(cards, "RibbonCape_Mofurin.zip", Tokens());

        Assert.Equal(["202", "201"], ranked.Select(card => card.ItemId));
    }

    /// <summary>ファイル名が名前だけ（アバター本体の zip）なら、そのアバター向けの商品より本体を上げる。</summary>
    [Fact]
    public void RerankLiftsTheAvatarItselfForAnAvatarOnlyFile()
    {
        IReadOnlyList<FallbackResolver.SearchCard> cards =
        [
            new("201", "【もふりん専用】Mofurin Ribbon Cape", "maker"),
            new("101", "オリジナル3Dモデル「もふりん」-Mofurin-", "avatar-shop"),
        ];

        var ranked = FallbackResolver.Rerank(cards, "Mofurin_PSD.zip", Tokens());

        Assert.Equal("101", ranked[0].ItemId);
    }

    /// <summary>同じ名前のアバターが登録簿に2項目あっても（別版など）、名前として扱い並べ直しにも効く。</summary>
    [Fact]
    public void TreatsANameSharedByTwoEntriesAsAnAvatarName()
    {
        var registry = new AvatarRegistry
        {
            Entries =
            [
                Entry("301", "オリジナル3Dモデル「くるみな」-Kurumina-"),
                Entry("302", "オリジナル3Dモデル「くるみな」-Kurumina- 大人版"),
            ],
        };
        var tokens = AvatarTokens.From(registry, dictionary.Readings);
        IReadOnlyList<FallbackResolver.SearchCard> cards =
        [
            new("401", "Ribbon Cape", "maker"),
            new("402", "【くるみな専用】Ribbon Cape", "maker"),
        ];

        Assert.Equal("Ribbon Cape", FileNameQuery.ToSearchQuery("RibbonCape_Kurumina.zip", tokens.IsAvatarName));
        Assert.Equal("402", FallbackResolver.Rerank(cards, "RibbonCape_Kurumina.zip", tokens)[0].ItemId);
    }

    /// <summary>衣装のファイルで、アバター本体の商品を上げない。</summary>
    [Fact]
    public void RerankDoesNotLiftTheAvatarItself()
    {
        IReadOnlyList<FallbackResolver.SearchCard> cards =
        [
            new("201", "Ribbon Cape", "maker"),
            new("101", "オリジナル3Dモデル「もふりん」-Mofurin-", "avatar-shop"),
        ];

        var ranked = FallbackResolver.Rerank(cards, "RibbonCape_Mofurin.zip", Tokens());

        Assert.Equal("201", ranked[0].ItemId);
    }
}
