using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// VRoid カテゴリの扱い。
///
/// **無条件にアバターとして受け入れてはいけない。**
/// 実測（一覧の先頭60件）ではアバター本体は3件だけで、残りは
/// テクスチャ・衣装・アクセサリ・ツールだった。無条件だと95%が誤りになる。
///
/// **かといって「アバターではない」でもない。**『BlueMallow / ブルーマロウ』は
/// VRMのモデルなのに category が VRoid で、3Dキャラクターではなかった。
///
/// 分かれ目は**出品者が「対応アバター」として挙げているか**。
/// 3Dモデル（その他）に +Head のような素体が入るのと同じ形で扱う。
/// </summary>
public class VRoidCategoryTests
{
    private static AvatarRegistryEntry VRoid(int fromSupportSection) => new()
    {
        ItemId = "3159577",
        DisplayName = "ブルーマロウ",
        Category = "VRoid",
        SeenAs = fromSupportSection > 0
            ? new Dictionary<string, int> { [nameof(AvatarLinkSource.SupportSection)] = fromSupportSection }
            : new Dictionary<string, int>(),
    };

    /// <summary>誰も対応先として挙げていないVRoid商品は、テクスチャや衣装の可能性が高い。</summary>
    [Fact]
    public void DoesNotTreatAVRoidItemAsAnAvatarOnItsOwn()
        => Assert.False(AvatarService.IsAvatar(VRoid(fromSupportSection: 0)));

    /// <summary>
    /// 出品者が「対応アバター」節に挙げているなら本体。
    /// その節に挙がるのはアバターであって、テクスチャではない。
    /// </summary>
    [Fact]
    public void AcceptsAVRoidItemDeclaredAsASupportedAvatar()
        => Assert.True(AvatarService.IsAvatar(VRoid(fromSupportSection: 1)));

    /// <summary>タグやvariation名から挙がっただけでは受け入れない。根拠が弱い。</summary>
    [Fact]
    public void IgnoresWeakerRoutes()
    {
        var entry = VRoid(fromSupportSection: 0) with
        {
            SeenAs = new Dictionary<string, int> { [nameof(AvatarLinkSource.Tag)] = 5 },
        };

        Assert.False(AvatarService.IsAvatar(entry));
    }

    /// <summary>3Dキャラクターは今まで通り、単独でアバター。</summary>
    [Fact]
    public void StillAcceptsTheCharacterCategoryOnItsOwn()
    {
        var entry = VRoid(fromSupportSection: 0) with { Category = "3Dキャラクター" };

        Assert.True(AvatarService.IsAvatar(entry));
    }

    /// <summary>手で「扱わない」と言われたら、対応アバター節にあっても従う。</summary>
    [Fact]
    public void ObeysTheUserOverride()
    {
        var entry = VRoid(fromSupportSection: 3) with { AvatarOverride = false };

        Assert.False(AvatarService.IsAvatar(entry));
    }
}
