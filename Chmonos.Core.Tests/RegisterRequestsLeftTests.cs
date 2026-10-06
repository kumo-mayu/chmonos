using System.Net;
using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 未確定で登録するとき、BOOTHへの問い合わせの残りの数を流す（メモ34）。
/// 画面はこの数に間隔を掛けて目安の時間を出す。**間隔や順番は変えず、数えて知らせるだけ。**
/// </summary>
public class RegisterRequestsLeftTests : IDisposable
{
    private const string ItemId = "9912345";

    private readonly string _root;
    private readonly DataStore _store;
    private readonly ItemService _service;

    public RegisterRequestsLeftTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-left-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);

        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = true };
        var client = new BoothClient(new HttpClient(new Handler()), settings, TestWait.None);
        _service = new ItemService(_store, client, new ImagePipeline(client, paths, settings), settings);
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

    /// <summary>画像3枚とショップのアイコンを持つ商品を返す。画像は読めない中身にして、取れなかった扱いで進める。</summary>
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (url.EndsWith(".json", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                        {
                          "id": {{ItemId}},
                          "name": "テスト商品",
                          "shop": { "name": "alpha", "subdomain": "alpha", "url": "https://alpha.booth.pm/", "thumbnail_url": "https://img.example/shop/c/48x48/a.png" },
                          "images": [
                            { "original": "https://img.example/1.jpg" },
                            { "original": "https://img.example/2.jpg" },
                            { "original": "https://img.example/3.jpg" }
                          ],
                          "variations": [ { "id": 900, "name": null, "price": 1000 } ]
                        }
                        """),
                });
            }

            return Task.FromResult(url.Contains("img.example", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html><body></body></html>") });
        }
    }

    private sealed class Recorder : IProgress<int>
    {
        public List<int> Values { get; } = [];

        public void Report(int value) => Values.Add(value);
    }

    private async Task AddUnresolvedAsync(string hash)
        => await _store.Unresolved.UpdateAsync(list =>
        {
            list.Add(new UnresolvedFile
            {
                Hash = hash,
                Paths = [$@"C:\dl\{hash}.zip"],
                SizeBytes = 100,
                ModifiedAtUtc = DateTimeOffset.Now,
                FirstSeenAt = DateTimeOffset.Now,
            });
            return list;
        });

    [Fact]
    public async Task ANewItemCountsDownJsonPageImagesAndTheShopIcon()
    {
        await AddUnresolvedAsync("aaa");
        var recorder = new Recorder();

        Assert.True(await _service.AssignItemIdAsync("aaa", ItemId, requestsLeft: recorder));

        // JSON と商品ページ（2）→ 商品ページだけ（1）→ 1枚目とアイコン（2）→ アイコンだけ（1）→ 0。
        // 残りの2枚は登録の後に⑤の段で取るので、登録の残りには入らない（メモ60 案B）
        Assert.Equal([2, 1, 2, 1, 0], recorder.Values);
    }

    [Fact]
    public async Task AnItemAlreadyHeldAsksNothingSoNothingIsReported()
    {
        await _store.Items.SaveAsync(new ItemRecord { Id = ItemId, Booth = new BoothBlock(), Local = new LocalBlock() });
        await AddUnresolvedAsync("aaa");
        var recorder = new Recorder();

        Assert.True(await _service.AssignItemIdAsync("aaa", ItemId, requestsLeft: recorder));

        Assert.Empty(recorder.Values);
    }
}
