using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 改変に貼る画像。商品の画像と**同じ圧縮を通し**、置き場所だけが違う
/// （<c>images/_mods/{改変ID}/</c>）。
/// </summary>
public sealed class ModificationImageTests : IDisposable
{
    private const string AvatarId = "7841391";

    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly ModificationService _service;

    public ModificationImageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-modimg-" + Guid.NewGuid().ToString("N"));
        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        var settings = new AppSettings { FetchIntervalMs = 0 };
        var client = new BoothClient(new HttpClient(new Unreachable()), settings);
        _service = new ModificationService(_store, new ImagePipeline(client, _paths, settings));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("改変に画像を貼るのにBOOTHへは行かないはず");
    }

    /// <summary>色だけ違う小さなPNG。中身が違えば別の画像として扱われる。</summary>
    private static byte[] MakePng(byte red)
    {
        using var image = new Image<Rgba32>(8, 8, new Rgba32(red, 100, 100));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private async Task<string> NewAsync()
    {
        await _store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = AvatarId }],
        });

        return (await _service.CreateAsync(AvatarId, "普段着"))!.Id;
    }

    [Fact]
    public async Task 貼ると記録とファイルの両方ができる()
    {
        var id = await NewAsync();

        var fileName = await _service.AddImageAsync(id, MakePng(200));

        Assert.NotNull(fileName);
        Assert.StartsWith("user-", fileName);
        Assert.EndsWith(".webp", fileName);

        var record = await _service.LoadAsync(id);
        Assert.Equal(fileName, Assert.Single(record!.Images).FileName);

        Assert.True(File.Exists(Path.Combine(_paths.ModificationImagesDir(id), fileName)));
    }

    [Fact]
    public async Task 置き場所は商品IDと衝突しない()
    {
        var id = await NewAsync();
        await _service.AddImageAsync(id, MakePng(200));

        // images/ 直下は商品IDのフォルダが並ぶ場所。_mods を挟んで避ける
        Assert.Contains(Path.Combine("images", "_mods"), _paths.ModificationImagesDir(id));
        Assert.False(Directory.Exists(_paths.ItemImagesDir(id)));
    }

    [Fact]
    public async Task 何枚でも貼れる()
    {
        // 1つの改変には正面・背面・表情差分と何枚も撮る
        var id = await NewAsync();

        foreach (var red in new byte[] { 10, 60, 110, 160, 210 })
        {
            await _service.AddImageAsync(id, MakePng(red));
        }

        Assert.Equal(5, (await _service.LoadAsync(id))!.Images.Count);
    }

    [Fact]
    public async Task 同じ絵を2回貼っても1枚にまとまる()
    {
        // 名前が中身のハッシュなので同じファイルになる。記録も増やさない
        var id = await NewAsync();
        var bytes = MakePng(200);

        var first = await _service.AddImageAsync(id, bytes);
        var second = await _service.AddImageAsync(id, bytes);

        Assert.Equal(first, second);
        Assert.Single((await _service.LoadAsync(id))!.Images);
    }

    [Fact]
    public async Task 画像として読めなければ足さない()
    {
        var id = await NewAsync();

        Assert.Null(await _service.AddImageAsync(id, [1, 2, 3, 4]));
        Assert.Empty((await _service.LoadAsync(id))!.Images);
    }

    [Fact]
    public async Task 無い改変には貼れない()
        => Assert.Null(await _service.AddImageAsync("mod-00000000", MakePng(200)));

    [Fact]
    public async Task 消すと記録とファイルの両方が消える()
    {
        var id = await NewAsync();
        var fileName = await _service.AddImageAsync(id, MakePng(200));

        Assert.True(await _service.RemoveImageAsync(id, fileName!));

        Assert.Empty((await _service.LoadAsync(id))!.Images);
        Assert.False(File.Exists(Path.Combine(_paths.ModificationImagesDir(id), fileName!)));
    }

    [Fact]
    public async Task 並べ替えられる()
    {
        var id = await NewAsync();
        var first = await _service.AddImageAsync(id, MakePng(10));
        var second = await _service.AddImageAsync(id, MakePng(200));

        await _service.MoveImageAsync(id, second!, -1);

        Assert.Equal(
            [second, first],
            (await _service.LoadAsync(id))!.Images.Select(image => image.FileName));
    }

    [Fact]
    public async Task 端では動かない()
    {
        var id = await NewAsync();
        var first = await _service.AddImageAsync(id, MakePng(10));
        var second = await _service.AddImageAsync(id, MakePng(200));

        await _service.MoveImageAsync(id, first!, -1);
        await _service.MoveImageAsync(id, second!, 1);

        Assert.Equal(
            [first, second],
            (await _service.LoadAsync(id))!.Images.Select(image => image.FileName));
    }

    [Fact]
    public async Task 改変を消すと画像のフォルダごと消える()
    {
        var id = await NewAsync();
        await _service.AddImageAsync(id, MakePng(200));
        var dir = _paths.ModificationImagesDir(id);
        Assert.True(Directory.Exists(dir));

        await _service.DeleteAsync(id);

        Assert.False(Directory.Exists(dir));
    }
}
