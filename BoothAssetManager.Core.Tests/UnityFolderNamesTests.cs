using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Tests;

/// <summary>入り先のフォルダの頭の記号を利用者が消していても、同じフォルダとみなす。</summary>
public sealed class UnityFolderNamesTests
{
    private static Func<string, IEnumerable<string>> Children(params string[] names) => _ => names;

    [Theory]
    [InlineData("_FUKA", "FUKA")]
    [InlineData("!!FUKA", "FUKA")]
    [InlineData("_ FUKA", "FUKA")]
    [InlineData("＿ふか", "ふか")]
    [InlineData("FUKA", "FUKA")]
    [InlineData("0_FUKA", "0_FUKA")]
    [InlineData("_[作者]衣装", "[作者]衣装")]
    [InlineData("【作者】衣装", "【作者】衣装")]
    [InlineData("___", "___")]
    public void 頭の並べるための記号だけを外す(string name, string expected)
        => Assert.Equal(expected, UnityFolderNames.StripSortPrefix(name));

    [Fact]
    public void 記号を消したフォルダに読み替える()
        => Assert.Equal("Assets/FUKA", UnityFolderNames.ResolveRoot("Assets/_FUKA", Children("FUKA", "lilToon")));

    [Fact]
    public void 利用者が記号を付け足したフォルダにも読み替える()
        => Assert.Equal("Assets/!FUKA", UnityFolderNames.ResolveRoot("Assets/FUKA", Children("!FUKA")));

    [Fact]
    public void 元の名前のフォルダがあればそのまま()
        => Assert.Equal("Assets/_FUKA", UnityFolderNames.ResolveRoot("Assets/_FUKA", Children("_FUKA", "FUKA")));

    [Fact]
    public void 候補が2つあれば取り違えるので読み替えない()
        => Assert.Equal("Assets/_FUKA", UnityFolderNames.ResolveRoot("Assets/_FUKA", Children("FUKA", "!FUKA")));

    [Fact]
    public void Packagesの下は読み替えない()
        => Assert.Equal("Packages/_a", UnityFolderNames.ResolveRoot("Packages/_a", Children("a")));

    [Fact]
    public void パスの頭の2段だけを読み替える()
    {
        var cache = new Dictionary<string, string>();
        Assert.Equal("Assets/FUKA/_x/a.fbx", UnityFolderNames.ResolvePath("Assets/_FUKA/_x/a.fbx", Children("FUKA"), cache));
    }
}
