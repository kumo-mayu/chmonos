using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Tests;

public sealed class UnityHandoffTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "unity-handoff-" + Guid.NewGuid().ToString("N")[..8]);

    public UnityHandoffTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // 掴まれていることがある。試験の後片付けで落ちる必要はない
        }
    }

    private string MakeZip(string name, params string[] entries)
    {
        var path = Path.Combine(_dir, name);
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);
        foreach (var entry in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
            writer.Write("中身は見ない");
        }

        return path;
    }

    [Fact]
    public void 直下のunitypackageを見つける()
    {
        var zip = MakeZip("flat.zip", "Sig_Ring_07_ver2.unitypackage", "readme.txt");

        var found = UnityHandoff.FindPackages(zip);

        Assert.Single(found);
        Assert.Equal("Sig_Ring_07_ver2", found[0].Name);
        Assert.Equal(string.Empty, found[0].Folder);
    }

    [Fact]
    public void unitypackage以外は拾わない()
    {
        var zip = MakeZip("none.zip", "cover.png", "manual.pdf", "source.psd");

        Assert.Empty(UnityHandoff.FindPackages(zip));
    }

    [Fact]
    public void 仮想パスはzipをフォルダとして繋いだ形になる()
    {
        var zip = MakeZip("nested.zip", "Bracelet.v1.01/Bracelet.v1.01.unitypackage");

        var found = UnityHandoff.FindPackages(zip);

        Assert.Equal(
            Path.Combine(zip, @"Bracelet.v1.01\Bracelet.v1.01.unitypackage"),
            found[0].VirtualPath);
        Assert.Equal("Bracelet.v1.01", found[0].Folder);
    }

    [Fact]
    public void 日本語のフォルダ名でも仮想パスが組める()
    {
        // 手元の実データがこの形。ここが崩れると送れない
        var zip = MakeZip("heart.zip", "なめらか心音ギミックv3.0.3/VRCHeartRate_Installer.unitypackage");

        var found = UnityHandoff.FindPackages(zip);

        Assert.Equal("VRCHeartRate_Installer", found[0].Name);
        Assert.EndsWith(@"なめらか心音ギミックv3.0.3\VRCHeartRate_Installer.unitypackage", found[0].VirtualPath);
    }

    [Fact]
    public void 複数入っているとき並びをzipのまま保つ()
    {
        // 依存物を先に入れないと本体が通らない。順序を勝手に決めない
        var zip = MakeZip(
            "two.zip",
            "Kuuta_ShapekeyAddon/BlendShare-0.0.10-User.unitypackage",
            "Kuuta_ShapekeyAddon/Kuuta_ShapekeyAddon.unitypackage");

        var found = UnityHandoff.FindPackages(zip);

        Assert.Equal(2, found.Count);
        Assert.Equal("BlendShare-0.0.10-User", found[0].Name);
        Assert.Equal("Kuuta_ShapekeyAddon", found[1].Name);
    }

    [Fact]
    public void 読めないzipでは空を返す()
    {
        var broken = Path.Combine(_dir, "broken.zip");
        File.WriteAllText(broken, "これはzipではない");

        Assert.Empty(UnityHandoff.FindPackages(broken));
    }

    [Fact]
    public void 無いファイルでも投げない()
    {
        Assert.Empty(UnityHandoff.FindPackages(Path.Combine(_dir, "居ない.zip")));
    }

    /// <summary>本物と同じ形の unitypackage（tar.gz、アセットごとに guid/pathname）を作る。</summary>
    private static byte[] MakeUnityPackage(params string[] assetPaths)
    {
        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            for (var index = 0; index < assetPaths.Length; index++)
            {
                var guid = index.ToString("x32");
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"{guid}/asset")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes("本体")),
                });
                // 本物の pathname は2行目に "00" が付いていることがある
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"{guid}/pathname")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(assetPaths[index] + "\n00")),
                });
            }
        }

        return memory.ToArray();
    }

    private UnityPackageEntry MakeZipWithPackage(string entryPath, byte[] package)
    {
        var path = Path.Combine(_dir, "with-package.zip");
        using (var stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8))
        using (var entry = archive.CreateEntry(entryPath).Open())
        {
            entry.Write(package);
        }

        return UnityHandoff.FindPackages(path).Single();
    }

    [Fact]
    public void unitypackageから入る先を読む()
    {
        var package = MakeZipWithPackage(
            "Bracelet/Bracelet.v1.01.unitypackage",
            MakeUnityPackage(
                "Assets/Piyo_crafts",
                "Assets/Piyo_crafts/Bracelet/Bracelet.prefab",
                "Assets/Piyo_crafts/Bracelet/Textures/base.png"));

        Assert.Equal(["Assets/Piyo_crafts"], UnityHandoff.ReadDestinations(package));
    }

    [Fact]
    public void Packagesに入る物も入る先として読む()
    {
        // BlendShare の実例。Assets/ だけ見て「入っていない」と取り違えたことがある
        var package = MakeZipWithPackage(
            "日本語のフォルダ/BlendShare-0.0.10-User.unitypackage",
            MakeUnityPackage(
                "Packages/com.triturbo.blendshare/package.json",
                "Packages/com.triturbo.blendshare/Editor/BlendShare.cs",
                "Assets/Kuuta_ShapekeyAddon/readme.txt"));

        Assert.Equal(
            ["Packages/com.triturbo.blendshare", "Assets/Kuuta_ShapekeyAddon"],
            UnityHandoff.ReadDestinations(package));
    }

    [Fact]
    public void 同じ場所のzipでも中身が変わったら読み直す()
    {
        // 読んだ結果は覚えておくが、差し替えた zip の古い一覧を返してはいけない
        var before = MakeZipWithPackage("Item/Item.unitypackage", MakeUnityPackage("Assets/Old/a.prefab"));
        Assert.Equal(["Assets/Old/a.prefab"], UnityHandoff.ReadAssetPaths(before));

        var after = MakeZipWithPackage(
            "Item/Item.unitypackage",
            MakeUnityPackage("Assets/New/a.prefab", "Assets/New/Textures/base.png"));

        Assert.Equal(["Assets/New/a.prefab", "Assets/New/Textures/base.png"], UnityHandoff.ReadAssetPaths(after));
    }

    [Fact]
    public void 壊れたunitypackageでは入る先を空で返す()
    {
        var package = MakeZipWithPackage("broken.unitypackage", Encoding.UTF8.GetBytes("tar.gzではない"));

        Assert.Empty(UnityHandoff.ReadDestinations(package));
    }

    [Fact]
    public void 入る先は最初の2段にまとめて多い順に並べる()
    {
        var roots = UnityHandoff.DestinationRoots(
        [
            "Assets/FUKA/a.cs",
            "Packages/com.x/package.json",
            "Assets/FUKA/b/c.prefab",
            "assets/fuka/d.png",
            // 1段だけのパスは入る先として読まない
            "Assets",
        ]);

        Assert.Equal(["Assets/FUKA", "Packages/com.x"], roots);
    }

    [Theory]
    [InlineData(new string[0], "")]
    [InlineData(new[] { "Assets/FUKA" }, "Assets/FUKA に入ります")]
    [InlineData(new[] { "Assets/A", "Packages/B" }, "Assets/A・Packages/B に入ります")]
    [InlineData(new[] { "Assets/A", "Assets/B", "Assets/C", "Assets/D", "Assets/E" }, "Assets/A・Assets/B・Assets/C ほか 2 か所に入ります")]
    public void 入る先を1文にする(string[] roots, string expected)
        => Assert.Equal(expected, UnityHandoff.DescribeDestinations(roots));

    [Theory]
    [InlineData("&Assets", "Assets")]
    [InlineData("Custom Package...", "Custom Package...")]
    [InlineData("&Refresh\tCtrl+R", "Refresh")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void メニューの項目名からキーの印とショートカットを落とす(string? text, string expected)
        => Assert.Equal(expected, UnityHandoff.NormalizeMenuText(text));

    [Theory]
    [InlineData("kip01 - SampleScene - Windows, Mac, Linux - Unity 2022.3.22f1 <DX11>", "kip01")]
    [InlineData("proj - Untitled - Windows, Mac, Linux - Unity 2022.3.22f1 <DX11>", "proj")]
    // 起動中・コンパイル中の題は作業の名前で、プロジェクト名ではない
    [InlineData("Compiling Scripts", null)]
    [InlineData("Reloading Domain", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void 窓のタイトルからプロジェクト名を取る(string? title, string? expected)
        => Assert.Equal(expected, UnityHandoff.ProjectNameFromWindowTitle(title));

    [Theory]
    [InlineData(
        @"""C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe"" -projectPath ""D:\work\vrchat\VRChatProjects\kip01"" -accept-apiupdate",
        @"D:\work\vrchat\VRChatProjects\kip01")]
    [InlineData(@"Unity.exe -projectPath C:\proj -nographics", @"C:\proj")]
    [InlineData(@"Unity.exe -projectPath C:\proj", @"C:\proj")]
    [InlineData("Unity.exe -batchmode", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void 起動引数からプロジェクトのパスを取る(string? commandLine, string? expected)
        => Assert.Equal(expected, UnityHandoff.ProjectPathFromCommandLine(commandLine));
}
