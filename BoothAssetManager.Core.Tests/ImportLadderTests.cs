using System.Net;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 取り込みの順序。**商品ごとに全部取るのではなく、段ごとに全商品を回る。**
///
/// 実データでは画像が全リクエストの76%を占める。後ろに回すと、
/// 「検索も統計も使えるようになるまで」が31分から5分に縮む。
/// </summary>
public class ImportLadderTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly ImportPipeline _pipeline;

    /// <summary>BOOTHへ行った順番。ここに梯子がそのまま出る。</summary>
    private readonly List<string> _requests = [];

    public ImportLadderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-ladder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        var settings = new AppSettings { FetchIntervalMs = 0 };
        var client = new BoothClient(new HttpClient(new LadderHandler(this)), settings);

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

    /// <summary>1x1のPNG。中身は問わないので最小のものを使う。</summary>
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static string JsonFor(string itemId, string shop) => $$"""
        {
          "id": {{itemId}},
          "name": "商品 {{itemId}}",
          "url": "https://booth.pm/ja/items/{{itemId}}",
          "shop": {
            "name": "{{shop}}",
            "subdomain": "{{shop}}",
            "thumbnail_url": "https://booth.pximg.net/c/48x48/users/1/{{shop}}.jpg",
            "url": "https://{{shop}}.booth.pm/"
          },
          "images": [
            { "original": "https://booth.pximg.net/a/i/{{itemId}}/one.jpg" },
            { "original": "https://booth.pximg.net/a/i/{{itemId}}/two.jpg" }
          ],
          "variations": [ { "id": 1, "name": null, "price": 100 } ]
        }
        """;

    private sealed class LadderHandler(ImportLadderTests owner) : HttpMessageHandler
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
                var shop = id == "111" ? "alpha" : "beta";

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonFor(id, shop)),
                });
            }

            if (url.Contains("booth.pximg.net", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(TinyPng),
                });
            }

            // 商品ページHTML。節の取り出しは section.shop__text の構造を見るので、実際の形に合わせる
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    <html><body>
                      <section class="main-info-column">
                        <div class="js-market-item-detail-description description">
                          <p class="autolink">説明の本文です。</p>
                        </div>
                        <section class="shop__text">
                          <h2 class="font-bold">対応アバター</h2>
                          <p class="js-autolink">くうた に対応しています。</p>
                        </section>
                      </section>
                    </body></html>
                    """),
            });
        }
    }

    /// <summary>zipに商品IDを書いて、解決が通るようにする。</summary>
    private string CreateSource(params string[] itemIds)
    {
        var folder = Path.Combine(_root, "source");
        Directory.CreateDirectory(folder);

        foreach (var itemId in itemIds)
        {
            var path = Path.Combine(folder, $"item_{itemId}.zip");
            File.WriteAllText(path, itemId);

            // Zone.Identifier に商品ページのURLを書くと、解決が1候補に絞れる
            File.WriteAllText(
                path + ":Zone.Identifier",
                "[ZoneTransfer]\r\nZoneId=3\r\n"
                    + $"HostUrl=https://booth.pm/ja/items/{itemId}\r\n");
        }

        return folder;
    }

    private static string Kind(string url)
    {
        if (url.EndsWith(".json", StringComparison.Ordinal))
        {
            return "json";
        }

        if (url.Contains("/c/48x48/", StringComparison.Ordinal) || url.Contains("/c/150x150/", StringComparison.Ordinal))
        {
            return "icon";
        }

        if (url.Contains("booth.pximg.net", StringComparison.Ordinal))
        {
            return url.Contains("/one.jpg", StringComparison.Ordinal) ? "image1" : "image2";
        }

        return "html";
    }

    /// <summary>
    /// **A3の本体。**2商品を取り込むと、段ごとに全商品を回る順になる。
    ///
    /// 商品ごとに取っていた頃は json,html,image,image,icon,json,html,… だった。
    /// </summary>
    [Fact]
    public async Task WalksTheLadderStageByStageInsteadOfItemByItem()
    {
        await _pipeline.RunAsync([CreateSource("111", "222")]);

        var kinds = _requests.Select(Kind).ToList();

        Assert.Equal(
            ["json", "json", "html", "html", "image1", "image1", "image2", "image2", "icon", "icon"],
            kinds);
    }

    /// <summary>
    /// ①が終わった時点で、検索・絞り込み・統計に要るものは全部揃っている。
    /// ここで中断しても商品は残る。
    /// </summary>
    [Fact]
    public async Task SavesEverythingSearchNeedsBeforeTouchingAnyImage()
    {
        await _pipeline.RunAsync([CreateSource("111", "222")]);

        var firstImage = _requests.FindIndex(url => Kind(url) is "image1");

        // 1枚目の画像より前に、両方の商品が保存されている
        Assert.Equal(2, _requests.Take(firstImage).Count(url => Kind(url) == "json"));

        var item = await _store.Items.LoadAsync("111");
        Assert.Equal("商品 111", item!.Booth.Name);
        Assert.Single(item.Booth.Variations);
    }

    /// <summary>②で h2 の節が後から入る。①で保存したものを潰さない。</summary>
    [Fact]
    public async Task AddsTheDescriptionSectionsInTheSecondStage()
    {
        await _pipeline.RunAsync([CreateSource("111")]);

        var item = await _store.Items.LoadAsync("111");

        Assert.Equal("商品 111", item!.Booth.Name);
        Assert.Single(item.Booth.H2Sections);
        Assert.Equal("対応アバター", item.Booth.H2Sections[0].Heading);
    }

    /// <summary>ショップのアイコンは最後。無くても名前で用は足りる。</summary>
    [Fact]
    public async Task LeavesShopIconsUntilTheVeryEnd()
    {
        await _pipeline.RunAsync([CreateSource("111", "222")]);

        var kinds = _requests.Select(Kind).ToList();
        var firstIcon = kinds.IndexOf("icon");

        Assert.True(firstIcon > 0);
        Assert.All(kinds.Skip(firstIcon), kind => Assert.Equal("icon", kind));
    }

    /// <summary>同じショップの商品が複数あっても、アイコンは1回しか取らない。</summary>
    [Fact]
    public async Task FetchesEachShopIconOnce()
    {
        await _pipeline.RunAsync([CreateSource("222", "333")]);

        Assert.Equal(1, _requests.Count(url => Kind(url) == "icon"));
    }
}
