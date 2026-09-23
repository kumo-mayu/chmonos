using System.Net;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 商品を外すときと、外した商品の画像。
/// 前は JSON を先に消していたので、画像フォルダが消せないと「商品は無いのに画像だけ残る」状態になり、
/// 外した後に回ってきた画像の取得がフォルダを作り直してもいた。
/// </summary>
public sealed class ItemDeleteTests : IDisposable
{
    private const string ItemId = "777";

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-delete-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly ImagePipeline _images;

    public ItemDeleteTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        var settings = new AppSettings { FetchIntervalMs = 0 };
        var client = new BoothClient(new HttpClient(new PngHandler()), settings, (_, _) => Task.CompletedTask);
        _images = new ImagePipeline(client, _paths, settings);
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

    private sealed class PngHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TinyPng) });
    }

    private Task SaveItemAsync()
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.UtcNow },
            Local = new LocalBlock(),
        });

    private static BoothImage Image(string name) => new() { OriginalUrl = $"https://booth.pximg.net/{name}.png" };

    /// <summary>画像フォルダが消せなければ、商品は残る（もう一度外せば済む）。</summary>
    [Fact]
    public async Task KeepsTheItemWhenItsImagesCannotBeDeleted()
    {
        await SaveItemAsync();
        await _images.SyncAsync(ItemId, [Image("a")]);

        var directory = _paths.ItemImagesDir(ItemId);
        var file = Directory.EnumerateFiles(directory, "*.webp").Single();

        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => _store.Items.Delete(ItemId));
        }

        Assert.True(_store.Items.Exists(ItemId));

        _store.Items.Delete(ItemId);
        Assert.False(_store.Items.Exists(ItemId));
        Assert.False(Directory.Exists(directory));
    }

    /// <summary>外した後に回ってきた画像の取得は、画像フォルダを作り直さない。</summary>
    [Fact]
    public async Task DoesNotRecreateImagesOfARemovedItem()
    {
        await SaveItemAsync();
        _store.Items.Delete(ItemId);

        var result = await _images.SyncAsync(ItemId, [Image("a")]);
        var present = await _images.SyncOneAsync(ItemId, Image("b"));

        Assert.Equal(0, result.Downloaded);
        Assert.False(present);
        Assert.False(Directory.Exists(_paths.ItemImagesDir(ItemId)));
    }

    /// <summary>画像の保存に失敗したら一時ファイルを消す（前は固定の名前で、失敗すると残り続けた）。</summary>
    [Fact]
    public async Task RemovesTheTemporaryFileWhenSavingAnImageFails()
    {
        await SaveItemAsync();
        var directory = _paths.ItemImagesDir(ItemId);

        // 置く先の名前がフォルダで塞がっているので、最後の置き換えで必ず失敗する
        Directory.CreateDirectory(Path.Combine(directory, UserImageName.For(TinyPng)));

        await Assert.ThrowsAnyAsync<Exception>(() => _images.SaveUserImageAsync(ItemId, TinyPng));

        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }
}
