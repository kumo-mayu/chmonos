using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class AvatarCompatibilityTests
{
    /// <summary>実データに合わせた登録簿。まめふれんず素体は商品として配布されていない。</summary>
    private static AvatarRegistry Registry() => new()
    {
        Entries =
        [
            new AvatarRegistryEntry { ItemId = "kipfel", DisplayName = "キプフェル", BaseName = "まめふれんず" },
            new AvatarRegistryEntry { ItemId = "mamehinata", DisplayName = "まめひなた", BaseName = "まめふれんず" },
            new AvatarRegistryEntry { ItemId = "kuuta", DisplayName = "くうた" },
            new AvatarRegistryEntry { ItemId = "tbody", DisplayName = "T-BODY", BaseName = "珍飯亭" },
            new AvatarRegistryEntry { ItemId = "misumi", DisplayName = "深澄", BaseName = "珍飯亭" },
        ],
        BaseGroups =
        [
            new AvatarBaseGroup { Name = "まめふれんず" },
            new AvatarBaseGroup { Name = "珍飯亭", ItemId = "tbody" },
        ],
    };

    private static LocalBlock Declaring(params string[] avatarIds) => new()
    {
        Avatars = avatarIds
            .Select(id => new AvatarLink { AvatarItemId = id, Source = AvatarLinkSource.SupportSection })
            .ToList(),
    };

    [Fact]
    public void NamedAvatarIsDirect()
    {
        var index = AvatarCompatibilityIndex.Build(Registry());

        Assert.Equal(AvatarMatch.Direct, index.MatchFor(Declaring("kuuta"), "kuuta"));
    }

    /// <summary>名指しされていないアバターは「未確認」。着られないという意味ではない。</summary>
    [Fact]
    public void UnrelatedAvatarIsUnknown()
    {
        var index = AvatarCompatibilityIndex.Build(Registry());

        Assert.Equal(AvatarMatch.Unknown, index.MatchFor(Declaring("kuuta"), "kipfel"));
    }

    /// <summary>同じ素体の兄弟へ広がる。「くうた対応」の衣装をくうたの兄弟でも着られる、という用途。</summary>
    [Fact]
    public void SpreadsToSiblingsOfTheSameBase()
    {
        var index = AvatarCompatibilityIndex.Build(Registry());
        var local = Declaring("kipfel");

        Assert.Equal(AvatarMatch.Direct, index.MatchFor(local, "kipfel"));
        Assert.Equal(AvatarMatch.ViaBase, index.MatchFor(local, "mamehinata"));
    }

    /// <summary>素体そのものの商品を名指ししていれば、そのグループ全体へ広がる。</summary>
    [Fact]
    public void SpreadsFromTheBaseProduct()
    {
        var index = AvatarCompatibilityIndex.Build(Registry());
        var local = Declaring("tbody");

        Assert.Equal(AvatarMatch.ViaBase, index.MatchFor(local, "misumi"));
    }

    /// <summary>素体を名前で名指ししている場合（配布されていない素体）も広がる。</summary>
    [Fact]
    public void SpreadsFromABaseNameDeclaration()
    {
        var index = AvatarCompatibilityIndex.Build(Registry());
        var local = new LocalBlock
        {
            AvatarBases = [new AvatarBaseLink { BaseName = "まめふれんず", Source = AvatarLinkSource.Tag }],
        };

        Assert.Equal(AvatarMatch.ViaBase, index.MatchFor(local, "kipfel"));
        Assert.Equal(AvatarMatch.ViaBase, index.MatchFor(local, "mamehinata"));
    }

    /// <summary>直接対応と素体経由が重なったら、強い方（直接対応）を残す。</summary>
    [Fact]
    public void DirectWinsOverViaBase()
    {
        var index = AvatarCompatibilityIndex.Build(Registry());
        var local = Declaring("kipfel", "mamehinata");

        Assert.Equal(AvatarMatch.Direct, index.MatchFor(local, "mamehinata"));
    }

    /// <summary>
    /// 「衣装の互換を広げない」にした組は、素体が一致しても広げない。
    /// 同じ素体を名乗っていても衣装が合わない組のために、仕組みとして残してある。
    /// </summary>
    [Fact]
    public void DoesNotSpreadThroughGroupsMarkedNotToInfer()
    {
        var registry = new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "a", BaseName = "頭部だけの規格" },
                new AvatarRegistryEntry { ItemId = "b", BaseName = "頭部だけの規格" },
            ],
            BaseGroups = [new AvatarBaseGroup { Name = "頭部だけの規格", InferClothing = false }],
        };

        var index = AvatarCompatibilityIndex.Build(registry);

        Assert.Equal(AvatarMatch.Direct, index.MatchFor(Declaring("a"), "a"));
        Assert.Equal(AvatarMatch.Unknown, index.MatchFor(Declaring("a"), "b"));
    }

    /// <summary>ユーザが消した宣言は最初から数えない。</summary>
    [Fact]
    public void IgnoresRejectedLinks()
    {
        var index = AvatarCompatibilityIndex.Build(Registry());
        var local = new LocalBlock
        {
            Avatars = [new AvatarLink { AvatarItemId = "kuuta", Source = AvatarLinkSource.Manual, Rejected = true }],
        };

        Assert.Empty(index.Resolve(local));
    }
}

public class AvatarServiceRuleTests
{
    private static AvatarRegistryEntry Entry(string category, int supportSeen = 0, bool? avatarOverride = null)
        => new()
        {
            ItemId = "1",
            Category = category,
            AvatarOverride = avatarOverride,
            SeenAs = supportSeen == 0
                ? new Dictionary<string, int>()
                : new Dictionary<string, int> { [nameof(AvatarLinkSource.SupportSection)] = supportSeen },
        };

    [Fact]
    public void AcceptsThreeDCharacterAnywhere()
        => Assert.True(AvatarService.IsAvatar(Entry("3Dキャラクター")));

    /// <summary>
    /// +Head の素体は 3Dモデル（その他）。「対応アバター」節から挙がっているときだけ受け入れる。
    /// 同カテゴリには小道具やシェーダーが大量にあるので、無条件には通さない。
    /// </summary>
    [Fact]
    public void AcceptsOtherModelsOnlyFromSupportSection()
    {
        Assert.True(AvatarService.IsAvatar(Entry("3Dモデル（その他）", supportSeen: 8)));
        Assert.False(AvatarService.IsAvatar(Entry("3Dモデル（その他）")));
    }

    /// <summary>依存ツールや素材は、対応節から挙がっていても受け入れない。</summary>
    [Theory]
    [InlineData("3Dツール・システム")]
    [InlineData("3Dテクスチャ")]
    [InlineData("3D衣装")]
    [InlineData("3D髪型")]
    public void RejectsDependencyCategories(string category)
        => Assert.False(AvatarService.IsAvatar(Entry(category, supportSeen: 3)));

    /// <summary>販売終了でcategoryが観測できないものは、勝手にどちらとも決めない。</summary>
    [Fact]
    public void DoesNotGuessWhenCategoryIsUnknown()
        => Assert.False(AvatarService.IsAvatar(Entry(null!, supportSeen: 5)));

    /// <summary>ユーザの上書きは規則より優先する。</summary>
    [Fact]
    public void UserOverrideWins()
    {
        Assert.True(AvatarService.IsAvatar(Entry(null!, avatarOverride: true)));
        Assert.False(AvatarService.IsAvatar(Entry("3Dキャラクター", avatarOverride: false)));
    }
}

public class AvatarBaseSeedTests
{
    /// <summary>初期辞書は空の登録簿に足される。</summary>
    [Fact]
    public void AddsSeedGroupsToAnEmptyRegistry()
    {
        var groups = new List<AvatarBaseGroup>();

        var added = AvatarBaseSeed.Merge(groups);

        Assert.Equal(AvatarBaseSeed.Groups.Count, added);
        Assert.Contains(groups, group => group.Name == "まめふれんず");
    }

    /// <summary>
    /// 既にある名前には触らない。ユーザが inferClothing を切り替えていても保つ。
    /// </summary>
    [Fact]
    public void KeepsWhatTheUserAlreadyHas()
    {
        var groups = new List<AvatarBaseGroup>
        {
            new() { Name = "+Head", InferClothing = true, Memo = "自分で直した" },
        };

        AvatarBaseSeed.Merge(groups);

        var head = groups.Single(group => group.Name == "+Head");
        Assert.True(head.InferClothing);
        Assert.Equal("自分で直した", head.Memo);
    }

    /// <summary>
    /// +Head も体の共通素体。以前は頭部だけの規格とみなして広げない設定で配っていた。
    /// 初期辞書はすべて衣装の互換を広げる。
    /// </summary>
    [Fact]
    public void EverySeedGroupSpreadsClothing()
        => Assert.All(AvatarBaseSeed.Groups, group => Assert.True(group.InferClothing));

    private static AvatarBaseGroup LegacyPlusHead() => new()
    {
        Name = "+Head",
        InferClothing = false,
        Memo = "頭部の共通規格。頭を差し替えられるだけで、衣装が合うとは限りません。",
        Aliases = [new AvatarAlias { Text = "+Head", Source = "Seed" }, new AvatarAlias { Text = "ぷらすへっど", Source = "Manual" }],
    };

    /// <summary>以前の初期辞書のままの +Head は直す。利用者が足した別名は残す。</summary>
    [Fact]
    public void RepairsTheLegacyPlusHeadSeed()
    {
        var groups = new List<AvatarBaseGroup> { LegacyPlusHead() };

        var repaired = AvatarBaseSeed.RepairLegacy(groups);

        var head = Assert.Single(groups);
        Assert.Equal(1, repaired);
        Assert.True(head.InferClothing);
        Assert.Contains(head.Aliases, alias => alias.Text == "ぷらすへっど");
        Assert.Contains(head.Aliases, alias => alias.Text == "PlusHead");
    }

    /// <summary>利用者がメモを書き換えていたら、その人の判断なので触らない。</summary>
    [Fact]
    public void LeavesAPlusHeadTheUserHasEdited()
    {
        var groups = new List<AvatarBaseGroup> { LegacyPlusHead() with { Memo = "うちでは頭だけ使う" } };

        Assert.Equal(0, AvatarBaseSeed.RepairLegacy(groups));
        Assert.False(groups.Single().InferClothing);
    }

    /// <summary>初期辞書を合流するときにも直す（検出のたびに通る）。</summary>
    [Fact]
    public void MergeAlsoRepairsTheLegacySeed()
    {
        var groups = new List<AvatarBaseGroup> { LegacyPlusHead() };

        AvatarBaseSeed.Merge(groups);

        Assert.True(groups.Single(group => group.Name == "+Head").InferClothing);
    }
}
