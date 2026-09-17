using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Tests;

/// <summary>共通素体そのものの商品の候補（ユーザ指摘 2026-09-17）。</summary>
public sealed class AvatarBaseItemFinderTests
{
    private static AvatarRegistryEntry Entry(string id, string name, string category, string? baseName = null)
        => new() { ItemId = id, BoothName = name, Category = category, BaseName = baseName };

    [Fact]
    public void 素体名を含むアバターでない商品を候補にする()
    {
        var group = new AvatarBaseGroup { Name = "まるボディ" };
        var entries = new[]
        {
            Entry("1", "【素体】まるボディ ver2", "3Dモデル（その他）"),
            Entry("2", "まるボディ採用アバター「たろう」", "3Dキャラクター"),
            Entry("3", "関係の無い衣装", "3D衣装"),
        };

        Assert.Equal(["1"], AvatarBaseItemFinder.Candidates(group, entries).Select(entry => entry.ItemId));
    }

    [Fact]
    public void 呼び方でも当て消した呼び方と所属するアバターは使わない()
    {
        var group = new AvatarBaseGroup
        {
            Name = "まるボディ",
            Aliases = [new AvatarAlias { Text = "MaruBody" }, new AvatarAlias { Text = "Maru", Rejected = true }],
        };
        var entries = new[]
        {
            Entry("1", "MaruBody base", "3Dモデル（その他）"),
            Entry("2", "Maru hat", "3D小道具"),
            Entry("3", "まるボディのVRoid", "VRoid", baseName: "まるボディ"),
        };

        Assert.Equal(["1"], AvatarBaseItemFinder.Candidates(group, entries).Select(entry => entry.ItemId));
    }
}
