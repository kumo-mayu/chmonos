using System.Net;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 再取得の最中に人が編集したときの振る舞い。
///
/// 再取得はBOOTHへ2回問い合わせるので、始めてから書き終わるまでに数秒かかる。
/// 「取り込み中もアプリを使える」ようにすると、**その数秒はユーザが編集している時間**になる。
/// </summary>
public class ItemRefreshTests : IDisposable
{
    private const string ItemId = "9907001";

    private static readonly string ItemJson = """
        {
          "id": 9907001,
          "name": "真・アバターペンシステム",
          "description": "アバターに組み込むペンシステムです。",
          "price": "¥ 2,500",
          "url": "https://booth.pm/ja/items/9907001",
          "shop": { "name": "Sample Gates", "subdomain": "samplerin", "url": "https://samplerin.booth.pm/" },
          "images": [],
          "variations": [
            { "id": 12826082, "name": null, "price": 2500 }
          ]
        }
        """;

    private readonly string _root;
    private readonly DataStore _store;
    private readonly ItemService _service;

    /// <summary>HTMLを取りに来た瞬間に走らせる。取得の最中に人が編集した、という状況を作る。</summary>
    private Func<Task>? _whileFetchingHtml;

    public ItemRefreshTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-refresh-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);

        var settings = new AppSettings { FetchIntervalMs = 0 };
        var client = new BoothClient(new HttpClient(new StubHandler(this)), settings);
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

    private sealed class StubHandler(ItemRefreshTests owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();

            if (url.EndsWith(".json", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ItemJson) };
            }

            if (url.Contains("/items/", StringComparison.Ordinal))
            {
                if (owner._whileFetchingHtml is { } edit)
                {
                    await edit();
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html><body></body></html>"),
                };
            }

            // 画像などは持っていない。落ちても取得は成立する
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private Task SaveItemAsync(LocalBlock local)
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { Name = "取り直す前の名前", FetchedAt = DateTimeOffset.Now },
            Local = local,
        });

    /// <summary>
    /// **これがA1で塞いだ穴。**
    ///
    /// 再取得は開始時点の <c>local</c> を抱えたまま数秒通信する。丸ごと書き戻す作りでは、
    /// その間に書かれたメモも分類も、取得が終わった瞬間に消えていた。
    /// </summary>
    [Fact]
    public async Task KeepsWhatTheUserTypedWhileTheFetchWasInFlight()
    {
        await SaveItemAsync(new LocalBlock { Memo = "取得前のメモ" });

        _whileFetchingHtml = () => _store.Items.SaveLocalAsync(
            ItemId,
            new LocalBlock
            {
                Memo = "取得中に書いたメモ",
                UserTags = [new UserTagAssignment { Top = "衣装" }],
            },
            LocalOwners.EditScreen);

        Assert.Equal(RefreshOutcome.Updated, await _service.RefreshAsync(ItemId));

        var reloaded = await _store.Items.LoadAsync(ItemId);

        Assert.Equal("取得中に書いたメモ", reloaded!.Local.Memo);
        Assert.Equal("衣装", reloaded.Local.UserTags[0].Top);

        // boothの側はちゃんと入れ替わっている
        Assert.Equal("真・アバターペンシステム", reloaded.Booth.Name);
        Assert.NotNull(reloaded.Local.LastFetchedAt);
        Assert.NotNull(reloaded.Local.NextFetchDueAt);
    }

    /// <summary>
    /// <c>ExistsOnBooth</c> は取り直した後のバリエーション一覧から入れ直す。
    /// 取得中にユーザが足した購入記録にも、その場で正しい値が入る。
    /// </summary>
    [Fact]
    public async Task MarksPurchasesAgainstTheVariationsJustFetched()
    {
        await SaveItemAsync(new LocalBlock());

        _whileFetchingHtml = () => _store.Items.SaveLocalAsync(
            ItemId,
            new LocalBlock
            {
                Purchases =
                [
                    new Purchase { VariationId = 12826082, Price = 2500 },
                    new Purchase { VariationId = 99999999, Price = 800 },
                ],
            },
            LocalOwners.EditScreen);

        await _service.RefreshAsync(ItemId);

        var reloaded = await _store.Items.LoadAsync(ItemId);

        Assert.Equal(2, reloaded!.Local.Purchases.Count);
        Assert.True(reloaded.Local.Purchases[0].ExistsOnBooth);
        Assert.False(reloaded.Local.Purchases[1].ExistsOnBooth);
    }

    /// <summary>取得の最中に商品を消されたら、書かずに終わる。消したものが戻ってきてはいけない。</summary>
    [Fact]
    public async Task DoesNotRecreateAnItemDeletedDuringTheFetch()
    {
        await SaveItemAsync(new LocalBlock());

        _whileFetchingHtml = () =>
        {
            File.Delete(Path.Combine(_root, "items", ItemId + ".json"));
            return Task.CompletedTask;
        };

        await _service.RefreshAsync(ItemId);

        Assert.False(_store.Items.Exists(ItemId));
    }
}
