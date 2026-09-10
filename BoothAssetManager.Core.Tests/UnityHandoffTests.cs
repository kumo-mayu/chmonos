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

    [Theory]
    [InlineData("kip01 - SampleScene - Windows, Mac, Linux - Unity 2022.3.22f1 <DX11>", "kip01")]
    [InlineData("proj - Untitled - Windows, Mac, Linux - Unity 2022.3.22f1 <DX11>", "proj")]
    [InlineData("kip01", "kip01")]
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
