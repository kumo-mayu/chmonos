using System.Net;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 404で取れなかった画像に置く印（<c>{ハッシュ}.missing</c>）。
///
/// 中身は要らない。**ファイルがあること自体が「これは取れなかった」を意味する。**
/// 記録を local に持たないのは、実態（ディスク）とフラグがずれたときに
/// どちらが正しいか分からなくなるため。印もファイルなら、実態の側にある。
/// </summary>
public class MissingImageMarkerTests : IDisposable
{
    private const string ItemId = "555";

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly ImagePipeline _images;

    /// <summary>404を返すURL。試験ごとに入れ替える。</summary>
    private readonly HashSet<string> _notFound = new(StringComparer.Ordinal);

    /// <summary>一時エラーを返すURL。</summary>
    private readonly HashSet<string> _flaky = new(StringComparer.Ordinal);

    private readonly List<string> _requests = [];

    public MissingImageMarkerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-missing-" + Guid.NewGuid().ToString("N"));
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        // 画像は在る商品にだけ保存する（外した商品の画像フォルダを作り直さない）ので、商品を置いておく
        _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.UtcNow },
            Local = new LocalBlock(),
        }).GetAwaiter().GetResult();

        // 再試行の待ちは実際には置かない。一時エラーの試験で10秒待つことになる
        var settings = new AppSettings { FetchIntervalMs = 0 };
        var client = new BoothClient(
            new HttpClient(new Handler(this)),
            settings,
            (_, _) => Task.CompletedTask);

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

        GC.SuppressFinalize(this);
    }

    private sealed class Handler(MissingImageMarkerTests owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();

            lock (owner._requests)
            {
                owner._requests.Add(url);
            }

            if (owner._notFound.Contains(url))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (owner._flaky.Contains(url))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(TinyPng),
            });
        }
    }

    private static string Url(int index) => $"https://booth.pximg.net/a/i/{ItemId}/{index}.jpg";

    private static IReadOnlyList<BoothImage> Images(params int[] indexes)
        => indexes.Select(index => new BoothImage { OriginalUrl = Url(index) }).ToList();

    private string Directory_ => _paths.ItemImagesDir(ItemId);

    private bool HasMarker(int index)
        => File.Exists(Path.Combine(Directory_, ImagePipeline.MissingMarkerFor(Url(index))));

    /// <summary>404だったら印を置く。取れた方は普通に残る。</summary>
    [Fact]
    public async Task LeavesAMarkerForAnImageThatCameBackNotFound()
    {
        _notFound.Add(Url(2));

        var result = await _images.SyncAsync(ItemId, Images(1, 2));

        Assert.Equal(1, result.Downloaded);
        Assert.Equal(1, result.Missing);
        Assert.False(HasMarker(1));
        Assert.True(HasMarker(2));
    }

    /// <summary>
    /// **一時エラーでは404の印を置かない**（商品が消えた証拠にならない）。
    /// ただし**しばらく休む印は置く**（ユーザ判断 2026-09-21・G6）——
    /// 印が無かったので、403・接続失敗・読めない画像を起動のたびに取り直していた。
    /// </summary>
    [Fact]
    public async Task RestsBeforeAskingAgainForAnImageThatFailedTemporarily()
    {
        _flaky.Add(Url(1));

        var result = await _images.SyncAsync(ItemId, Images(1));

        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Missing);
        Assert.False(HasMarker(1));

        // 休んでいる間は取りに行かない
        _requests.Clear();
        await _images.SyncAsync(ItemId, Images(1));
        Assert.Empty(_requests);
    }

    /// <summary>休みが明けたら、もう一度取りに行く（「もう無い」と決めつけない）。</summary>
    [Fact]
    public async Task AsksAgainOnceTheRestIsOver()
    {
        _flaky.Add(Url(1));
        await _images.SyncAsync(ItemId, Images(1));

        // 印の日時を8日前にする（休むのは7日）
        var marker = Path.Combine(
            _paths.ItemImagesDir(ItemId),
            Core.Images.ImagePipeline.RetryMarkerFor(Url(1)));
        File.SetLastWriteTimeUtc(marker, DateTime.UtcNow.AddDays(-8));

        _requests.Clear();
        await _images.SyncAsync(ItemId, Images(1));

        Assert.NotEmpty(_requests);
    }

    /// <summary>印の付いた画像は二度と取りに行かない。毎回1本ずつ無駄にしない。</summary>
    [Fact]
    public async Task StopsAskingForAnImageItHasAlreadyMarked()
    {
        _notFound.Add(Url(1));
        await _images.SyncAsync(ItemId, Images(1));

        _requests.Clear();
        var result = await _images.SyncAsync(ItemId, Images(1));

        Assert.Empty(_requests);
        Assert.Equal(1, result.Missing);
    }

    /// <summary>
    /// 新しく置いたら知らせる。知らせないと、開いている検索カードや商品ページは
    /// 起動し直すまで空のままだった（友人の報告）。
    /// 取れなかったとき・元から持っていたときは知らせない——描き直す理由が無い。
    /// </summary>
    [Fact]
    public async Task 画像を新しく置いたときだけ知らせる()
    {
        var saved = new List<string>();
        _images.ItemImagesSaved += saved.Add;
        _notFound.Add(Url(3));

        // 2枚落としても知らせは1回。画面は商品単位で組み直すので、枚数分知らせても同じ
        await _images.SyncAsync(ItemId, Images(1, 2, 3));
        Assert.Equal([ItemId], saved);

        await _images.SyncAsync(ItemId, Images(1, 2, 3));
        Assert.Single(saved);

        // 1枚だけ取る経路（④）でも知らせる
        Assert.True(await _images.SyncOneAsync(ItemId, Images(4)[0]));
        Assert.Equal(2, saved.Count);

        // 404だった物は知らせない
        Assert.False(await _images.SyncOneAsync(ItemId, Images(3)[0]));
        Assert.Equal(2, saved.Count);
    }

    /// <summary>1枚だけ取る経路（④）でも同じ。</summary>
    [Fact]
    public async Task HonoursTheMarkerWhenFetchingJustTheFirstImage()
    {
        _notFound.Add(Url(1));

        Assert.False(await _images.SyncOneAsync(ItemId, Images(1)[0]));
        Assert.True(HasMarker(1));

        _requests.Clear();
        Assert.False(await _images.SyncOneAsync(ItemId, Images(1)[0]));
        Assert.Empty(_requests);
    }

    /// <summary>
    /// **印を外すのは商品を取り直したときだけ。**
    /// 作者がたまたま非公開にしていただけ、という場合はこれで復活する。
    /// </summary>
    [Fact]
    public async Task ClearsEveryMarkerWhenTheItemIsRefetched()
    {
        _notFound.Add(Url(1));
        _notFound.Add(Url(2));
        await _images.SyncAsync(ItemId, Images(1, 2));

        Assert.Equal(2, _images.CountMissingMarkers(ItemId));
        Assert.Equal(2, _images.ClearMissingMarkers(ItemId));
        Assert.Equal(0, _images.CountMissingMarkers(ItemId));

        // 商品が戻っていれば、そのまま取れる
        _notFound.Clear();
        var result = await _images.SyncAsync(ItemId, Images(1, 2));
        Assert.Equal(2, result.Downloaded);
    }

    /// <summary>
    /// BOOTH側の一覧から消えたURLの印は、消すだけ。もう取りに行く先が無いので、
    /// 残すと「取れなかった画像がある」と数え続けることになる。
    /// </summary>
    [Fact]
    public async Task DropsMarkersForUrlsBoothNoLongerLists()
    {
        _notFound.Add(Url(1));
        _notFound.Add(Url(2));
        await _images.SyncAsync(ItemId, Images(1, 2));
        Assert.Equal(2, _images.CountMissingMarkers(ItemId));

        // 作者が2枚目を消した（一覧から居なくなった）
        await _images.SyncAsync(ItemId, Images(1));

        Assert.Equal(1, _images.CountMissingMarkers(ItemId));
        Assert.True(HasMarker(1));
        Assert.False(HasMarker(2));
    }

    /// <summary>
    /// 印の付いた画像は「決着済み」として数える。
    /// 数に入れないと、二度と取れないものを毎回の起動で対象に挙げ続ける。
    /// </summary>
    [Fact]
    public async Task StopsListingAnItemWhoseRemainingImagesAreAllUnavailable()
    {
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { Name = "商品", FetchedAt = DateTimeOffset.Now, Images = Images(1, 2) },
            Local = new LocalBlock(),
        });

        var backlog = new ImageBacklog(_store, _images);

        _notFound.Add(Url(2));
        await _images.SyncAsync(ItemId, Images(1, 2));

        // 1枚は手元、1枚は取れないと確定。もう挙げない
        Assert.Empty(await backlog.FindPendingAsync());
    }

    /// <summary>印はギャラリーに混ざらない。読み込みは *.webp しか拾わない。</summary>
    [Fact]
    public async Task KeepsMarkersOutOfTheImageListing()
    {
        _notFound.Add(Url(2));
        await _images.SyncAsync(ItemId, Images(1, 2));

        var webp = System.IO.Directory.EnumerateFiles(Directory_, "*.webp").ToList();

        Assert.Single(webp);
        Assert.DoesNotContain(webp, path => path.EndsWith(".missing", StringComparison.Ordinal));
    }
}
