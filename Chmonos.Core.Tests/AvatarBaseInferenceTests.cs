using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 共通素体の名指しと、名前からの所属の推定。
///
/// 手で所属を決める道しか無かった頃は、所持207件の実データでも所属しているアバターが0体で、
/// 素体経由の対応が一度も働いていなかった。
/// </summary>
public class AvatarBaseInferenceTests
{
    [Theory]
    [InlineData("まるぼでぃ素体アバター", "まるぼでぃ")]
    [InlineData("まるぼでぃ対応", "まるぼでぃ")]
    [InlineData("MARUBODY 2.0", "marubody")]
    [InlineData("+head対応", "+head")]
    [InlineData("PlusHead", "+head")]
    [InlineData("+Head", "+head")]
    [InlineData("珍飯亭共通素体", "珍飯亭")]
    public void BringsSpellingsTogether(string text, string expected)
        => Assert.Equal(expected, AvatarBaseKeys.Key(text));

    [Fact]
    public void SeedsMarubodyWithItsReading()
    {
        var group = AvatarBaseSeed.Groups.Single(group => group.Name == "MARUBODY");

        Assert.Contains(group.Aliases, alias => alias.Text == "まるぼでぃ");
    }

    private static AvatarRegistryEntry Avatar(string id, string boothName, params string[] aliases) => new()
    {
        ItemId = id,
        BoothName = boothName,
        Category = "3Dキャラクター",
        Aliases = aliases.Select(text => new AvatarAlias { Text = text, Source = nameof(AvatarLinkSource.SupportSection) }).ToList(),
    };

    private static AvatarRegistry Registry() => new()
    {
        Entries =
        [
            Avatar("nenmir", "オリジナル3Dモデル 「ネミア -Nenmir-」 #MARUBODY"),
            Avatar("lise", "オリジナル3Dモデル「リセ -Lise-」 #MARUBODY"),
            Avatar("hagiri", "【+Head】オリジナル3Dモデル「刃錐」(Hagiri)"),
            Avatar("lumiere", "オリジナル3Dモデル『Lumiere/ルミエール』【+Head】"),
            Avatar("korone", "オリジナル3Dモデル『コロネ』", "コロネ（えも研素体）："),
            Avatar("klara", "オリジナル3Dモデル『クララ』", "クララ（えも研素体）"),
            Avatar("kuuta", "【くうた-Kuuta-】オリジナル3Dモデル"),
        ],
    };

    /// <summary>名前の「#MARUBODY」「【+Head】」、対応節の呼び名の「（えも研素体）」から所属を推す。</summary>
    [Fact]
    public void InfersMembershipFromNames()
    {
        var index = AvatarCompatibilityIndex.Build(Registry());

        Assert.Equal(["lise", "nenmir"], index.MembersOf("MARUBODY").OrderBy(id => id));
        Assert.Equal(["hagiri", "lumiere"], index.MembersOf("+Head").OrderBy(id => id));
        Assert.Equal(["klara", "korone"], index.MembersOf("えも研").OrderBy(id => id));
        Assert.Null(index.BaseNameOf("kuuta"));
    }

    /// <summary>手で決めた所属は、名前から推したものより優先する。</summary>
    [Fact]
    public void PrefersTheManualBase()
    {
        var registry = Registry();
        registry = new AvatarRegistry
        {
            Entries = registry.Entries.Select(entry => entry.ItemId == "lise" ? entry with { BaseName = "まめふれんず" } : entry).ToList(),
        };

        var index = AvatarCompatibilityIndex.Build(registry);

        Assert.Equal("まめふれんず", index.BaseNameOf("lise"));
    }

    /// <summary>商品が素体を名指ししていれば、推した仲間に素体経由で届く（検索の側でつなぐ）。</summary>
    [Fact]
    public void ReachesInferredMembersFromABaseDeclaration()
    {
        var index = AvatarCompatibilityIndex.Build(Registry());
        var local = new LocalBlock
        {
            AvatarBases = [new AvatarBaseLink { BaseName = "MARUBODY", Source = AvatarLinkSource.Tag, Confirmed = true }],
        };

        Assert.Equal(AvatarMatch.ViaBase, index.MatchFor(local, "nenmir"));
        Assert.Equal(AvatarMatch.Unknown, index.MatchFor(local, "kuuta"));
    }

    /// <summary>タグ・種類名・対応の見出しの下の行から、素体の名指しを拾う。</summary>
    [Fact]
    public void FindsBaseDeclarationsInTagsVariationsAndSupportLines()
    {
        var html = """
            <h2>対応アバター</h2>
            <p>+head素体アバター<br>コロネ（えも研素体）： https://booth.pm/ja/items/5776079</p>
            """;

        var names = AvatarDetector.ScanBaseDeclarations(
            html,
            description: null,
            tags: ["まるぼでぃ", "ネイル"],
            variationNames: ["まるぼでぃ対応"],
            groups: [],
            supportHeadings: ["対応アバター"]);

        Assert.Equal(["MARUBODY", "+Head", "えも研"], names);
    }

    /// <summary>「〇〇素体に着用可能です」の行も名指しとみなす。グループの分からない「オリジナル素体」は拾わない。</summary>
    [Fact]
    public void ReadsWearableLinesButNotUnknownBases()
    {
        var description = """
            ※まめふれんず素体に着用可能です
            オリジナル素体、Sotai_CLZTyoe1両対応
            """;

        var names = AvatarDetector.ScanBaseDeclarations(null, description, [], [], [], ["対応アバター"]);

        Assert.Equal(["まめふれんず"], names);
    }
}
