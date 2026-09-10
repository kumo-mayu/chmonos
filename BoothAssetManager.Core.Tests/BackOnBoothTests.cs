using System.Net;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 非公開と見なしていた商品がBOOTHに戻ってきたときの往復。
///
/// 黙って埋めると、画像が急に増え、価格が入り、印が消える。
/// 説明が無いと「壊れた」と読まれる。
/// </summary>
public class BackOnBoothTests : IDisposable
{
    private const string ItemId = "5927710";

    private readonly string _root;
    private readonly DataStore _store;
    private readonly ItemService _service;

    public BackOnBoothTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-back-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);

        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var client = new BoothClient(new HttpClient(new Handler()), settings);
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

    /// <summary>BOOTHに戻っている状態を返す。</summary>
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.ToString().EndsWith(".json", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                        {
                          "id": {{ItemId}},
                          "name": "オリジナル3Dモデル『Bird/鳥』",
                          "shop": { "name": "alpha", "subdomain": "alpha", "url": "https://alpha.booth.pm/" },
                          "images": [],
                          "variations": [ { "id": 1, "name": null, "price": 100 } ]
                        }
                        """),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body></body></html>"),
            });
        }
    }

    private async Task SeedAsync(bool delisted, string? displayName = null, bool notifyOnUpdate = true)
    {
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now.AddDays(-40), Name = "とり" },
            Local = new LocalBlock
            {
                DisplayName = displayName,
                IsDelisted = delisted,
                ConsecutiveNotFoundCount = delisted ? 3 : 0,
                NotifyOnUpdate = notifyOnUpdate,
            },
        });
    }

    [Fact]
    public async Task TellsTheUserWhenADelistedItemComesBack()
    {
        await SeedAsync(delisted: true);

        Assert.Equal(RefreshOutcome.Updated, await _service.RefreshAsync(ItemId));

        var notification = Assert.Single(
            _store.Notifications.Load(),
            entry => entry.Kind == NotificationKind.ItemBackOnBooth);
        Assert.Equal(ItemId, notification.ItemId);
    }

    /// <summary>印は外れる。外すのは取り直せた瞬間だけ。</summary>
    [Fact]
    public async Task ClearsTheDelistedMark()
    {
        await SeedAsync(delisted: true);
        await _service.RefreshAsync(ItemId);

        var item = await _store.Items.LoadAsync(ItemId);

        Assert.False(item!.Local.IsDelisted);
        Assert.Equal(0, item.Local.ConsecutiveNotFoundCount);
    }

    /// <summary>
    /// ユーザが付けた名前はそのまま。切り替えるかは聞かない
    /// （自分で付けた名前を優先すると決めてある）。
    /// </summary>
    [Fact]
    public async Task KeepsTheNameTheUserGave()
    {
        await SeedAsync(delisted: true, displayName: "とりさん");
        await _service.RefreshAsync(ItemId);

        var item = await _store.Items.LoadAsync(ItemId);

        Assert.Equal("とりさん", item!.Local.DisplayName);
        Assert.Equal("とりさん", item.DisplayName);
        Assert.Equal("オリジナル3Dモデル『Bird/鳥』", item.Booth.Name);
    }

    /// <summary>
    /// 「知らせる」を切っていても出す。**これは更新の知らせではなく、
    /// こちらが「もう無い」と判断していたのが誤りだったという訂正。**
    /// </summary>
    [Fact]
    public async Task TellsEvenWhenUpdateNoticesAreOff()
    {
        await SeedAsync(delisted: true, notifyOnUpdate: false);
        await _service.RefreshAsync(ItemId);

        Assert.Contains(
            _store.Notifications.Load(),
            entry => entry.Kind == NotificationKind.ItemBackOnBooth);
    }

    /// <summary>印が立っていなかった商品では出さない（普通の取り直し）。</summary>
    [Fact]
    public async Task SaysNothingWhenTheItemWasNeverDelisted()
    {
        await SeedAsync(delisted: false);
        await _service.RefreshAsync(ItemId);

        Assert.DoesNotContain(
            _store.Notifications.Load(),
            entry => entry.Kind == NotificationKind.ItemBackOnBooth);
    }
}
