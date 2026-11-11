using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>Unity プロジェクトの VPM の読み取り（plan 40）。名前・版は作り物。</summary>
public sealed class VpmPackagesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("chmonos-vpm-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string Manifest = """
        {
          "dependencies": {
            "com.example.shader": { "version": "2.0.0" },
            "com.example.tool": { "version": "1.0.0" }
          },
          "locked": {
            "com.example.shader": { "version": "2.0.1", "dependencies": {} },
            "com.example.tool": { "version": "1.0.0", "dependencies": { "com.example.core": ">=1.0.0" } },
            "com.example.core": { "version": "1.5.0", "dependencies": {} }
          }
        }
        """;

    private static string PackageJson(string name, string? displayName, string version)
        => displayName is null
            ? $$"""{ "name": "{{name}}", "version": "{{version}}" }"""
            : $$"""{ "name": "{{name}}", "displayName": "{{displayName}}", "version": "{{version}}" }""";

    [Fact]
    public void manifestのlockedにだけある物も_VPMとして並べ_版はフォルダのpackage_jsonを使う()
    {
        var packages = VpmPackages.Parse(Manifest,
        [
            ("com.example.shader", PackageJson("com.example.shader", "作り物シェーダー", "2.0.1")),
            ("com.example.tool", PackageJson("com.example.tool", "Tool", "1.0.0")),
            ("com.example.core", PackageJson("com.example.core", "Core", "1.5.0")),
        ]);

        Assert.Equal(
            [
                new VpmPackage("com.example.core", "Core", "1.5.0", "com.example.core", true),
                new VpmPackage("com.example.tool", "Tool", "1.0.0", "com.example.tool", true),
                new VpmPackage("com.example.shader", "作り物シェーダー", "2.0.1", "com.example.shader", true),
            ],
            packages);
    }

    [Fact]
    public void manifestに無いフォルダは埋め込みとして並べ_package_jsonの無いフォルダは並べない()
    {
        var packages = VpmPackages.Parse(Manifest,
        [
            ("com.example.shader", PackageJson("com.example.shader", null, "2.0.1")),
            ("MyEmbedded", PackageJson("com.example.embedded", "手で置いた物", "0.3.0")),
            ("NotAPackage", null),
        ]);

        var embedded = Assert.Single(packages, package => !package.InManifest);
        Assert.Equal(new VpmPackage("com.example.embedded", "手で置いた物", "0.3.0", "MyEmbedded", false), embedded);
        Assert.DoesNotContain(packages, package => package.Folder == "NotAPackage");

        // 表示名の無い物は名前で見せる
        Assert.Equal("com.example.shader", packages.Single(package => package.Name == "com.example.shader").Shown);
    }

    [Fact]
    public void manifestにあってフォルダの無い物は_manifestの版でフォルダ無しとして並べる()
    {
        var packages = VpmPackages.Parse(Manifest, []);

        Assert.Equal(3, packages.Count);
        Assert.All(packages, package => Assert.Null(package.Folder));
        Assert.Equal("2.0.1", packages.Single(package => package.Name == "com.example.shader").Version);
    }

    [Theory]
    [InlineData("{ 壊れている")]
    [InlineData("[]")]
    [InlineData("""{ "dependencies": [] }""")]
    [InlineData("""{ "dependencies": { "com.example.x": "1.0.0" } }""")]
    public void manifestが壊れている_欄の形が違うときも_止まらずに読めた分を返す(string manifest)
    {
        var packages = VpmPackages.Parse(manifest, [("com.example.y", PackageJson("com.example.y", "Y", "1.0.0"))]);

        // 形の違う dependencies の値（文字列）は版が読めないだけで、名前は並べる
        Assert.Contains(packages, package => package is { Name: "com.example.y", InManifest: false });
        Assert.All(packages.Where(package => package.InManifest), package => Assert.Null(package.Version));
    }

    [Fact]
    public void 壊れたpackage_jsonのフォルダは並べない()
    {
        var packages = VpmPackages.Parse(null, [("broken", "{ 壊れている"), ("array", "[]")]);

        Assert.Empty(packages);
    }

    [Fact]
    public void ディスクから読むと_Packagesの下の隠しフォルダは見ない()
    {
        var packages = Path.Combine(_root, "Packages");
        Directory.CreateDirectory(Path.Combine(packages, "com.example.shader"));
        Directory.CreateDirectory(Path.Combine(packages, ".git"));
        File.WriteAllText(Path.Combine(packages, "vpm-manifest.json"), Manifest);
        File.WriteAllText(Path.Combine(packages, "com.example.shader", "package.json"), PackageJson("com.example.shader", "作り物シェーダー", "2.0.1"));
        File.WriteAllText(Path.Combine(packages, ".git", "package.json"), PackageJson("com.example.hidden", "隠し", "9.9.9"));

        var read = VpmPackages.Read(_root);

        Assert.Equal("com.example.shader", read.Single(package => package.Folder is not null).Name);
        Assert.DoesNotContain(read, package => package.Name == "com.example.hidden");
    }

    [Fact]
    public void Packagesの無いプロジェクトは空()
        => Assert.Empty(VpmPackages.Read(_root));

    [Fact]
    public void 調べた結果の控えに_VPMを書いて読み戻せ_欄の無い前の控えは読んでいないとして読む()
    {
        var paths = new AppPaths(Path.Combine(_root, "store"));
        paths.EnsureCreated();
        var store = new UnityScanStore(paths);
        var vpm = new VpmPackage("com.example.shader", "作り物シェーダー", "2.0.1", "com.example.shader", true);

        store.Save(@"D:\作り物\projA", UnityProjectScan.Empty with { Vpm = [vpm] });
        store.Save(@"D:\作り物\projB", UnityProjectScan.Empty);

        Assert.Equal([vpm], store.Load(@"D:\作り物\projA")!.Vpm);
        Assert.Null(store.Load(@"D:\作り物\projB")!.Vpm);

        // 計算で出せる表示名（Shown）は控えに書かない
        var written = File.ReadAllText(Directory.GetFiles(Path.Combine(paths.Root, AppPaths.UnityScansDirName))
            .Single(file => File.ReadAllText(file).Contains("projA", StringComparison.Ordinal)));
        Assert.DoesNotContain("shown", written, StringComparison.OrdinalIgnoreCase);
    }
}
