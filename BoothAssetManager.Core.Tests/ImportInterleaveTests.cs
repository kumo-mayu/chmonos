using System.Net;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 取り込みの続きと割り込み。
///
/// U5：画像を取っている最中に積んだフォルダは、今の周回の残りの画像より先に
/// ①商品の情報 ②商品ページ ④1枚目 まで進む（設計詳細_取り込みの順序.md の「積まれたら①②が最優先」）。
/// 以前は今の周回が⑥ショップのアイコンまで終わるまで、積んだ分は何も始まらなかった。
///
/// U9：②の途中で閉じてから押し直しても、取得済みの商品の説明が取り直される。
/// 以前は①が済んだ商品は「取得済み」で飛ばされ、⑦の取り直しの日まで説明が埋まらなかった。
/// </summary>
public class ImportInterleaveTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly ImportPipeline _pipeline;

    /// <summary>BOOTHへ行った順番。</summary>
    private readonly List<string> _requests = [];

    public ImportInterleaveTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-interleave-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        var settings = new AppSettings { FetchIntervalMs = 0 };
        var client = new BoothClient(new HttpClient(new FakeBooth(this)), settings);

        _pipeline = new ImportPipeline(_store, client, new ImagePipeline(client, _paths, settings), settings);
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

    private static string JsonFor(string itemId) => $$"""
        {
          "id": {{itemId}},
          "name": "商品 {{itemId}}",
          "url": "https://booth.pm/ja/items/{{itemId}}",
          "shop": {
            "name": "shop",
            "subdomain": "shop",
            "thumbnail_url": "https://booth.pximg.net/c/48x48/users/1/shop.jpg",
            "url": "https://shop.booth.pm/"
          },
          "images": [
            { "original": "https://booth.pximg.net/a/i/{{itemId}}/one.jpg" },
            { "original": "https://booth.pximg.net/a/i/{{itemId}}/two.jpg" }
          ],
          "variations": [ { "id": 1, "name": null, "price": 100 } ]
        }
        """;

    private sealed class FakeBooth(ImportInterleaveTests owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();

            lock (owner._requests)
            {
                owner._requests.Add(url);
            }

            if (url.EndsWith(".json", StringComparison.Ordinal))
            {
                var id = url.Split('/')[^1].Replace(".json", string.Empty, StringComparison.Ordinal);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonFor(id)) });
            }

            if (url.Contains("booth.pximg.net", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TinyPng) });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    <html><body>
                      <section class="main-info-column">
                        <div class="js-market-item-detail-description description"><p class="autolink">説明の本文です。</p></div>
                        <section class="shop__text"><h2>利用規約</h2><p>再配布は禁止です。</p></section>
                      </section>
                    </body></html>
                    """),
            });
        }
    }

    private sealed class InlineProgress(Action<ImportProgress> report) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => report(value);
    }

    /// <summary>zipに商品IDを書き、ダウンロード元の記録で解決が1候補に絞れるようにする。</summary>
    private string CreateSource(string name, params string[] itemIds)
    {
        var folder = Path.Combine(_root, name);
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

    private int IndexOf(Func<string, bool> match)
    {
        lock (_requests)
        {
            return _requests.FindIndex(url => match(url));
        }
    }

    private static bool Json(string url, string id) => url.EndsWith($"/items/{id}.json", StringComparison.Ordinal);

    private static bool Image(string url, string id, string which)
        => url.Contains($"/i/{id}/{which}.jpg", StringComparison.Ordinal);

    /// <summary>
    /// **U5 の本体。**残りの画像（⑤）を取っている最中に積んだ商品は、
    /// 今の周回のまだ取っていない2枚目より先に、商品の情報と1枚目まで進む。
    /// </summary>
    [Fact]
    public async Task StackedFolderGoesAheadOfTheRemainingImages()
    {
        var first = CreateSource("first", "111", "222");
        var second = CreateSource("second", "333");

        var work = new ImportWorkSet([first]);
        var stacked = false;

        var progress = new InlineProgress(report =>
        {
            if (!stacked && report.Phase == ImportPhase.FetchingGallery)
            {
                stacked = true;
                work.Add([second]);
            }
        });

        await _pipeline.RunAsync(work, progress);

        var stackedJson = IndexOf(url => Json(url, "333"));
        var stackedFirstImage = IndexOf(url => Image(url, "333", "one"));
        var lastRemaining = IndexOf(url => Image(url, "222", "two"));

        Assert.True(stackedJson >= 0 && lastRemaining >= 0);
        Assert.True(stackedJson < lastRemaining, "積んだ商品の情報が、今の周回の2枚目より後になった");
        Assert.True(stackedFirstImage < lastRemaining, "積んだ商品の1枚目が、今の周回の2枚目より後になった");

        // 最後には全部そろう
        Assert.True(File.Exists(Path.Combine(_paths.ItemImagesDir("333"), "02.webp"))
            || Directory.EnumerateFiles(_paths.ItemImagesDir("333"), "*.webp").Count() == 2);
    }

    /// <summary>
    /// U8・U10：①で作った商品は③が済むまで「③待ち」で、画面はそれを見て編集に出さない。
    /// ③の段が終わったら（検出を使わない設定でも）外れる。外れないと取り込みが終わるまで編集できない。
    /// </summary>
    [Fact]
    public async Task HoldsNewItemsUntilDetectionIsDone()
    {
        var work = new ImportWorkSet([CreateSource("hold", "111", "222")]);
        bool? heldDuringPage = null;
        bool? heldDuringImages = null;

        var progress = new InlineProgress(report =>
        {
            if (report.Phase == ImportPhase.FetchingHtml && heldDuringPage is null)
            {
                heldDuringPage = work.IsAwaitingDetection("111");
            }

            if (report.Phase == ImportPhase.FetchingThumbnails && heldDuringImages is null)
            {
                heldDuringImages = work.IsAwaitingDetection("111");
            }
        });

        await _pipeline.RunAsync(work, progress);

        Assert.True(heldDuringPage, "②の間に、①で作った商品が③待ちになっていない");
        Assert.False(heldDuringImages, "③の後の画像の段になっても③待ちのまま");
        Assert.Equal(0, work.AwaitingDetectionCount);
        Assert.Equal(2, work.AddedCount);
    }

    /// <summary>
    /// U1：残り時間の見込みの元になる「残りの問い合わせ」。①の最初は新しい商品2件ぶんの①②が残っていて、
    /// 画像（2枚×2件＋ショップのアイコン1つ）まで取り終わると0に戻る。
    /// </summary>
    [Fact]
    public async Task CountsTheRequestsLeft()
    {
        var work = new ImportWorkSet([CreateSource("plan", "111", "222")]);
        (int Json, int Pages, int Images)? atStart = null;
        (int Json, int Pages, int Images)? atFirstImage = null;

        var progress = new InlineProgress(report =>
        {
            if (report.Phase == ImportPhase.FetchingJson && atStart is null)
            {
                atStart = work.RequestsLeft;
            }

            if (report.Phase == ImportPhase.FetchingThumbnails && atFirstImage is null)
            {
                atFirstImage = work.RequestsLeft;
            }
        });

        await _pipeline.RunAsync(work, progress);

        Assert.Equal((2, 2, 0), atStart!.Value);
        // 1枚目を取り始めたところ。取っている最中の1件は、取り終えるまで残りに数える
        Assert.Equal((0, 0, 5), atFirstImage!.Value);
        Assert.Equal((0, 0, 0), work.RequestsLeft);
    }

    /// <summary>取得済みの商品は③待ちにしない。前の取り込みで編集できていたものを塞がない。</summary>
    [Fact]
    public async Task DoesNotHoldItemsThatWereAlreadyThere()
    {
        var source = CreateSource("known", "111");
        await _pipeline.RunAsync(new ImportWorkSet([source]));
        File.Delete(_paths.ItemHtmlFile("111"));

        var work = new ImportWorkSet([source]);
        var held = false;
        var progress = new InlineProgress(report =>
        {
            held |= work.IsAwaitingDetection("111");
        });

        await _pipeline.RunAsync(work, progress);

        Assert.False(held, "取得済みの商品を③待ちにした（説明を取り直しているだけ）");
        Assert.Equal(0, work.AddedCount);
    }

    /// <summary>積まなければ、今までどおり段ごとに全商品を回る（1枚目を全部取ってから2枚目）。</summary>
    [Fact]
    public async Task KeepsTheLadderWhenNothingIsStacked()
    {
        await _pipeline.RunAsync(new ImportWorkSet([CreateSource("only", "111", "222")]));

        Assert.True(IndexOf(url => Image(url, "222", "one")) < IndexOf(url => Image(url, "111", "two")));
    }

    /// <summary>
    /// **U9 の本体。**①が済んで②の前に閉じた商品（JSONはあるが説明が無い）は、
    /// 押し直したときに商品ページを取り直す。
    /// </summary>
    [Fact]
    public async Task RefetchesThePageOfAnItemLeftWithoutOne()
    {
        var source = CreateSource("resume", "111");

        // 1回目：全部取る。そのあと「②の前に閉じた」状態を作る（説明を消す）
        await _pipeline.RunAsync(new ImportWorkSet([source]));
        File.Delete(_paths.ItemHtmlFile("111"));
        lock (_requests)
        {
            _requests.Clear();
        }

        // 2回目：押し直す
        await _pipeline.RunAsync(new ImportWorkSet([source]));

        Assert.True(IndexOf(url => url.Contains("/items/111", StringComparison.Ordinal) && !url.EndsWith(".json", StringComparison.Ordinal)) >= 0,
            "説明の無い取得済みの商品の、商品ページを取りに行っていない");
        Assert.Contains("説明の本文です。", File.ReadAllText(_paths.ItemHtmlFile("111")));
    }

    /// <summary>説明がある取得済みの商品は、押し直しても取りに行かない（通信を増やさない）。</summary>
    [Fact]
    public async Task DoesNotRefetchAnItemThatAlreadyHasItsPage()
    {
        var source = CreateSource("again", "111");

        await _pipeline.RunAsync(new ImportWorkSet([source]));
        lock (_requests)
        {
            _requests.Clear();
        }

        await _pipeline.RunAsync(new ImportWorkSet([source]));

        Assert.Equal(-1, IndexOf(url => url.Contains("/items/111", StringComparison.Ordinal)));
    }
}
