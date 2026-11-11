using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// ファイルの割り振り（plan 30）の組分けと絞り。名前はすべて作り物（実際のダウンロードの名前は写さない）。
/// 区切りの候補はユーザ指示 2026-11-11 の物。実物の名前では <c>_</c> しか効いていなかったので、ほかの区切りはここで決める。
/// </summary>
public sealed class FilePoolGroupsTests
{
    [Theory]
    [InlineData("SampleShop_Outfit_v1.0.zip", "SampleShop")]
    [InlineData("SampleShop-Outfit.zip", "SampleShop")]
    [InlineData("SampleShop Outfit.zip", "SampleShop")]
    [InlineData("作り物の衣装\u3000黒.zip", "作り物の衣装")]
    [InlineData("SampleShop.Outfit.unitypackage", "SampleShop")]
    [InlineData("Outfit(Black).zip", "Outfit")]
    [InlineData("衣装（黒）.zip", "衣装")]
    [InlineData("Outfit[Quest].zip", "Outfit")]
    [InlineData("衣装【PC用】.zip", "衣装")]
    [InlineData("衣装「夏」.zip", "衣装")]

    // 頭の区切り・括弧は飛ばす（組の名前が空にならないように）
    [InlineData("【作り物】衣装.zip", "作り物")]
    [InlineData("_作り物_衣装.zip", "作り物")]

    // 版の印の前で切る。前が英字なら切らない（名前の一部の「ver」）
    [InlineData("作り物の衣装v1.2.zip", "作り物の衣装")]
    [InlineData("作り物の衣装Ver1.2.zip", "作り物の衣装")]
    [InlineData("作り物の衣装ver.2.zip", "作り物の衣装")]
    [InlineData("SilverRing2.zip", "SilverRing2")]
    [InlineData("Saver.zip", "Saver")]

    // 英字だけが続く名前は大文字小文字の切れ目で切らない
    [InlineData("SampleShopOutfit.zip", "SampleShopOutfit")]

    // 区切りの無い和文はそのまま1つ
    [InlineData("作り物の衣装セット.zip", "作り物の衣装セット")]

    // 区切りだけの名前は、拡張子を除いた名前のまま
    [InlineData("___.zip", "___")]
    public void 組の名前は頭から区切りの最初の1つまで(string fileName, string expected)
        => Assert.Equal(expected, FilePoolGroups.GroupName(fileName));

    [Theory]
    [InlineData(1, 1, HolderCountFilter.AtMost, true)]
    [InlineData(2, 1, HolderCountFilter.AtMost, false)]
    [InlineData(0, 1, HolderCountFilter.AtMost, true)]
    [InlineData(2, 2, HolderCountFilter.AtLeast, true)]
    [InlineData(1, 2, HolderCountFilter.AtLeast, false)]
    [InlineData(2, 2, HolderCountFilter.Exactly, true)]
    [InlineData(3, 2, HolderCountFilter.Exactly, false)]
    public void 重複回数の絞りは以下以上のみ(int count, int threshold, HolderCountFilter filter, bool expected)
        => Assert.Equal(expected, FilePoolGroups.Matches(count, threshold, filter));

    private static PooledFile File(string hash, string? name, params string[] contents)
        => new(hash, name, 1, name is null ? [] : [@"D:\作り物\" + name], ["1"]) { Contents = contents };

    [Fact]
    public void 組は大文字小文字を区別せずにまとめ_名前の順に並べ_名前の無い物は最後()
    {
        var groups = FilePoolGroups.Of(
        [
            File("1", "sampleshop_Top.zip"),
            File("2", "Beta_A.zip"),
            File("3", "SampleShop_Skirt.zip"),
            File("4", null),
        ]);

        Assert.Equal(["Beta", "sampleshop", string.Empty], groups.Select(group => group.Name));
        Assert.Equal(["1", "3"], groups[1].Files.Select(file => file.Hash));
        Assert.Equal(["4"], groups[2].Files.Select(file => file.Hash));
    }

    [Fact]
    public void 組の見出しには中のunitypackageの名前を重ねずに添える()
    {
        var groups = FilePoolGroups.Of(
        [
            File("1", "SampleShop_Set.zip", "Set/SampleShop_Top.unitypackage", @"Set\SampleShop_Skirt.UNITYPACKAGE", "readme.txt"),
            File("2", "SampleShop_Top.unitypackage"),
            File("3", "SampleShop_Manual.pdf"),
        ]);

        var group = Assert.Single(groups);
        Assert.Equal(["SampleShop_Skirt", "SampleShop_Top"], group.PackageNames);
    }
}
