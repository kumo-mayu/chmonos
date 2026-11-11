using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>送る unitypackage と VPM の GUID の重なり（plan 40）。GUID・名前は作り物。</summary>
public sealed class VpmOverlapsTests
{
    private static readonly VpmPackage Shader = new("com.example.shader", "作り物シェーダー", "2.0.1", "com.example.shader", true);
    private static readonly VpmPackage Tool = new("com.example.tool", "作り物の道具", "1.0.0", "com.example.tool", true);

    private static string Guid(int n) => n.ToString("x32");

    private static UnityPackageEntry Entry(string name, string hash = "AAAA") => new($@"D:\files\{name}.zip", $"{name}.unitypackage", 1) { ZipHash = hash };

    /// <summary>プロジェクトの GUID の表：1〜3 は VPM のシェーダー、4 は道具、5 は package.json の無い実フォルダ、6 は Assets。</summary>
    private static readonly Dictionary<string, string> Project = new(StringComparer.OrdinalIgnoreCase)
    {
        [Guid(1)] = "Packages/com.example.shader/Shader/a.shader",
        [Guid(2)] = "Packages/com.example.shader/Shader/b.shader",
        [Guid(3)] = "Packages/com.example.shader/Shader",
        [Guid(4)] = "Packages/com.example.tool/Editor/t.cs",
        [Guid(5)] = "Packages/blendshare.example/x.asset",
        [Guid(6)] = "Assets/Mine/m.mat",
    };

    [Fact]
    public void VPMと同じGUIDのファイルを数え_フォルダの項目は数えず_入る場所の共通の頭を添える()
    {
        IReadOnlyList<UnityPackageAsset> assets =
        [
            new(Guid(3), "Assets/lilToon/Shader"),
            new(Guid(1), "Assets/lilToon/Shader/a.shader"),
            new(Guid(2), "Assets/lilToon/Shader/b.shader"),
            new(Guid(9), "Assets/Outfit/o.prefab"),
        ];

        var found = VpmOverlaps.Find([Entry("outfit")], _ => assets, [Shader, Tool], () => Project);

        var hit = Assert.Single(found);
        Assert.Equal(0, hit.Index);
        var overlap = Assert.Single(hit.Overlaps);
        Assert.Equal((Shader, 2, "Assets/lilToon/Shader"), (overlap.Package, overlap.Count, overlap.Root));
    }

    [Fact]
    public void package_jsonの無い実フォルダとAssetsの物は_重なりと数えない()
    {
        IReadOnlyList<UnityPackageAsset> assets = [new(Guid(5), "Packages/blendshare.example/x.asset"), new(Guid(6), "Assets/Mine/m.mat")];

        Assert.Empty(VpmOverlaps.Find([Entry("outfit")], _ => assets, [Shader, Tool], () => Project));
    }

    [Fact]
    public void manifestに無い埋め込みの物と_同じPackagesのフォルダへ入れ直す物は_重なりと数えない()
    {
        // unitypackage が自分で Packages/ に置いた物（package.json を同梱）。入れ直すたびに言うと、入れたい物を外させてしまう
        var embedded = Shader with { InManifest = false };
        IReadOnlyList<UnityPackageAsset> intoAssets = [new(Guid(1), "Assets/S/a.shader")];
        Assert.Empty(VpmOverlaps.Find([Entry("outfit")], _ => intoAssets, [embedded], () => Project));

        // VPM の物でも、同じ Packages/<フォルダ> の中へ入れる包みは、その包み自身の入れ直し
        IReadOnlyList<UnityPackageAsset> intoSame = [new(Guid(1), "Packages/com.example.shader/Shader/a.shader")];
        Assert.Empty(VpmOverlaps.Find([Entry("outfit")], _ => intoSame, [Shader], () => Project));
    }

    [Fact]
    public void Assetsの直下に散らばるときは_場所を言わない()
    {
        IReadOnlyList<UnityPackageAsset> assets = [new(Guid(1), "Assets/a.shader"), new(Guid(4), "Assets/Tools/t.cs"), new(Guid(2), "Assets/Other/b.shader")];

        var hit = Assert.Single(VpmOverlaps.Find([Entry("outfit")], _ => assets, [Shader, Tool], () => Project));

        Assert.Equal(3, hit.Count);
        Assert.All(hit.Overlaps.Where(overlap => overlap.Package == Shader), overlap => Assert.Null(overlap.Root));
        Assert.Equal("Assets/Tools", hit.Overlaps.Single(overlap => overlap.Package == Tool).Root);

        // 多い方から並べる
        Assert.Equal([Shader, Tool], hit.Overlaps.Select(overlap => overlap.Package));
    }

    [Fact]
    public void 中身の控えが無い包みは照らさず_照らす物が無ければプロジェクトの表を作らない()
    {
        var asked = 0;
        Dictionary<string, string> Table()
        {
            asked++;
            return Project;
        }

        Assert.Empty(VpmOverlaps.Find([Entry("single", hash: null!)], _ => null, [Shader], Table));
        Assert.Equal(0, asked);

        // VPM のフォルダが無いプロジェクト（manifest だけ）でも作らない
        Assert.Empty(VpmOverlaps.Find([Entry("outfit")], _ => [new(Guid(1), "Assets/a.shader")], [Shader with { Folder = null }], Table));
        Assert.Equal(0, asked);
    }

    [Fact]
    public void 送る列の位置を持ち_重なる包みだけ並べる()
    {
        var packages = new[] { Entry("clean"), Entry("withShader", "BBBB") };
        IReadOnlyList<UnityPackageAsset>? Stored(UnityPackageEntry entry)
            => entry.ZipHash == "BBBB" ? [new(Guid(1), "Assets/S/a.shader")] : [new(Guid(9), "Assets/C/c.prefab")];

        var hit = Assert.Single(VpmOverlaps.Find(packages, Stored, [Shader], () => Project));

        Assert.Equal((1, "withShader"), (hit.Index, hit.Entry.Name));
    }

    [Fact]
    public void 控えだけを読む口は_ハッシュの無い包みとフォルダの中の物に_nullを返す()
    {
        var reads = new UnityPackageReads(_ => throw new InvalidOperationException("控えを読まない"), _ => throw new InvalidOperationException("zip を解かない"), (_, _, _) => { });

        Assert.Null(reads.StoredOnly(new UnityPackageEntry(@"D:\files\a.zip", "a.unitypackage", 1)));
        Assert.Null(reads.StoredOnly(new UnityPackageEntry(@"D:\files", "a.unitypackage", 1) { InFolder = true, ZipHash = "AAAA" }));
    }

    [Fact]
    public void ディスクの作り物のプロジェクトで_manifestとmetaを読んで重なりを見つける()
    {
        var root = Directory.CreateTempSubdirectory("chmonos-vpm-overlap-").FullName;
        try
        {
            var shader = Path.Combine(root, "Packages", "com.example.shader", "Shader");
            Directory.CreateDirectory(shader);
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            File.WriteAllText(Path.Combine(root, "Packages", "vpm-manifest.json"), """{ "dependencies": { "com.example.shader": { "version": "2.0.1" } } }""");
            File.WriteAllText(Path.Combine(root, "Packages", "com.example.shader", "package.json"), """{ "name": "com.example.shader", "displayName": "作り物シェーダー", "version": "2.0.1" }""");
            File.WriteAllText(Path.Combine(shader, "a.shader"), "Shader \"a\" {}");
            File.WriteAllText(Path.Combine(shader, "a.shader.meta"), $"fileFormatVersion: 2\nguid: {Guid(1)}\n");
            var reads = new UnityPackageReads(
                _ => new Dictionary<string, IReadOnlyList<UnityPackageAsset>> { ["outfit.unitypackage"] = [new(Guid(1), "Assets/lilToon/Shader/a.shader")] },
                _ => throw new InvalidOperationException("zip を解かない"),
                (_, _, _) => { });

            var hit = Assert.Single(VpmOverlaps.FindInProject(root, [Entry("outfit")], reads));

            Assert.Equal(("作り物シェーダー", 1, "Assets/lilToon/Shader"), (hit.Overlaps[0].Package.Shown, hit.Count, hit.Overlaps[0].Root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 控えだけを読む口は_控えに無ければzipを解かずにnullを返す()
    {
        var reads = new UnityPackageReads(_ => null, _ => throw new InvalidOperationException("zip を解かない"), (_, _, _) => throw new InvalidOperationException("書き足さない"));

        Assert.Null(reads.StoredOnly(Entry("outfit")));
    }
}
