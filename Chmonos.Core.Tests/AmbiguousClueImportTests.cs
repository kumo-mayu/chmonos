using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 手掛かりが2つ以上の商品を指すファイルは、どれかの商品へ黙って入れず、候補を付けて未確定に残す。
/// 「推定した値を勝手に入れない。候補として見せ、人が選んだときだけ入れる」（CLAUDE.md の作りの決め事）。
///
/// 2026-11-28 の点検で、候補が2つ以上でも1つ目の商品へ入れる形に壊しても、どの試験も落ちなかった（M26）。
/// 試験の中の未確定は、どれも候補が1つか0だったため。
/// </summary>
public sealed class AmbiguousClueImportTests : IDisposable
{
    private const string FirstId = "9900201";
    private const string SecondId = "9900202";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-ambiguous-clue-" + Guid.NewGuid().ToString("N"));
    private readonly string _source;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly AppSettings _settings = new() { SaveImages = false, FetchIntervalMs = 0 };

    /// <summary>BOOTH へ出た問い合わせ。候補を決めずに残すなら、商品の情報を取りに行く理由が無い。</summary>
    private readonly ConcurrentQueue<string> _requests = new();

    public AmbiguousClueImportTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);
        _source = Path.Combine(_root, "source");
        Directory.CreateDirectory(_source);
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
    }

    private ImportPipeline Pipeline()
    {
        var client = new BoothClient(new HttpClient(new RecordingBooth(_requests)), _settings, TestWait.None);
        return new ImportPipeline(_store, client, new ImagePipeline(client, _paths, _settings), _settings);
    }

    /// <summary>どの問い合わせにも商品の情報を返す（入れてしまう形に壊したとき、商品が作られて差が見えるように）。</summary>
    private sealed class RecordingBooth(ConcurrentQueue<string> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            requests.Enqueue(url);
            if (url.EndsWith(".json", StringComparison.Ordinal))
            {
                var id = url.Split('/')[^1].Replace(".json", string.Empty, StringComparison.Ordinal);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                        {
                          "id": {{id}},
                          "name": "作り物の商品 {{id}}",
                          "url": "https://booth.pm/ja/items/{{id}}",
                          "shop": { "name": "shop", "subdomain": "shop", "thumbnail_url": "", "url": "https://shop.booth.pm/" },
                          "images": [],
                          "variations": [ { "id": 1, "name": null, "price": 100 } ]
                        }
                        """),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html><body></body></html>") });
        }
    }

    /// <summary>中の文に商品の URL を2つ書いた zip（例：セット商品の説明に、単品の商品ページも書いてある）。</summary>
    private string MakeZipNamingTwoItems()
    {
        var zip = Path.Combine(_source, "asset.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("readme.txt").Open());
            writer.Write($"https://booth.pm/ja/items/{FirstId}\nhttps://booth.pm/ja/items/{SecondId}\n");
        }

        return zip;
    }

    [Fact]
    public async Task 手掛かりが2つの商品を指すファイルは商品に入れず候補2つで未確定に残る()
    {
        var zip = MakeZipNamingTwoItems();

        await Pipeline().RunAsync([_source]);

        // どちらの商品も作らない（1つ目へ黙って入れない）
        Assert.Empty(_store.Items.EnumerateItemIds());

        var unresolved = Assert.Single(_store.Unresolved.Load());
        Assert.Equal([zip], unresolved.Paths);
        Assert.Equal([FirstId, SecondId], unresolved.CandidateItemIds.Order(StringComparer.Ordinal));

        // 人が選ぶまで BOOTH へ問い合わせない（候補の先回りは設定で別に走る⑧で、取り込みの中ではない）
        Assert.Empty(_requests);
    }
}
