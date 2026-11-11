using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

public sealed class UnityPackageReadsStoredTests
{
    private static readonly LocalFileRecord Zip = new()
    {
        Hash = "ABC",
        Paths = [@"D:\作り物\a.zip"],
        SizeBytes = 1,
        Contents = ["a/本体.unitypackage", "a/説明.txt", "a/追加.unitypackage"],
    };

    private static UnityPackageReads Reads(params (string Entry, string Path)[] stored) => new(
        _ => stored.Length == 0
            ? null
            : stored.ToDictionary(pair => pair.Entry, pair => (IReadOnlyList<UnityPackageAsset>)[new UnityPackageAsset("g-" + pair.Path, pair.Path)]),
        _ => throw new InvalidOperationException("zip を解かない"),
        (_, _, _) => { });

    [Fact]
    public void 無くなったzipの中身は_控えから全部の包みを読む()
    {
        var assets = Reads(("a/本体.unitypackage", "Assets/A/本体.fbx"), ("a/追加.unitypackage", "Assets/A/追加.fbx"))
            .StoredIfGone(Zip, _ => false);

        Assert.Equal(["Assets/A/本体.fbx", "Assets/A/追加.fbx"], assets.Select(asset => asset.Path));
    }

    [Fact]
    public void 無くなったzipの包みには_zipが在るときと同じ印を付ける()
    {
        // 片方の商品の zip だけ無くなっても、同じ unitypackage と見分けられる（UnityProjectMatcher が共有の部品と取り違えない）
        var contents = Reads(("a/本体.unitypackage", "Assets/A/本体.fbx"), ("a/追加.unitypackage", "Assets/A/追加.fbx"))
            .StoredContentsIfGone(Zip, _ => false);
        var present = new UnityPackageEntry(@"D:\作り物\a.zip", "a/本体.unitypackage", 0) { ZipHash = "abc" };

        Assert.Equal(2, contents.Count);
        Assert.Equal(UnityPackageContents.SourceOf(present, Zip), contents[0].Source);
        Assert.NotEqual(contents[0].Source, contents[1].Source);
    }

    [Fact]
    public void フォルダの中の物には印を付けず_単体のunitypackageはファイルのハッシュで見分ける()
    {
        var inFolder = new UnityPackageEntry(@"D:\作り物\展開", "本体.unitypackage", 0) { InFolder = true };
        var single = new LocalFileRecord { Hash = "DEF", Paths = [@"D:\作り物\本体.unitypackage"], SizeBytes = 1 };
        var renamed = new UnityPackageEntry(@"D:\別の所", "本体 (1).unitypackage", 0) { InFolder = true };

        Assert.Null(UnityPackageContents.SourceOf(inFolder, null));
        Assert.Equal(UnityPackageContents.SourceOf(inFolder, single), UnityPackageContents.SourceOf(renamed, single));
    }

    [Fact]
    public void zipが在れば_別の道で読むので空()
        => Assert.Empty(Reads(("a/本体.unitypackage", "Assets/A/本体.fbx")).StoredIfGone(Zip, _ => true));

    [Fact]
    public void 控えが無ければ_空で_zipを解こうとしない()
        => Assert.Empty(Reads().StoredIfGone(Zip, _ => false));
}
