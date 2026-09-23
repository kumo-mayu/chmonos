using System.Net;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 取り込みが、時間のかかる段を挟んだ後に古い写しで書き戻さないこと。
/// 読めなかったファイルを黙って飛ばさないこと。
/// </summary>
public class ImportWriteBackTests : IDisposable
{
    private const string ItemId = "111";

    private readonly string _root;
    private readonly DataStore _store;
    private readonly ImportPipeline _pipeline;
    private readonly Handler _handler = new();

    public ImportWriteBackTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-wb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);

        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var client = new BoothClient(new HttpClient(_handler), settings);
        _pipeline = new ImportPipeline(_store, client, new ImagePipeline(client, paths, settings), settings);
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

    private sealed class Handler : HttpMessageHandler
    {
        /// <summary>②（商品ページ）を取っている最中に1回だけ走らせる。</summary>
        public Func<Task>? DuringPage { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();

            if (url.EndsWith(".json", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                        {
                          "id": {{ItemId}},
                          "name": "①で取った名前",
                          "shop": { "name": "alpha", "subdomain": "alpha", "url": "https://alpha.booth.pm/" },
                          "images": [],
                          "variations": [ { "id": 1, "name": null, "price": 100 } ]
                        }
                        """),
                });
            }

            if (DuringPage is { } during)
            {
                DuringPage = null;
                during().GetAwaiter().GetResult();
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body></body></html>"),
            });
        }
    }

    private string CreateSource()
    {
        var folder = Path.Combine(_root, "source");
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, $"item_{ItemId}.zip");
        File.WriteAllText(path, ItemId);
        File.WriteAllText(
            path + ":Zone.Identifier",
            $"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://booth.pm/ja/items/{ItemId}\r\n");

        return folder;
    }

    /// <summary>
    /// ②は①の時点の booth に節を足して丸ごと書いていたので、①と②の間に人が「商品情報を取り直す」を押すと、
    /// 取り直した新しい booth が①の古い物に戻っていた。今の方が新しければ触らない。
    /// </summary>
    [Fact]
    public async Task ThePageStepKeepsABoothRefreshedMeanwhile()
    {
        _handler.DuringPage = async () =>
        {
            var existing = await _store.Items.LoadAsync(ItemId);
            await _store.Items.SaveLocalAsync(
                ItemId,
                existing!.Local,
                LocalOwners.Fetch,
                existing.Booth with { Name = "取り直した名前", FetchedAt = DateTimeOffset.Now.AddMinutes(1) });
        };

        await _pipeline.RunAsync(new ImportWorkSet([CreateSource()]));

        Assert.Equal("取り直した名前", (await _store.Items.LoadAsync(ItemId))!.Booth.Name);
    }

    /// <summary>取り直しが挟まらなければ、②は①の booth に節を足して書く（説明のファイルも置く）。</summary>
    [Fact]
    public async Task ThePageStepStillWritesWhenNothingHappenedMeanwhile()
    {
        await _pipeline.RunAsync(new ImportWorkSet([CreateSource()]));

        Assert.Equal("①で取った名前", (await _store.Items.LoadAsync(ItemId))!.Booth.Name);
        Assert.True(File.Exists(_store.Paths.ItemHtmlFile(ItemId)));
    }

    /// <summary>
    /// ハッシュを計算できなかったファイル（書き込み中のダウンロード・ほかのアプリが開いている物）を
    /// 黙って飛ばしていた。読めなかった数に入れて結果に出す。
    /// </summary>
    [Fact]
    public async Task CountsAFileThatCouldNotBeHashed()
    {
        var source = CreateSource();

        ImportSummary summary;
        using (new FileStream(Path.Combine(source, $"item_{ItemId}.zip"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            summary = await _pipeline.RunAsync(new ImportWorkSet([source]));
        }

        Assert.Equal(1, summary.FilesUnreadable);
        Assert.Null(await _store.Items.LoadAsync(ItemId));
    }
}
