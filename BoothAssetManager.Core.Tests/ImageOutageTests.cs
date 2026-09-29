using System.Net;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 画像の段（取り込みの④⑤⑥・起動時の⑤）の打ち切り（ユーザ判断 2026-09-29）。
///
/// つながっていない・BOOTH が 5xx を返し続けるときに、1枚ごとに再試行で長く待ちながら全部を回っていた。
/// **届かない失敗が3件続いたら、その回の残りは取りに行かない。**取らなかった絵は印を置かないので、
/// 「まだ取っていない」扱いのまま次の機会に取る。
/// 通信はしない（作り物の応答）。再試行の待ちは差し替えて待たない。
/// </summary>
public class ImageOutageTests : IDisposable
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public enum Answer
    {
        Ok,

        /// <summary>接続できない（ネットにつながっていない）。</summary>
        Unreachable,

        /// <summary>503（BOOTH の不調）。</summary>
        ServerDown,

        /// <summary>429（混雑）。BOOTH は応答している。</summary>
        Busy,
    }

    /// <summary>1件あたりの問い合わせの数（最初の1本と、間を空けた再試行2本）。</summary>
    private const int AttemptsPerImage = 3;

    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly ImagePipeline _images;
    private readonly ImageBacklog _backlog;
    private readonly ImportPipeline _pipeline;

    /// <summary>n 番目に問い合わせた画像（URLの初出順）への答え。足りない分は <see cref="_otherImages"/>。</summary>
    private readonly List<Answer> _imageAnswers = [];

    private Answer _otherImages = Answer.Ok;

    /// <summary>商品JSONに載せる画像の枚数。</summary>
    private int _imagesPerItem = 2;

    private readonly List<string> _imageAsked = [];
    private int _imageRequests;

    public ImageOutageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-image-outage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = true };
        var client = new BoothClient(
            new HttpClient(new Handler(this)),
            settings,
            delay: (_, _) => Task.CompletedTask);

        _images = new ImagePipeline(client, _paths, settings);
        _backlog = new ImageBacklog(_store, _images);
        _pipeline = new ImportPipeline(_store, client, _images, settings);
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

    private sealed class Handler(ImageOutageTests owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();

            if (url.StartsWith("https://booth.pm/", StringComparison.Ordinal))
            {
                var isJson = url.EndsWith(".json", StringComparison.Ordinal);
                var id = url.Split('/')[^1].Replace(".json", string.Empty, StringComparison.Ordinal);
                var images = string.Join(
                    ", ",
                    Enumerable.Range(1, owner._imagesPerItem)
                        .Select(index => $$"""{ "original": "{{ImageUrl(id, index)}}" }"""));

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(isJson
                        ? $$"""
                            { "id": {{id}}, "name": "商品 {{id}}",
                              "url": "https://booth.pm/ja/items/{{id}}",
                              "images": [{{images}}], "variations": [] }
                            """
                        : "<html><body></body></html>"),
                });
            }

            Answer answer;
            lock (owner)
            {
                owner._imageRequests++;
                if (!owner._imageAsked.Contains(url))
                {
                    owner._imageAsked.Add(url);
                }

                var index = owner._imageAsked.IndexOf(url);
                answer = index < owner._imageAnswers.Count ? owner._imageAnswers[index] : owner._otherImages;
            }

            return answer switch
            {
                Answer.Unreachable => throw new HttpRequestException("つながらない"),
                Answer.ServerDown => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
                Answer.Busy => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)),
                _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TinyPng) }),
            };
        }
    }

    private static string ImageUrl(string itemId, int index) => $"https://booth.pximg.net/a/i/{itemId}/{index}.jpg";

    private async Task SaveItemsAsync(params string[] ids)
    {
        foreach (var id in ids)
        {
            await _store.Items.SaveAsync(new ItemRecord
            {
                Id = id,
                Booth = new BoothBlock
                {
                    Name = $"商品 {id}",
                    FetchedAt = DateTimeOffset.Now,
                    Images = Enumerable.Range(1, _imagesPerItem)
                        .Select(index => new BoothImage { OriginalUrl = ImageUrl(id, index) })
                        .ToList(),
                },
                Local = new LocalBlock(),
            });
        }
    }

    private string CreateSource(params string[] itemIds)
    {
        var folder = Path.Combine(_root, "source");
        Directory.CreateDirectory(folder);

        foreach (var itemId in itemIds)
        {
            var path = Path.Combine(folder, $"item_{itemId}.zip");
            File.WriteAllText(path, itemId);
            File.WriteAllText(
                path + ":Zone.Identifier",
                $"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://booth.pm/ja/items/{itemId}\r\n");
        }

        return folder;
    }

    /// <summary>画像のフォルダに置かれた印（取れなかった・しばらく休む）の数。</summary>
    private int CountMarkers(string itemId)
    {
        var directory = _paths.ItemImagesDir(itemId);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.missing").Count() + Directory.EnumerateFiles(directory, "*.retry").Count()
            : 0;
    }

    // ── 起動時の⑤（前の取り込みで残った画像） ──

    [Theory]
    [InlineData(Answer.ServerDown)]
    [InlineData(Answer.Unreachable)]
    public async Task BacklogStopsAfterThreeFailuresAndLeavesTheRestPending(Answer failure)
    {
        _otherImages = failure;
        await SaveItemsAsync("111", "222", "333", "444", "555");

        Assert.Equal(0, await _backlog.ResumeAsync());

        // 1枚目の段の3件目で打ち切り、4件目以降も残り（2枚目）も問い合わせない
        Assert.Equal(3, _imageAsked.Count);
        Assert.Equal(3 * AttemptsPerImage, _imageRequests);

        // 印を置かないので、全部がまだ取っていない扱いのまま（次の起動でまた対象になる）
        Assert.Equal(5, (await _backlog.FindPendingAsync()).Count);
        foreach (var id in new[] { "111", "222", "333", "444", "555" })
        {
            Assert.Equal(0, CountMarkers(id));
        }
    }

    /// <summary>「続けて」なので、途中で1枚取れたら数え直す。</summary>
    [Fact]
    public async Task BacklogCountsAgainAfterAnImageIsFetched()
    {
        _imagesPerItem = 1;
        _imageAnswers.AddRange([Answer.ServerDown, Answer.ServerDown, Answer.Ok, Answer.ServerDown, Answer.ServerDown]);
        await SaveItemsAsync("111", "222", "333", "444", "555");

        await _backlog.ResumeAsync();

        // 数え直さないと4件目で打ち切り、5件目を問い合わせない
        Assert.Equal(5, _imageAsked.Count);
        Assert.True(File.Exists(_images.FilePathFor("333", ImageUrl("333", 1))));
    }

    /// <summary>429（混雑）は BOOTH が応答しているので数えない（数え直す）。</summary>
    [Fact]
    public async Task BacklogDoesNotCountBusyAnswers()
    {
        _imagesPerItem = 1;
        _otherImages = Answer.Busy;
        await SaveItemsAsync("111", "222", "333", "444");

        await _backlog.ResumeAsync();

        Assert.Equal(4, _imageAsked.Count);
    }

    // ── 1商品の残りの画像（⑤） ──

    /// <summary>
    /// 1商品の中でも打ち切る。問い合わせた3枚は今までどおり「しばらく休む」の印を置き、
    /// 問い合わせなかった残りには置かない（つながった後の次の機会に取れるように）。
    /// </summary>
    [Fact]
    public async Task GalleryStopsInsideOneItem()
    {
        _imagesPerItem = 5;
        _otherImages = Answer.ServerDown;
        await SaveItemsAsync("111");
        var item = await _store.Items.LoadAsync("111");
        var outage = new BoothOutageWatch();

        var result = await _images.SyncAsync("111", item!.Booth.Images, outage);

        Assert.Equal(BoothOutageKind.ServerDown, outage.Stopped);
        Assert.Equal(3, _imageAsked.Count);
        Assert.Equal(3, result.Failed);
        Assert.Equal(3, CountMarkers("111"));
        Assert.False(_images.IsSettled("111", ImageUrl("111", 4)));
        Assert.False(_images.IsSettled("111", ImageUrl("111", 5)));
    }

    // ── 取り込みの④⑤⑥ ──

    /// <summary>
    /// 取り込みの画像の段も3件で打ち切る。①②は取れているので取り込み自体は成立し、「続きから」は残さない
    /// （画像は起動時の⑤か「足りない情報を取得」で取れる）。
    /// </summary>
    [Fact]
    public async Task ImportStopsTheImageStageAfterThreeFailures()
    {
        _otherImages = Answer.ServerDown;
        var source = CreateSource("111", "222", "333", "444", "555");

        var summary = await _pipeline.RunAsync([source]);

        Assert.Equal(5, summary.ItemsAdded);
        Assert.Equal(BoothOutageKind.None, summary.Stopped);
        Assert.Equal(3, _imageAsked.Count);
        Assert.Equal(3 * AttemptsPerImage, _imageRequests);
        Assert.False(_store.ImportState.Load().HasProgress);
        Assert.Equal(5, (await _backlog.FindPendingAsync()).Count);
    }

    /// <summary>取り込みの画像の段も、途中で取れたら数え直す。</summary>
    [Fact]
    public async Task ImportImageStageCountsAgainAfterAnImageIsFetched()
    {
        _imagesPerItem = 1;
        _imageAnswers.AddRange([Answer.Unreachable, Answer.Unreachable, Answer.Ok, Answer.Unreachable, Answer.Unreachable]);
        var source = CreateSource("111", "222", "333", "444", "555");

        await _pipeline.RunAsync([source]);

        // 数え直さないと4件目で打ち切り、5件目の1枚目を問い合わせない
        Assert.Equal(5, _imageAsked.Count);
    }
}
