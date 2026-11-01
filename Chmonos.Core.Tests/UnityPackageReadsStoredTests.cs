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
    public void zipが在れば_別の道で読むので空()
        => Assert.Empty(Reads(("a/本体.unitypackage", "Assets/A/本体.fbx")).StoredIfGone(Zip, _ => true));

    [Fact]
    public void 控えが無ければ_空で_zipを解こうとしない()
        => Assert.Empty(Reads().StoredIfGone(Zip, _ => false));
}
