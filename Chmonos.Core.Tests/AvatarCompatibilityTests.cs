using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

public class AvatarCompatibilityTests
{
    /// <summary>実データに合わせた登録簿。まめふれんず素体は商品として配布されていない。</summary>
    internal static AvatarRegistry Registry() => new()
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

    internal static LocalBlock Declaring(params string[] avatarIds) => new()
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

    /// <summary>
    /// 説明文のリンク（要確認）は絞り込みで数えない。サムネに使ったアバターやクレジットで、
    /// 対応の宣言ではないことが多い。
    /// </summary>
    [Fact]
    public void DoesNotCountDescriptionLinks()
    {
        var index = AvatarCompatibilityIndex.Build(Registry());
        var local = new LocalBlock
        {
            Avatars = [new AvatarLink { AvatarItemId = "kuuta", Source = AvatarLinkSource.H2Link }],
        };

        Assert.Equal(AvatarMatch.Unknown, index.MatchFor(local, "kuuta"));
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

    /// <summary>
    /// 展開した結果を商品の記録ごとに覚えても（2026-09-24）、何度引いても・記録を作り直しても、
    /// まっさらな索引で引いた結果と同じ。
    /// </summary>
    [Fact]
    public void RememberedResolutionEqualsFreshOne()
    {
        var index = AvatarCompatibilityIndex.Build(AvatarCompatibilityTests.Registry());
        var locals = new[]
        {
            AvatarCompatibilityTests.Declaring("kipfel"),
            AvatarCompatibilityTests.Declaring("tbody"),
            AvatarCompatibilityTests.Declaring(),
            AvatarCompatibilityTests.Declaring("kuuta", "kipfel"),
            new LocalBlock { AvatarBases = [new AvatarBaseLink { BaseName = "珍飯亭" }] },
        };

        for (var round = 0; round < 3; round++)
        {
            foreach (var local in locals)
            {
                var fresh = AvatarCompatibilityIndex.Build(AvatarCompatibilityTests.Registry()).Resolve(local);
                Assert.Equal(fresh.OrderBy(pair => pair.Key), index.Resolve(local).OrderBy(pair => pair.Key));
            }
        }

        // 記録を作り直せば（with）、新しい中身で展開する
        var changed = locals[2] with { Avatars = [new AvatarLink { AvatarItemId = "misumi", Source = AvatarLinkSource.SupportSection }] };
        Assert.Empty(index.Resolve(locals[2]));
        Assert.Equal(AvatarMatch.Direct, index.MatchFor(changed, "misumi"));
        Assert.Equal(AvatarMatch.ViaBase, index.MatchFor(changed, "tbody"));
    }
}
