using System.Net;
using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 未確定の「このIDで登録」は、商品JSON・商品ページ・1枚目の画像・ショップのアイコンまでを取って終わり、
/// 残りの画像は梯子の⑤（<see cref="BoothPriority.Gallery"/>）の段で後から裏に取る（メモ60 案B・ユーザ判断 2026-10-06）。
///
/// 時計には頼らない。答えを止めて門に待ち手を並ばせ、BOOTH が答えた順で見る（門は1本ずつなので、答えた順＝通した順）。
/// </summary>
public class RegisterGalleryLaterTests : IDisposable
{
    private const string ItemA = "9900701";
    private const string ItemB = "9900702";
    private const string ShopThumbnail = "https://booth.pximg.net/c/48x48/users/9900700/shop.jpg";
    private const string Sentinel = "https://booth.pximg.net/fake/sentinel-user.png";
    private const string ThumbnailStep = "https://booth.pximg.net/fake/sentinel-thumbnail.png";
    private const string ShopIconStep = "https://booth.pximg.net/fake/sentinel-shopicon.png";

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly BoothClient _client;
    private readonly ImagePipeline _images;
    private readonly ItemService _service;
    private readonly HoldingBooth _booth = new();

    public RegisterGalleryLaterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-gallery-later-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = true };
        _client = new BoothClient(new HttpClient(_booth), settings, TestWait.None);
        _images = new ImagePipeline(_client, _paths, settings);
        _service = new ItemService(_store, _client, _images, settings);
    }

    public void Dispose()
    {
        _booth.ReleaseAll();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private static string Json(string itemId) => $"https://booth.pm/ja/items/{itemId}.json";

    private static string Page(string itemId) => $"https://booth.pm/ja/items/{itemId}";

    private static string Image(string itemId, int index) => $"https://booth.pximg.net/fake/i/{itemId}/{index}.png";

    private static bool IsShopIcon(string url) => url.Contains("/users/9900700/", StringComparison.Ordinal);

    /// <summary>画像3枚とショップのアイコンを持つ商品を答える作り物。指名した問い合わせは、放すまで答えない。</summary>
    private sealed class HoldingBooth : HttpMessageHandler
    {
        private readonly object _sync = new();
        private readonly List<string> _sent = [];
        private readonly List<string> _answered = [];
        private readonly List<(Func<string, bool> Match, TaskCompletionSource Release)> _holds = [];

        public IReadOnlyList<string> Sent
        {
            get
            {
                lock (_sync)
                {
                    return [.. _sent];
                }
            }
        }

        /// <summary>答えた順。門は1本ずつ通すので、これが BOOTH へ出た順になる。</summary>
        public IReadOnlyList<string> Answered
        {
            get
            {
                lock (_sync)
                {
                    return [.. _answered];
                }
            }
        }

        public TaskCompletionSource Hold(Func<string, bool> match)
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_sync)
            {
                _holds.Add((match, release));
            }

            return release;
        }

        public void ReleaseAll()
        {
            lock (_sync)
            {
                foreach (var (_, release) in _holds)
                {
                    release.TrySetResult();
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Task? waiting;
            lock (_sync)
            {
                _sent.Add(url);
                waiting = _holds.FirstOrDefault(hold => hold.Match(url)).Release?.Task;
            }

            if (waiting is not null)
            {
                await waiting.WaitAsync(cancellationToken);
            }

            lock (_sync)
            {
                _answered.Add(url);
            }

            if (url.EndsWith(".json", StringComparison.Ordinal))
            {
                var id = url.Split('/')[^1].Replace(".json", string.Empty, StringComparison.Ordinal);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                        {
                          "id": {{id}},
                          "name": "作り物の商品 {{id}}",
                          "shop": { "name": "作り物の店", "subdomain": "fake-shop", "url": "https://fake-shop.booth.pm/", "thumbnail_url": "{{ShopThumbnail}}" },
                          "images": [
                            { "original": "{{Image(id, 1)}}" },
                            { "original": "{{Image(id, 2)}}" },
                            { "original": "{{Image(id, 3)}}" }
                          ],
                          "variations": [ { "id": 1, "name": null, "price": 100 } ]
                        }
                        """),
                };
            }

            return url.Contains("booth.pximg.net", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TinyPng) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html><body></body></html>") };
        }
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

    /// <summary>画面の入口（<c>CommandHandler</c>）と同じく「人が押した」の段で登録する。</summary>
    private Task<bool> AssignAsUser(string hash, string itemId)
        => Task.Run(async () =>
        {
            using var _ = BoothClient.Prioritize(BoothPriority.User);
            return await _service.AssignItemIdAsync(hash, itemId);
        });

    private Task StartFetch(BoothPriority priority, string url)
        => Task.Run(async () =>
        {
            using var _ = BoothClient.Prioritize(priority);
            await _client.GetBinaryAsync(url);
        });

    private static async Task Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"{what}が起きなかった");
            await Task.Delay(5);
        }
    }

    private Task UntilQueued(int expected)
        => Until(() => _client.WaitingRequestCount >= expected, $"門に {expected} 本並ぶこと");

    private bool HasImageOnDisk(string itemId, int index)
        => File.Exists(Path.Combine(_paths.ItemImagesDir(itemId), ImagePipeline.FileNameFor(Image(itemId, index))));

    [Fact]
    public async Task 登録はJSONとページと1枚目とアイコンで終わり_残りの画像は次の登録より後で_1枚目の段とアイコンの段の間に来る()
    {
        await AddUnresolvedAsync("aaa");
        await AddUnresolvedAsync("bbb");

        // A のアイコンで止め、その間に「人が押した」1本（見張り）を門に並ばせる。
        // アイコンを放すと見張りが門を取って止まるので、A の登録が終わった後に頼まれた残りの画像は門で待つ
        var icon = _booth.Hold(IsShopIcon);
        var sentinel = _booth.Hold(url => url == Sentinel);

        var registerA = AssignAsUser("aaa", ItemA);
        await Until(() => _booth.Sent.Any(IsShopIcon), "A のアイコンの問い合わせ");
        var user = StartFetch(BoothPriority.User, Sentinel);
        await UntilQueued(1);
        icon.TrySetResult();

        Assert.True(await registerA);

        // 登録の中で取ったのは4つだけ（残りの2枚は登録の後）
        Assert.Equal([Json(ItemA), Page(ItemA), Image(ItemA, 1), _booth.Answered[3]], _booth.Answered);
        Assert.True(IsShopIcon(_booth.Answered[3]));
        Assert.True(HasImageOnDisk(ItemA, 1));
        Assert.False(HasImageOnDisk(ItemA, 2));
        Assert.DoesNotContain(_store.Unresolved.Load(), file => file.Hash == "aaa");

        // 残りの画像（A の2枚目）が門で待つ。そこへ次の登録（人が押した）と、④・⑥の段の1本ずつを並べる
        await UntilQueued(1);
        var thumbnail = StartFetch(BoothPriority.Thumbnail, ThumbnailStep);
        var shopIcon = StartFetch(BoothPriority.ShopIcon, ShopIconStep);
        var registerB = AssignAsUser("bbb", ItemB);
        await UntilQueued(4);
        sentinel.TrySetResult();

        Assert.True(await registerB);
        await Task.WhenAll(user, thumbnail, shopIcon);
        await Until(() => HasImageOnDisk(ItemA, 3) && HasImageOnDisk(ItemB, 3), "残りの画像が揃うこと");

        var order = _booth.Answered.ToList();

        // 見張りが門を放したとき、待っていた中で先に通るのは次の登録（人が押した）。残りの画像は先回りしない
        Assert.Equal(Json(ItemB), order[order.IndexOf(Sentinel) + 1]);

        // 残りの画像は⑤の段：④（1枚目の段）より後、⑥（アイコンの段）より前
        Assert.True(order.IndexOf(ThumbnailStep) < order.IndexOf(Image(ItemA, 2)));
        Assert.True(order.IndexOf(Image(ItemA, 2)) < order.IndexOf(ShopIconStep));

        // 次の登録の画像も、1枚目までが登録の中、残りは後から。同じ店のアイコンは手元にあるので問い合わせない
        Assert.True(order.IndexOf(Image(ItemB, 1)) < order.IndexOf(Image(ItemB, 2)));
        Assert.Single(order, IsShopIcon);

        // 1枚ずつ1回だけ取る（残りの画像は1枚目を取り直さない）
        Assert.Single(order, url => url == Image(ItemA, 1));
        Assert.Single(order, url => url == Image(ItemA, 2));
        Assert.Single(order, url => url == Image(ItemA, 3));
    }

    [Fact]
    public async Task 登録の見込みは_JSONとページと1枚目とアイコン_画像の枚数には寄らない()
    {
        var preview = await _service.PreviewAsync(ItemA);

        Assert.NotNull(preview);
        Assert.Equal(2 + 1 + 1, preview!.RequestsToRegister);
    }

    [Fact]
    public async Task 画像を保存しない設定なら_残りの画像を頼まない()
    {
        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var client = new BoothClient(new HttpClient(_booth), settings, TestWait.None);
        var service = new ItemService(_store, client, new ImagePipeline(client, _paths, settings), settings);
        await AddUnresolvedAsync("aaa");

        Assert.True(await service.AssignItemIdAsync("aaa", ItemA));

        Assert.Equal([Json(ItemA), Page(ItemA)], _booth.Answered);
    }

    [Fact]
    public async Task 登録の列が待たせている間は残りの画像を頼まず_札を返すとまとめて頼む()
    {
        await AddUnresolvedAsync("aaa");
        await AddUnresolvedAsync("bbb");

        var hold = _service.HoldRemainingImages();
        Assert.True(await AssignAsUser("aaa", ItemA));
        Assert.True(await AssignAsUser("bbb", ItemB));

        // 列が動いている間（札を持っている間）は、2件とも残りの画像を1本も問い合わせていない
        Assert.DoesNotContain(_booth.Sent, url => url == Image(ItemA, 2) || url == Image(ItemB, 2));

        hold.Dispose();
        await Until(() => HasImageOnDisk(ItemA, 3) && HasImageOnDisk(ItemB, 3), "札を返すと、待たせた2件の残りの画像が届く");

        // 残りの画像は、2件の登録の問い合わせが全部済んだ後に来る（登録の合間に入らない）
        var answered = _booth.Answered.ToList();
        var lastRegistration = answered.FindLastIndex(url => url == Image(ItemB, 1) || IsShopIcon(url));
        var firstRemaining = answered.FindIndex(url => url == Image(ItemA, 2) || url == Image(ItemB, 2));
        Assert.True(firstRemaining > lastRegistration, string.Join(" / ", _booth.Answered));
    }
}
