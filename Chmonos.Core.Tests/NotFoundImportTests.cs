using System.Net;
using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// BOOTHに無い商品のファイルを、取り込みが落とさないこと。
///
/// 買っていて手元にあるものなので、無かったことにしてはいけない。
/// 未確定へ戻せば、別IDで再公開されている場合は候補検索が拾えるし、
/// そうでなければ人が「BOOTHに無い商品」として登録できる。
/// </summary>
public class NotFoundImportTests : IDisposable
{
    private const string MissingId = "9999999";
    private const string LivingId = "111";

    private readonly string _root;
    private readonly DataStore _store;
    private readonly ImportPipeline _pipeline;

    public NotFoundImportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-nf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);

        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var client = new BoothClient(new HttpClient(new Handler()), settings, TestWait.None);
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

    /// <summary>MissingId だけ404を返す。ほかは普通に応える。</summary>
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains(MissingId, StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (url.EndsWith(".json", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                        {
                          "id": {{LivingId}},
                          "name": "生きている商品",
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

    private string CreateSource(params string[] itemIds)
    {
        var folder = Path.Combine(_root, "source");
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

    private static UnresolvedFile Unresolved(string hash, string path) => new()
    {
        Hash = hash,
        Paths = [path],
        SizeBytes = 1,
        ModifiedAtUtc = DateTimeOffset.UnixEpoch,
        FirstSeenAt = DateTimeOffset.UnixEpoch,
    };

    /// <summary>
    /// 外付けを外したまま取り込んでも、そこにあった未確定が消えないこと（技術的負債 1-3）。
    /// 走査はつながっていないフォルダを「中身なし」として返すので、前は片付いたのと区別が付かず消えていた。
    /// </summary>
    [Fact]
    public async Task KeepsUnresolvedOnAnUnpluggedDrive()
    {
        var offline = UnresolvedMergeTests.MissingVolumeFolder();
        await _store.Unresolved.SaveAsync([Unresolved("OFFLINE", offline + @"\a.zip")]);

        await _pipeline.RunAsync(new ImportWorkSet([CreateSource(MissingId), offline]));

        var unresolved = _store.Unresolved.Load();
        Assert.Contains(unresolved, file => file.Hash == "OFFLINE");
        Assert.Equal(2, unresolved.Count);
    }

    /// <summary>つながっている取り込み元で見つからなくなった物は、片付いたので落とす（前と同じ）。</summary>
    [Fact]
    public async Task DropsUnresolvedThatIsGoneFromAConnectedSource()
    {
        var source = CreateSource(LivingId);
        await _store.Unresolved.SaveAsync([Unresolved("GONE", Path.Combine(source, "gone.zip"))]);

        await _pipeline.RunAsync(new ImportWorkSet([source]));

        Assert.DoesNotContain(_store.Unresolved.Load(), file => file.Hash == "GONE");
    }

    /// <summary>これが本命。404のファイルが手元から消えないこと。</summary>
    [Fact]
    public async Task KeepsFilesWhoseItemIsGoneFromBooth()
    {
        var summary = await _pipeline.RunAsync(new ImportWorkSet([CreateSource(MissingId)]));

        Assert.Equal(1, summary.NotFound);
        Assert.Empty(_store.Items.EnumerateItemIds());

        var unresolved = _store.Unresolved.Load();
        Assert.Single(unresolved);
        Assert.Contains(MissingId, unresolved[0].Paths[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// 手掛かりから決まった商品IDは候補として載せる。
    /// 「このファイルは 9999999 を指しているが、BOOTHには無い」と読める形にするため。
    /// </summary>
    [Fact]
    public async Task CarriesTheItemIdAsACandidate()
    {
        await _pipeline.RunAsync(new ImportWorkSet([CreateSource(MissingId)]));

        var unresolved = _store.Unresolved.Load();
        Assert.Contains(MissingId, unresolved[0].CandidateItemIds);
    }

    /// <summary>
    /// 404で戻した物も、ふつうの未確定と同じくダウンロード元の記録を持つ（file-lifecycle.md 気になった所9）。
    /// 前は持たず、毎回同じ道を通るので取り込み直しても付かず、元zip の束（ZoneReferrerUrl）に入らなかった。
    /// </summary>
    [Fact]
    public async Task CarriesTheZoneUrlsOfTheFile()
    {
        var folder = CreateSource(MissingId);
        var path = Path.Combine(folder, $"item_{MissingId}.zip");
        File.WriteAllText(
            path + ":Zone.Identifier",
            $"[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=C:\\Downloads\\parent.zip\r\nHostUrl=https://booth.pm/ja/items/{MissingId}\r\n");

        await _pipeline.RunAsync(new ImportWorkSet([folder]));

        var unresolved = Assert.Single(_store.Unresolved.Load());
        Assert.Equal($"https://booth.pm/ja/items/{MissingId}", unresolved.ZoneHostUrl);
        Assert.Equal(@"C:\Downloads\parent.zip", unresolved.ZoneReferrerUrl);
    }

    /// <summary>404の1件があっても、生きている商品の取り込みは普通に通る。</summary>
    [Fact]
    public async Task StillImportsTheItemsThatAreAlive()
    {
        var summary = await _pipeline.RunAsync(new ImportWorkSet([CreateSource(MissingId, LivingId)]));

        Assert.Equal(1, summary.NotFound);
        Assert.Equal(1, summary.ItemsAdded);
        Assert.Contains(LivingId, _store.Items.EnumerateItemIds());
        Assert.Single(_store.Unresolved.Load());
    }

    /// <summary>
    /// 同じフォルダをもう一度取り込んでも、未確定が二重にならないこと。
    /// 未確定は周回をまたいで積み上げる作りなので、ここは崩れやすい。
    /// </summary>
    [Fact]
    public async Task DoesNotPileUpTheSameFileTwice()
    {
        var folder = CreateSource(MissingId);

        await _pipeline.RunAsync(new ImportWorkSet([folder]));
        await _pipeline.RunAsync(new ImportWorkSet([folder]));

        Assert.Single(_store.Unresolved.Load());
    }
}
