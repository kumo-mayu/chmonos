using System.IO.Compression;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>作り物のバイト列だけを使う。</summary>
public sealed class UnityProjectImagesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("chmonos-images-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Project => Path.Combine(_root, "project");

    private string Put(string relative, byte[] content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] Bytes(byte seed, int length = 32) => Enumerable.Repeat(seed, length).ToArray();

    private static uint CrcOf(byte[] content)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = zip.CreateEntry("x").Open();
            stream.Write(content);
        }

        memory.Position = 0;
        using var read = new ZipArchive(memory, ZipArchiveMode.Read);
        return read.Entries[0].Crc32;
    }

    private IReadOnlyList<UnityImageMatch> MatchOf(params UnityItemImage[] images)
        => UnityProjectImages.Match(
            UnityProjectImages.ListProject(Project),
            new Dictionary<string, IReadOnlyList<UnityItemImage>> { ["item"] = images },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)).GetValueOrDefault("item") ?? [];

    [Fact]
    public void 名前を変えても_中身が同じなら同じ画像()
    {
        var content = Bytes(1);
        Put(@"project\Assets\手で置いた\renamed.png", content);

        var match = Assert.Single(MatchOf(new UnityItemImage("original.png", content.Length, CrcOf(content))));

        Assert.Equal(UnityImageMatchKind.SameImage, match.Kind);
        Assert.Equal("Assets/手で置いた/renamed.png", match.ProjectPath);
    }

    [Fact]
    public void 同じ大きさでも中身が違えば_当たらない()
    {
        var content = Bytes(1);
        Put(@"project\Assets\other.png", Bytes(2));

        Assert.Empty(MatchOf(new UnityItemImage("original.png", content.Length, CrcOf(content))));
    }

    [Fact]
    public void 名前だけ同じなら_名前が同じと言う()
    {
        Put(@"project\Assets\body.PNG", Bytes(2, 40));

        var match = Assert.Single(MatchOf(new UnityItemImage("body.png", 32, CrcOf(Bytes(1)))));

        Assert.Equal(UnityImageMatchKind.SameName, match.Kind);
    }

    [Fact]
    public void 中身で見つかった画像には_名前だけの当たりを重ねない()
    {
        var content = Bytes(1);
        Put(@"project\Assets\a\body.png", content);
        Put(@"project\Assets\b\body.png", Bytes(2, 40));

        var match = Assert.Single(MatchOf(new UnityItemImage("body.png", content.Length, CrcOf(content))));

        Assert.Equal("Assets/a/body.png", match.ProjectPath);
    }

    [Fact]
    public void 単体の画像は記録のハッシュで照らす()
    {
        var content = Bytes(3);
        Put(@"project\Assets\x.jpg", content);
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));

        Assert.Equal(UnityImageMatchKind.SameImage, Assert.Single(MatchOf(new UnityItemImage("元.jpg", content.Length, Sha256: sha))).Kind);
    }

    [Fact]
    public void 登録したフォルダの画像はその場の中身で照らす()
    {
        var content = Bytes(4);
        Put(@"project\Assets\x.tga", content);
        var folder = Path.GetDirectoryName(Put(@"folder\sub\y.tga", content))!;

        var images = UnityProjectImages.ImagesInFolder(Path.GetDirectoryName(folder)!).ToArray();

        Assert.Equal(UnityImageMatchKind.SameImage, Assert.Single(MatchOf(images)).Kind);
    }

    [Fact]
    public void 同じ画像は_どちらの商品にも出る()
    {
        var content = Bytes(5);
        Put(@"project\Assets\x.png", content);
        var image = new UnityItemImage("x.png", content.Length, CrcOf(content));

        var found = UnityProjectImages.Match(
            UnityProjectImages.ListProject(Project),
            new Dictionary<string, IReadOnlyList<UnityItemImage>> { ["a"] = [image], ["b"] = [image] },
            new HashSet<string>());

        Assert.Equal(2, found.Count);
    }

    [Fact]
    public void unitypackageで入ったファイルは_名前だけでは当てない()
    {
        Put(@"project\Assets\pack\body.png", Bytes(2, 40));

        var found = UnityProjectImages.Match(
            UnityProjectImages.ListProject(Project),
            new Dictionary<string, IReadOnlyList<UnityItemImage>> { ["item"] = [new UnityItemImage("body.png", null)] },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Assets/pack/body.png" });

        Assert.Empty(found);
    }

    [Fact]
    public void Assetsの外と画像でない物は見ない()
    {
        Put(@"project\Library\a.png", Bytes(1));
        Put(@"project\Assets\a.fbx", Bytes(1));
        Put(@"project\Assets\a.png.meta", Bytes(1));

        Assert.Empty(UnityProjectImages.ListProject(Project));
    }

    private LocalFileRecord ZipOf(params (string Name, byte[] Content)[] entries)
    {
        var zip = Path.Combine(_root, "配布.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(content);
            }
        }

        return new LocalFileRecord
        {
            Hash = "ABCDEF0123456789",
            Paths = [zip],
            SizeBytes = new FileInfo(zip).Length,
            Contents = entries.Select(entry => entry.Name).ToList(),
        };
    }

    [Fact]
    public void zipの目次を控え_zipが無くなっても同じ画像と言える()
    {
        var content = Bytes(6);
        Put(@"project\Assets\renamed.png", content);
        var paths = new AppPaths(Path.Combine(_root, "store"));
        paths.EnsureCreated();
        var store = new ZipImageStore(paths);
        var file = ZipOf(("tex/original.png", content), ("説明.txt", Bytes(7)));

        var first = UnityProjectImages.ImagesOf(file, store).ToArray();
        File.Delete(file.Paths[0]);
        var second = UnityProjectImages.ImagesOf(file, store).ToArray();

        Assert.Equal(first, second);
        Assert.Equal(UnityImageMatchKind.SameImage, Assert.Single(MatchOf(second)).Kind);
    }

    [Fact]
    public void 手で直した控えの欠けた行は_読まない()
    {
        var paths = new AppPaths(Path.Combine(_root, "store"));
        paths.EnsureCreated();
        const string hash = "ABCDEF0123456789";
        Directory.CreateDirectory(paths.ZipImagesDir);
        File.WriteAllText(paths.ZipImageFile(hash), """{"images":[null,{"path":"tex/a.png","size":3,"crc":"0000000A"}]}""");

        var row = Assert.Single(new ZipImageStore(paths).Load(hash)!);

        Assert.Equal("tex/a.png", row.Path);
    }

    [Fact]
    public void 取り消すと_列挙も照らし合わせも途中で止まる()
    {
        var content = Bytes(9);
        Put(@"project\Assets\a.png", content);
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
        using var cancel = new CancellationTokenSource();
        var images = UnityProjectImages.ListProject(Project);
        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() => UnityProjectImages.ListProject(Project, cancel.Token));
        Assert.Throws<OperationCanceledException>(() => UnityProjectImages.ImagesInFolder(Project, cancel.Token).ToList());
        Assert.Throws<OperationCanceledException>(() => UnityProjectImages.Match(
            images,
            new Dictionary<string, IReadOnlyList<UnityItemImage>> { ["item"] = [new UnityItemImage("a.png", content.Length, Sha256: sha)] },
            new HashSet<string>(),
            cancel.Token));
    }

    [Fact]
    public void zipも控えも無ければ_名前だけで照らす()
    {
        Put(@"project\Assets\original.png", Bytes(8));
        var paths = new AppPaths(Path.Combine(_root, "store"));
        paths.EnsureCreated();
        var file = new LocalFileRecord { Hash = "ABCDEF0123456789", Paths = [Path.Combine(_root, "無い.zip")], SizeBytes = 1, Contents = ["tex/original.png"] };

        var images = UnityProjectImages.ImagesOf(file, new ZipImageStore(paths)).ToArray();

        Assert.Equal(UnityImageMatchKind.SameName, Assert.Single(MatchOf(images)).Kind);
    }

    [Fact]
    public async Task 取り込みの始めに_今ある商品の画像のzipも読み残しに入る()
    {
        var paths = new AppPaths(Path.Combine(_root, "store"));
        paths.EnsureCreated();
        var store = new DataStore(paths);
        var images = new ZipImageStore(paths);
        var file = ZipOf(("tex/a.png", Bytes(9)));
        await store.Items.SaveAsync(new ItemRecord
        {
            Id = "9900701",
            Local = new LocalBlock { LocalFiles = [file] },
        });
        var catalog = new UnityPackageCatalog(store, new UnityPackagePathStore(paths), images);

        var pending = await catalog.FindPendingAsync();
        Assert.Equal(file.Hash, Assert.Single(pending.Files).Hash);

        await catalog.ReadAsync(pending.Files);
        Assert.True(images.Has(file.Hash));
        Assert.Empty((await catalog.FindPendingAsync()).Files);
    }
}
