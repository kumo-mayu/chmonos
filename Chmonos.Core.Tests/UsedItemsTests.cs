using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

/// <summary>商品を使っている改変と Unity のプロジェクトの数（<c>docs/spec/used-updates.md</c>）。ID は作り物。</summary>
public class UsedItemsTests
{
    private static ModificationRecord Modification(string id, params ModificationMember[] members) => new()
    {
        Id = id,
        AvatarItemId = "9907201",
        Name = id,
        Members = members,
    };

    [Fact]
    public void 改変は1つにつき1回数え_外した行だけなら数えない()
    {
        var used = new UsedItems(
            [
                Modification("m1", new ModificationMember { ItemId = "A" }, new ModificationMember { ItemId = "A", FileHash = "X" }),
                Modification("m2", new ModificationMember { ItemId = "A" }),
                Modification("m3", new ModificationMember { ItemId = "B", Detached = true }),
            ],
            new Dictionary<string, UnityProjectScan>());

        Assert.Equal(new UsedBy(2, 0), used.Of("A"));
        Assert.False(used.Of("B").IsUsed);
    }

    [Fact]
    public void プロジェクトは入っている物と同じ絵の画像だけを数え_名前が同じだけの画像は数えない()
    {
        var scan = new UnityProjectScan(
            [new UnityProjectMatch("A", 1, 1)],
            new Dictionary<string, IReadOnlyList<UnityImageMatch>>
            {
                ["B"] = [new UnityImageMatch(@"Assets\b.png", "b.png", UnityImageMatchKind.SameImage)],
                ["C"] = [new UnityImageMatch(@"Assets\c.png", "c.png", UnityImageMatchKind.SameName)],
            });

        var used = new UsedItems([], new Dictionary<string, UnityProjectScan> { [@"D:\p1"] = scan, [@"D:\p2"] = scan });

        Assert.Equal(new UsedBy(0, 2), used.Of("A"));
        Assert.Equal(new UsedBy(0, 2), used.Of("B"));
        Assert.False(used.Of("C").IsUsed);
    }
}
