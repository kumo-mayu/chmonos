using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Tests;

/// <summary>Unity プロジェクトのフォルダを見て、手元のどの商品が入っているかを数える（#72）。</summary>
public sealed class UnityProjectMatcherTests
{
    private const string Project = @"C:\proj";

    private static Func<string, bool> Has(params string[] unityPaths)
    {
        var set = unityPaths.Select(path => Path.Combine(Project, path.Replace('/', Path.DirectorySeparatorChar)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    private static Dictionary<string, IReadOnlyList<string>> Items(params (string Id, string[] Paths)[] items)
        => items.ToDictionary(item => item.Id, item => (IReadOnlyList<string>)item.Paths);

    [Fact]
    public void 入っているファイルを数えて割合で並べる()
    {
        var items = Items(
            ("pen", ["Assets/nHaruka", "Assets/nHaruka/Pen.asset", "Assets/nHaruka/Pen.prefab", "Assets/nHaruka/Ink.mat"]),
            ("sweater", ["Assets/Kuuta/Sweater.fbx", "Assets/Kuuta/Sweater.mat"]));

        var matches = UnityProjectMatcher.Match(Project, items,
            Has("Assets/nHaruka/Pen.asset", "Assets/Kuuta/Sweater.fbx", "Assets/Kuuta/Sweater.mat"));

        Assert.Equal(["sweater", "pen"], matches.Select(match => match.ItemId));
        Assert.Equal((2, 2), (matches[0].Present, matches[0].Total));
        Assert.Equal((1, 3), (matches[1].Present, matches[1].Total));
    }

    [Fact]
    public void フォルダは数えない()
    {
        // 上のフォルダは同じ作者の別の商品とも重なるので、ファイルだけを見る
        var items = Items(("a", ["Assets/FUKA", "Assets/FUKA/Addon", "Assets/FUKA/Addon/Sound.wav"]));

        Assert.Empty(UnityProjectMatcher.Match(Project, items, Has("Assets/FUKA", "Assets/FUKA/Addon")));
    }

    [Fact]
    public void ほかの商品と共有しているファイルは数えない()
    {
        // lilToon を同梱したアバターが2つ。lilToon が入っているだけでは候補に出さない
        var items = Items(
            ("avatarA", ["Assets/lilToon/Shader/lts.shader", "Assets/A/A.fbx"]),
            ("avatarB", ["Assets/lilToon/Shader/lts.shader", "Assets/B/B.fbx"]));

        var matches = UnityProjectMatcher.Match(Project, items, Has("Assets/lilToon/Shader/lts.shader", "Assets/B/B.fbx"));

        var only = Assert.Single(matches);
        Assert.Equal("avatarB", only.ItemId);
        Assert.Equal((1, 1), (only.Present, only.Total));
    }

    [Fact]
    public void Packagesに入る物も数える()
    {
        // BlendShare は Packages/com.triturbo.blendshare に入る
        var items = Items(("blendshare", ["Packages/com.triturbo.blendshare/package.json", "Packages/com.triturbo.blendshare/Editor/A.cs"]));

        var only = Assert.Single(UnityProjectMatcher.Match(Project, items, Has("Packages/com.triturbo.blendshare/package.json")));
        Assert.Equal((1, 2), (only.Present, only.Total));
    }

    [Fact]
    public void 一つも無ければ候補に出さない()
        => Assert.Empty(UnityProjectMatcher.Match(Project, Items(("a", ["Assets/A/A.fbx"])), Has()));

    [Fact]
    public void パッケージを読めなかった商品は飛ばす()
        => Assert.Empty(UnityProjectMatcher.Match(Project, Items(("a", [])), Has("Assets/A/A.fbx")));

    [Fact]
    public void 入り先の頭の記号を消したプロジェクトでも入っていると数える()
    {
        // ショップが先頭に並べるために付けた記号（_FUKA）を、利用者が消していることがある
        var items = Items(("a", ["Assets/_FUKA/Addon/Sound.wav", "Assets/_FUKA/Addon/Sound.mat"]));

        var only = Assert.Single(UnityProjectMatcher.Match(Project, items,
            Has("Assets/FUKA/Addon/Sound.wav"), _ => ["FUKA"]));

        Assert.Equal((1, 2), (only.Present, only.Total));
    }
}
