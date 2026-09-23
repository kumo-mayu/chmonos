using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Resolution;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 「検索結果の1位」は本当の検索の1位にだけ付ける（点検 2026-09-23）。
/// 同梱の URL の商品を候補の先頭に置くので、その並びの添字を順位にすると、検索に出てもいない商品が1位の点をもらっていた。
/// 通信は偽物で置き換える。
/// </summary>
public sealed class FallbackRankTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-rank-" + Guid.NewGuid().ToString("N"));

    public FallbackRankTests() => Directory.CreateDirectory(_root);

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

    /// <summary>unitypackage の中の readme に商品の URL が書いてある zip。</summary>
    private string ZipWithBundledUrl(string itemId)
    {
        using var package = new MemoryStream();
        using (var gzip = new GZipStream(package, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            var guid = new string('a', 32);
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"{guid}/pathname")
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes("Assets/Sample/readme.txt\n00")),
            });
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"{guid}/asset")
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes($"https://booth.pm/ja/items/{itemId}")),
            });
        }

        var path = Path.Combine(_root, "Sample_Outfit.zip");
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        using var entry = zip.CreateEntry("Sample.unitypackage").Open();
        entry.Write(package.ToArray());
        return path;
    }

    [Fact]
    public async Task OnlyTheRealTopSearchHitIsCalledFirst()
    {
        var zip = ZipWithBundledUrl("111");
        var resolver = new FallbackResolver(new FakeClient(searchIds: ["222", "333"]));

        var proposal = await resolver.ProposeAsync(zip);

        var bundled = Assert.Single(proposal.Candidates, candidate => candidate.ItemId == "111");
        var top = Assert.Single(proposal.Candidates, candidate => candidate.ItemId == "222");
        Assert.DoesNotContain("検索結果の1位", bundled.Reasons);
        Assert.Contains("検索結果の1位", top.Reasons);
    }

    private sealed class FakeClient(IReadOnlyList<string> searchIds) : IBoothClient
    {
        public Task<BoothFetchResult<string>> SearchAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult(BoothFetchResult<string>.Success(string.Concat(searchIds.Select(id =>
                $"""<li class="item-card l-card" data-product-id="{id}" data-product-name="別の商品 {id}" data-product-brand="shop{id}">"""))));

        public Task<BoothFetchResult<string>> GetItemJsonAsync(string itemId, CancellationToken cancellationToken = default)
            => Task.FromResult(BoothFetchResult<string>.Success($$"""
                { "id": {{itemId}}, "name": "別の商品 {{itemId}}", "url": "https://booth.pm/ja/items/{{itemId}}",
                  "images": [], "variations": [] }
                """));

        public Task<BoothFetchResult<string>> GetItemHtmlAsync(string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BoothFetchResult<byte[]>> GetBinaryAsync(string url, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BoothFetchResult<string>> GetTextUntilAsync(
            string url,
            Func<string, bool> found,
            int maxBytes = 262144,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public int CurrentIntervalMs => 0;

        public bool IsThrottled => false;

#pragma warning disable CS0067
        public event Action<BoothActivity>? ActivityChanged;
#pragma warning restore CS0067
    }
}
