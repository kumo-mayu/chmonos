using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 説明の本文の「〇〇共通素体」を、商品の対応素体の候補として拾う（ユーザ判断 2026-09-29）。
/// 名前はすべて作り物（友人のデータの写しに無いことを確かめた）。
/// </summary>
public class AvatarBaseMentionsTests
{
    private static readonly IReadOnlyList<string> NoIgnored = [];

    private static IReadOnlyList<AvatarBaseMention> Find(
        string description,
        IEnumerable<AvatarBaseGroup>? groups = null,
        IEnumerable<AvatarBaseLink>? links = null,
        string? html = null)
        => AvatarBaseMentions.Find(AvatarDetector.Parse(html, description), groups ?? [], links ?? [], NoIgnored);

    [Fact]
    public void OffersANameThatIsNotInTheRegistryAsNew()
    {
        var found = Find("ミズナギ工房共通素体に対応した衣装です。");

        var mention = Assert.Single(found);
        Assert.Equal("ミズナギ工房", mention.Name);
        Assert.False(mention.IsRegistered);
    }

    [Fact]
    public void UsesTheRegisteredNameWhenTheTextMatchesAnAlias()
    {
        var group = new AvatarBaseGroup
        {
            Name = "Pellucid",
            Aliases = [new AvatarAlias { Text = "ぺるーしど", Source = nameof(AvatarLinkSource.Tag) }],
        };

        var mention = Assert.Single(Find("ぺるーしど共通素体向けのネイルです", [group]));

        Assert.Equal("Pellucid", mention.Name);
        Assert.True(mention.IsRegistered);
    }

    [Theory]
    [InlineData("本商品はミズナギ工房共通素体に着用できます")]
    [InlineData("※ミズナギ工房の共通素体を使っています")]
    [InlineData("【ミズナギ工房共通素体】")]
    public void CutsTheNameOutOfTheSentence(string line)
        => Assert.Equal("ミズナギ工房", Assert.Single(Find(line)).Name);

    [Fact]
    public void KeepsHiraganaInsideTheName()
        => Assert.Equal("くもりび", Assert.Single(Find("くもりび共通素体対応")).Name);

    [Theory]
    [InlineData("ミズナギ工房共通素体ではありません")]
    [InlineData("ミズナギ工房共通素体には対応していません")]
    [InlineData("ミズナギ工房共通素体は非対応です")]
    [InlineData("ミズナギ工房共通素体以外のアバター向け")]
    [InlineData("ミズナギ工房共通素体は着用できません")]
    public void SkipsNegatedLines(string line)
        => Assert.Empty(Find(line));

    [Fact]
    public void ReadsOtherLinesNextToANegatedOne()
    {
        var found = Find("くもりび共通素体には対応していません\nソラトビ共通素体に対応しています");

        Assert.Equal("ソラトビ", Assert.Single(found).Name);
    }

    [Fact]
    public void SkipsCreditSections()
    {
        const string html = "<h2>クレジット</h2><p>撮影：ソラトビ共通素体</p><h2>対応</h2><p>くもりび共通素体</p>";

        var found = Find(string.Empty, html: html);

        Assert.Equal("くもりび", Assert.Single(found).Name);
    }

    [Theory]
    [InlineData("各共通素体に対応")]
    [InlineData("オリジナル共通素体を使用")]
    [InlineData("共通素体に対応")]
    public void SkipsWordsThatDoNotNameABase(string line)
        => Assert.Empty(Find(line));

    [Fact]
    public void SkipsBasesTheItemAlreadyHasOrHadRemoved()
    {
        var links = new[]
        {
            new AvatarBaseLink { BaseName = "くもりび", Source = AvatarLinkSource.Tag, Confirmed = true },
            new AvatarBaseLink { BaseName = "ソラトビ", Source = AvatarLinkSource.Manual, Confirmed = true, Rejected = true },
        };

        var found = Find("くもりび共通素体・ソラトビ共通素体・ヨツバネ共通素体", links: links);

        Assert.Equal("ヨツバネ", Assert.Single(found).Name);
    }

    [Fact]
    public void SkipsBasesDeletedFromTheRegistry()
    {
        var deleted = new AvatarBaseGroup { Name = "ヨツバネ", Rejected = true };

        Assert.Empty(Find("ヨツバネ共通素体に対応", [deleted]));
    }

    [Fact]
    public void ListsEachBaseOnceEvenWhenWrittenInBothHtmlAndPlainText()
    {
        var found = Find("くもりび共通素体に対応", html: "<p>くもりび共通素体に対応</p>");

        Assert.Single(found);
    }
}
