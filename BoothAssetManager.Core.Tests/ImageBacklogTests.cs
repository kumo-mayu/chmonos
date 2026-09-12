using System.Net;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 前の取り込みで残った画像を、次の起動で取り直す（梯子の⑤の再開）。
///
/// **自動で始めてよいのは、対象が手元のJSONだけで決まるから。**
/// フォルダの走査は起きないので、「ユーザが指示していない読み取り」にならない。
/// </summary>
public class ImageBacklogTests : IDisposable
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly ImagePipeline _images;
    private readonly ImageBacklog _backlog;

    private readonly List<string> _requests = [];

    public ImageBacklogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-backlog-" + Guid.NewGuid().ToString("N"));
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        var settings = new AppSettings { FetchIntervalMs = 0 };
        var client = new BoothClient(new HttpClient(new ImageHandler(_requests)), settings);

        _images = new ImagePipeline(client, _paths, settings);
        _backlog = new ImageBacklog(_store, _images);
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

    /// <summary>その場で呼ぶ <see cref="IProgress{T}"/>。試験を時間に依存させないため。</summary>
    private sealed class InlineProgress(Action<(int Done, int Total)> report) : IProgress<(int Done, int Total)>
    {
        public void Report((int Done, int Total) value) => report(value);
    }

    private sealed class ImageHandler(List<string> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (requests)
            {
                requests.Add(request.RequestUri!.ToString());
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(TinyPng),
            });
        }
    }

    private async Task SaveItemAsync(string id, int imageCount)
    {
        var images = Enumerable.Range(1, imageCount)
            .Select(index => new BoothImage { OriginalUrl = $"https://booth.pximg.net/a/i/{id}/{index}.jpg" })
            .ToList();

        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = $"商品 {id}", FetchedAt = DateTimeOffset.Now, Images = images },
            Local = new LocalBlock(),
        });
    }

    /// <summary>1枚だけ落とした状態を作る（取り込みが④まで進んで止まった形）。</summary>
    private async Task DownloadFirstAsync(string id)
    {
        var item = await _store.Items.LoadAsync(id);
        await _images.SyncOneAsync(id, item!.Booth.Images[0]);
        _requests.Clear();
    }

    /// <summary>揃っていない商品だけを対象にする。全部あるものは触らない。</summary>
    [Fact]
    public async Task FindsOnlyTheItemsThatAreStillMissingImages()
    {
        await SaveItemAsync("111", 3);
        await SaveItemAsync("222", 2);
        await DownloadFirstAsync("111");

        // 222 は全部落としておく
        var complete = await _store.Items.LoadAsync("222");
        await _images.SyncAsync("222", complete!.Booth.Images);

        var pending = await _backlog.FindPendingAsync();

        Assert.Equal(["111"], pending);
    }

    /// <summary>画像を持たない商品は対象にならない。</summary>
    [Fact]
    public async Task IgnoresItemsThatHaveNoImagesAtAll()
    {
        await SaveItemAsync("111", 0);

        Assert.Empty(await _backlog.FindPendingAsync());
    }

    /// <summary>**⑤の再開。**残りだけを取りに行き、既にあるものは取り直さない。</summary>
    [Fact]
    public async Task FetchesOnlyTheImagesThatAreNotOnDiskYet()
    {
        await SaveItemAsync("111", 3);
        await DownloadFirstAsync("111");

        var downloaded = await _backlog.ResumeAsync();

        Assert.Equal(2, downloaded);
        Assert.Equal(2, _requests.Count);
        Assert.DoesNotContain(_requests, url => url.EndsWith("/1.jpg", StringComparison.Ordinal));

        // もう残っていない
        Assert.Empty(await _backlog.FindPendingAsync());
    }

    /// <summary>取るものが無ければ、通信を1本も出さない。</summary>
    [Fact]
    public async Task StaysSilentWhenNothingIsMissing()
    {
        await SaveItemAsync("111", 1);
        var item = await _store.Items.LoadAsync("111");
        await _images.SyncAsync("111", item!.Booth.Images);
        _requests.Clear();

        Assert.Equal(0, await _backlog.ResumeAsync());
        Assert.Empty(_requests);
    }

    /// <summary>途中で止めても、そこまでに落としたものは残る。</summary>
    [Fact]
    public async Task KeepsWhatItAlreadyDownloadedWhenCancelled()
    {
        await SaveItemAsync("111", 2);
        await SaveItemAsync("222", 2);

        using var cancellation = new CancellationTokenSource();

        // Progress<T> は別スレッドへ投げるので、止まる位置が実行の速さで変わる。試験は時間に依存させない
        // 始めに「0件目」も知らせるので、1商品ぶん終わった知らせで止める
        var progress = new InlineProgress(report =>
        {
            if (report.Done > 0)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _backlog.ResumeAsync(progress, cancellation.Token));

        // 1商品ぶんは終わっている。次の起動で残りから続く
        Assert.Single(await _backlog.FindPendingAsync());
    }
}
