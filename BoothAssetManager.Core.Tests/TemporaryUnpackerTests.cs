using System.IO.Compression;
using System.Text;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// zip を一時フォルダへ展開する（#56）。unitypackage になっていない配布物を Unity へ入れるための逃げ道。
/// </summary>
public sealed class TemporaryUnpackerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bam-unpack-" + Guid.NewGuid().ToString("N")[..8]);

    private string Root => Path.Combine(_dir, "unpacked");

    public TemporaryUnpackerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string MakeZip(string name, params (string Path, string Text)[] entries)
    {
        var path = System.IO.Path.Combine(_dir, name);
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);
        foreach (var (entryPath, text) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(entryPath).Open());
            writer.Write(text);
        }

        return path;
    }

    [Fact]
    public void 日本語の名前も含めて展開する()
    {
        var zip = MakeZip("テクスチャ集.zip", ("ふわもこ/目_01.png", "png"), ("readme.txt", "説明"));

        var folder = new TemporaryUnpacker(Root).Unpack(zip);

        Assert.StartsWith(Root, folder);
        Assert.Equal("png", File.ReadAllText(Path.Combine(folder, "ふわもこ", "目_01.png")));
        Assert.Equal("説明", File.ReadAllText(Path.Combine(folder, "readme.txt")));
    }

    [Fact]
    public void 置き場所の外へ出る名前は書き出さない()
    {
        var zip = MakeZip("slip.zip", ("../../outside.txt", "外"), ("inside.txt", "中"));

        var folder = new TemporaryUnpacker(Root).Unpack(zip);

        Assert.True(File.Exists(Path.Combine(folder, "inside.txt")));
        Assert.False(File.Exists(Path.Combine(_dir, "outside.txt")));
        Assert.False(File.Exists(Path.Combine(Root, "outside.txt")));
    }

    [Fact]
    public void 同じzipは展開し直さない()
    {
        var zip = MakeZip("same.zip", ("a.txt", "元"));
        var unpacker = new TemporaryUnpacker(Root);

        var first = unpacker.Unpack(zip);
        File.WriteAllText(Path.Combine(first, "a.txt"), "展開した後に触った");
        var second = unpacker.Unpack(zip);

        Assert.Equal(first, second);
        Assert.Equal("展開した後に触った", File.ReadAllText(Path.Combine(second, "a.txt")));
    }

    [Fact]
    public void 片付けると置き場所ごと消える()
    {
        var unpacker = new TemporaryUnpacker(Root);
        unpacker.Unpack(MakeZip("gone.zip", ("a.txt", "a")));

        Assert.True(unpacker.CleanUp());
        Assert.False(Directory.Exists(Root));
    }

    [Fact]
    public void zipの中の1ファイルだけを取り出す()
    {
        // Unity の「Custom Package...」には実在するパスを渡す（#69）
        var zip = MakeZip("pack.zip", ("中/Sig_Ring.unitypackage", "tar.gz"), ("readme.txt", "説明"));
        var unpacker = new TemporaryUnpacker(Root);

        var path = unpacker.ExtractEntry(zip, "中/Sig_Ring.unitypackage");

        Assert.Equal("Sig_Ring.unitypackage", Path.GetFileName(path));
        Assert.Equal("tar.gz", File.ReadAllText(path));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "readme.txt")));
        Assert.Equal(path, unpacker.ExtractEntry(zip, "中/Sig_Ring.unitypackage"));
    }

    [Fact]
    public void zipの中に無いファイルは投げる()
    {
        var zip = MakeZip("pack.zip", ("a.txt", "a"));

        Assert.Throws<FileNotFoundException>(() => new TemporaryUnpacker(Root).ExtractEntry(zip, "無い.unitypackage"));
    }

    [Fact]
    public void 無いzipでは投げる()
        => Assert.Throws<FileNotFoundException>(() => new TemporaryUnpacker(Root).Unpack(Path.Combine(_dir, "無い.zip")));

    [Fact]
    public void 既定の置き場所の中かを見分ける()
    {
        Assert.True(TemporaryUnpacker.IsInsideDefaultRoot(Path.Combine(TemporaryUnpacker.DefaultRoot, "x-1234", "a.png")));
        Assert.False(TemporaryUnpacker.IsInsideDefaultRoot(@"D:\dl\a.png"));
    }
}
