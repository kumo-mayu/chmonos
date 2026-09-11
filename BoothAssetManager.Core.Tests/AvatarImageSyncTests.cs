using System.Net;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// U18：持っていないアバターの1枚目を <c>images/_avatars</c> に取って置く。
/// 持っているアバターは商品の1枚目を使うので、控えの1枚は消す。画像を保存しない設定なら何もしない。
/// </summary>
public class AvatarImageSyncTests : IDisposable
{
    private const string AvatarId = "5001";

    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly List<string> _requests = [];

    public AvatarImageSyncTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-avatar-image-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);
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

        GC.SuppressFinalize(this);
    }

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private sealed class FakeBooth(AvatarImageSyncTests owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            lock (owner._requests)
            {
                owner._requests.Add(url);
            }

            if (url.Contains("booth.pximg.net", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TinyPng) });
            }

            var id = url.Split('/')[^1].Replace(".json", string.Empty, StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""
                    {
                      "id": {{id}},
                      "name": "アバター {{id}}",
                      "url": "https://booth.pm/ja/items/{{id}}",
                      "images": [
                        { "original": "https://booth.pximg.net/a/i/{{id}}/one.jpg" },
                        { "original": "https://booth.pximg.net/a/i/{{id}}/two.jpg" }
                      ],
                      "variations": [ { "id": 1, "name": null, "price": 100 } ]
                    }
                    """),
            });
        }
    }

    private AvatarImageSync Create(bool saveImages = true)
    {
        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = saveImages };
        var client = new BoothClient(new HttpClient(new FakeBooth(this)), settings);
        return new AvatarImageSync(_store, client, new ImagePipeline(client, _paths, settings));
    }

    private Task SaveRegistryAsync(string? imageUrl = null)
        => _store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = AvatarId, BoothName = "アバター", AvatarOverride = true, ImageUrl = imageUrl }],
        });

    private IReadOnlyList<string> SavedImages()
        => Directory.Exists(_paths.AvatarImagesDir(AvatarId))
            ? Directory.GetFiles(_paths.AvatarImagesDir(AvatarId), "*.webp")
            : [];

    [Fact]
    public async Task SavesOnlyTheFirstImageOfAnAvatarNotOwned()
    {
        await SaveRegistryAsync();

        var saved = await Create().SyncAsync();

        Assert.Equal(1, saved);
        Assert.Single(SavedImages());
        Assert.EndsWith("/one.jpg", _store.Avatars.Load().Entries.Single().ImageUrl);
        Assert.DoesNotContain(_requests, url => url.EndsWith("/two.jpg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UsesTheKnownUrlWithoutAskingAgain()
    {
        await SaveRegistryAsync("https://booth.pximg.net/a/i/5001/one.jpg");

        await Create().SyncAsync();

        Assert.Single(SavedImages());
        Assert.DoesNotContain(_requests, url => url.EndsWith(".json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DoesNotFetchAgainOnceSaved()
    {
        await SaveRegistryAsync();
        await Create().SyncAsync();
        lock (_requests)
        {
            _requests.Clear();
        }

        await Create().SyncAsync();

        Assert.Empty(_requests);
    }

    [Fact]
    public async Task DropsTheCopyOnceTheAvatarIsOwned()
    {
        await SaveRegistryAsync();
        await Create().SyncAsync();
        Assert.Single(SavedImages());

        // 後で買って取り込んだ
        File.WriteAllText(_paths.ItemFile(AvatarId), "{}");
        lock (_requests)
        {
            _requests.Clear();
        }

        await Create().SyncAsync();

        Assert.False(Directory.Exists(_paths.AvatarImagesDir(AvatarId)), "買った後も控えの1枚が残っている");
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task DoesNothingWhenImagesAreNotSaved()
    {
        await SaveRegistryAsync();

        var saved = await Create(saveImages: false).SyncAsync();

        Assert.Equal(0, saved);
        Assert.Empty(_requests);
        Assert.Empty(SavedImages());
    }
}
