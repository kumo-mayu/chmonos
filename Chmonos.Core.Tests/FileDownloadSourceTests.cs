using System.IO.Compression;
using System.Net;
using BoothZipInspector.Models;
using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// ファイルの行に、ダウンロード元の記録（Zone.Identifier）から BOOTH のダウンロードの ID と商品ページを残すこと（ユーザ判断 2026-11-10）。
/// 残すのは数字と組み直した URL だけで、署名付きの URL・BOOTH 以外は残さない。今ある行を書き直すときは保ち、前からの行は起動時の見回りが1回だけ読む。
/// 番号はどれも作り物。
/// </summary>
public class FileDownloadSourceTests : IDisposable
{
    private const string ItemId = "9900821";
    private const long DownloadId = 8800821;
    private const string Signature = "Signature=SECRET-SIGNATURE";

    private static readonly string CdnUrl =
        $"https://s9.booth.pm/00000000-1111-2222-3333-444444444444/f/{ItemId}/{DownloadId}/作り物.zip?Expires=1&{Signature}";

    private static readonly DownloadSource Known = new() { DownloadableId = 1111, ItemUrl = "https://booth.pm/ja/items/1111" };

    private readonly string _root;
    private readonly string _source;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly ImportPipeline _pipeline;
    private readonly ItemService _service;

    public FileDownloadSourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "chmonos-dlsrc-" + Guid.NewGuid().ToString("N"));
        _source = Path.Combine(_root, "source");
        Directory.CreateDirectory(_source);

        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        // 本物の BOOTH へは行かない。商品の JSON を聞かれたら作り物を返す
        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var client = new BoothClient(new HttpClient(new FakeBooth()), settings, TestWait.None);
        _pipeline = new ImportPipeline(_store, client, new ImagePipeline(client, _paths, settings), settings);
        _service = new ItemService(_store, client, new ImagePipeline(client, _paths, settings));
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

    private sealed class FakeBooth : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(request.RequestUri!.ToString().EndsWith(".json", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                        {
                          "id": {{ItemId}},
                          "name": "作り物の商品",
                          "shop": { "name": "alpha", "subdomain": "alpha", "url": "https://alpha.booth.pm/" },
                          "images": [],
                          "variations": [ { "id": 1, "name": null, "price": 100 } ]
                        }
                        """),
                }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html><body></body></html>") });
    }

    /// <summary>中に1ファイル入った zip。<paramref name="hostUrl"/> があればダウンロード元の記録を付ける。</summary>
    private async Task<(string Path, string Hash)> MakeZipAsync(string name, string? hostUrl = null, string? referrerUrl = null, string? folder = null)
    {
        var directory = folder ?? _source;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var stream = archive.CreateEntry(name + ".unitypackage").Open();
            stream.Write(new byte[64]);
        }

        if (hostUrl is not null || referrerUrl is not null)
        {
            File.WriteAllText(
                path + ":Zone.Identifier",
                "[ZoneTransfer]\r\nZoneId=3\r\n"
                + (referrerUrl is null ? "" : $"ReferrerUrl={referrerUrl}\r\n")
                + (hostUrl is null ? "" : $"HostUrl={hostUrl}\r\n"));
        }

        return (path, await FileHasher.ComputeSha256Async(path));
    }

    private async Task SaveItemAsync(params LocalFileRecord[] files)
        => await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { Name = "作り物の商品", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { LocalFiles = files, NextFetchDueAt = DateTimeOffset.Now.AddDays(30) },
        });

    private async Task<LocalFileRecord> FileOfAsync(string hash, string itemId = ItemId)
        => Assert.Single((await _store.Items.LoadAsync(itemId))!.Local.LocalFiles,
            file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase));

    private string ItemJson(string itemId = ItemId) => File.ReadAllText(_paths.ItemFile(itemId));

    // ---- 読んだ記録から作る値 ----

    [Fact]
    public void 署名付きのCDNのURLからは番号と組み直した商品ページだけを残す()
    {
        var source = DownloadSources.From(CdnUrl, "https://accounts.booth.pm/");

        Assert.Equal(DownloadId, source.DownloadableId);
        Assert.Equal($"https://booth.pm/ja/items/{ItemId}", source.ItemUrl);
    }

    [Fact]
    public void 商品ページから落とした記録は商品ページのURLを使う()
    {
        var source = DownloadSources.From("https://booth.pm/downloadables/8800821", $"https://shop-a.booth.pm/items/{ItemId}?ref=x");

        Assert.Equal(DownloadId, source.DownloadableId);
        Assert.Equal($"https://shop-a.booth.pm/items/{ItemId}", source.ItemUrl);
    }

    [Theory]
    [InlineData("https://example.com/f/1/2/a.zip?Signature=x", null)]
    [InlineData("https://accounts.booth.pm", null)]
    [InlineData(null, @"D:\落とした物\作り物.zip")]
    [InlineData("https://booth.pm.evil.com/ja/items/9900821", "https://evil.example/booth.pm/ja/items/9900821")]
    public void BOOTHの物でなければ何も残さない(string? host, string? referrer)
        => Assert.True(DownloadSources.From(host, referrer).IsEmpty);

    // ---- 新しく行ができる道 ----

    [Fact]
    public async Task 取り込みでできた行に残し_署名はJSONに書かない()
    {
        var (_, hash) = await MakeZipAsync("cdn.zip", CdnUrl);

        await _pipeline.RunAsync(new ImportWorkSet([_source]));

        var file = await FileOfAsync(hash);
        Assert.Equal(DownloadId, file.DownloadSource?.DownloadableId);
        Assert.Equal($"https://booth.pm/ja/items/{ItemId}", file.DownloadSource?.ItemUrl);
        Assert.DoesNotContain("SECRET", ItemJson(), StringComparison.Ordinal);
        Assert.DoesNotContain("s9.booth.pm", ItemJson(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 手で結んだ行は結ぶ時に読み_印が無ければ読んだ印だけ残す()
    {
        await SaveItemAsync();
        var (withZone, hashA) = await MakeZipAsync("a.zip", CdnUrl);
        var (noZone, hashB) = await MakeZipAsync("b.zip");

        Assert.Equal(FileAttachResult.Attached, (await _service.AttachFileAsync(ItemId, withZone)).Result);
        Assert.Equal(FileAttachResult.Attached, (await _service.AttachFileAsync(ItemId, noZone)).Result);

        Assert.Equal(DownloadId, (await FileOfAsync(hashA)).DownloadSource?.DownloadableId);
        Assert.True((await FileOfAsync(hashB)).DownloadSource is { IsEmpty: true });
    }

    [Fact]
    public async Task 未確定から選ぶと未確定の記録の値を運ぶ_無ければ未読のまま()
    {
        var (pathA, hashA) = await MakeZipAsync("u1.zip");
        var (pathB, hashB) = await MakeZipAsync("u2.zip");
        UnresolvedFile Unresolved(string hash, string path, string? host) => new()
        {
            Hash = hash,
            Paths = [path],
            SizeBytes = new FileInfo(path).Length,
            ModifiedAtUtc = DateTimeOffset.UtcNow,
            FirstSeenAt = DateTimeOffset.Now,
            ZoneHostUrl = host,
        };
        await _store.Unresolved.SaveAsync([Unresolved(hashA, pathA, CdnUrl), Unresolved(hashB, pathB, null)]);

        var idA = await _service.RegisterLocalItemAsync(hashA, "作り物A");
        var idB = await _service.RegisterLocalItemAsync(hashB, "作り物B");

        Assert.Equal(DownloadId, (await FileOfAsync(hashA, idA!)).DownloadSource?.DownloadableId);
        Assert.Null((await FileOfAsync(hashB, idB!)).DownloadSource);
        Assert.DoesNotContain("SECRET", ItemJson(idA!), StringComparison.Ordinal);
    }

    // ---- 書き直す道 ----

    [Fact]
    public void 突き合わせは知っている値を保ち_未読や空なら今読んだ値を入れる()
    {
        var read = new DownloadSource { DownloadableId = 2222 };
        LocalFileRecord Row(string hash, DownloadSource? source) => new() { Hash = hash, Paths = [@"Z:\x\" + hash + ".zip"], SizeBytes = 1, DownloadSource = source };

        var merged = LocalFileMerger.MergeByHand(
            [Row("AA", Known), Row("BB", null), Row("CC", new DownloadSource()), Row("DD", Known)],
            [Row("AA", read), Row("BB", read), Row("CC", read), Row("DD", new DownloadSource())]);

        Assert.Equal(Known, merged.Single(file => file.Hash == "AA").DownloadSource);
        Assert.Equal(read, merged.Single(file => file.Hash == "BB").DownloadSource);
        Assert.Equal(read, merged.Single(file => file.Hash == "CC").DownloadSource);
        Assert.Equal(Known, merged.Single(file => file.Hash == "DD").DownloadSource);
    }

    [Fact]
    public async Task 取り込みが印の無い写しの場所を足しても元の値を保つ()
    {
        // USB から写した（印の消えた）同じ中身を取り込んだ
        var (copy, hash) = await MakeZipAsync("copied.zip");
        await SaveItemAsync(new LocalFileRecord { Hash = hash, Paths = [Path.Combine(_root, "前の場所", "copied.zip")], SizeBytes = new FileInfo(copy).Length, DownloadSource = Known });

        // 手掛かりの無い zip は、持ち主のいる中身として場所を足す
        await _pipeline.RunAsync(new ImportWorkSet([_source]));

        var file = await FileOfAsync(hash);
        Assert.Contains(copy, file.Paths, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Known, file.DownloadSource);
    }

    [Fact]
    public void IDの付け替えとまとめで運ぶ()
    {
        var source = new LocalBlock { LocalFiles = [new LocalFileRecord { Hash = "AA", Paths = [@"Z:\a.zip"], SizeBytes = 1, DownloadSource = Known }] };
        var target = new LocalBlock { LocalFiles = [new LocalFileRecord { Hash = "AA", Paths = [@"Z:\b.zip"], SizeBytes = 1 }] };

        Assert.Equal(Known, ItemIdChange.Merge(source, new LocalBlock(), new HashSet<int>(), "1", "2").LocalFiles.Single().DownloadSource);
        Assert.Equal(Known, ItemIdChange.Merge(source, target, new HashSet<int>(), "1", "2").LocalFiles.Single().DownloadSource);
    }

    // ---- 起動時の見回り ----

    private (DownloadSourceSweep Sweep, List<string> Reads) NewSweep()
    {
        var reads = new List<string>();
        var sweep = new DownloadSourceSweep(_store, () => new FilePresenceProbe(), path =>
        {
            reads.Add(path);
            return BoothZipInspector.ZoneIdentifierReader.Read(path);
        });
        return (sweep, reads);
    }

    [Fact]
    public async Task 見回りは未読の行だけを1回読み_印の無い物にも読んだ印を残して読み直さない()
    {
        var (withZone, hashA) = await MakeZipAsync("old-a.zip", CdnUrl);
        var (noZone, hashB) = await MakeZipAsync("old-b.zip");
        var (already, hashC) = await MakeZipAsync("old-c.zip", CdnUrl);
        LocalFileRecord Row(string path, string hash, DownloadSource? source = null)
            => new() { Hash = hash, Paths = [path], SizeBytes = new FileInfo(path).Length, DownloadSource = source };
        await SaveItemAsync(Row(withZone, hashA), Row(noZone, hashB), Row(already, hashC, Known));

        var (sweep, reads) = NewSweep();
        var written = await sweep.SweepAsync();

        Assert.Equal([ItemId], written);
        Assert.Equal(2, reads.Count);
        Assert.Equal(DownloadId, (await FileOfAsync(hashA)).DownloadSource?.DownloadableId);
        Assert.True((await FileOfAsync(hashB)).DownloadSource is { IsEmpty: true });
        Assert.Equal(Known, (await FileOfAsync(hashC)).DownloadSource);
        Assert.DoesNotContain("SECRET", ItemJson(), StringComparison.Ordinal);

        var again = await sweep.SweepAsync();
        Assert.Empty(again);
        Assert.Equal(2, reads.Count);
    }

    [Fact]
    public async Task 見回りは別の中身かもしれない場所を読まず_無い行は印を付けずに次へ残す()
    {
        var (resized, hashA) = await MakeZipAsync("resized.zip", CdnUrl);
        var (cachedOther, hashB) = await MakeZipAsync("cached.zip", CdnUrl);
        var hashC = new string('C', 64);
        await SaveItemAsync(
            new LocalFileRecord { Hash = hashA, Paths = [resized], SizeBytes = new FileInfo(resized).Length + 1 },
            new LocalFileRecord { Hash = hashB, Paths = [cachedOther], SizeBytes = new FileInfo(cachedOther).Length },
            new LocalFileRecord { Hash = hashC, Paths = [Path.Combine(_root, "無い", "gone.zip")], SizeBytes = 1 });

        // 走査の控えは、この場所に別の中身があると言っている（同じ名前で上書きした更新版を、まだ取り込んでいない）
        var info = new FileInfo(cachedOther);
        await _store.ScanCache.SaveAsync([new ScanCacheEntry
        {
            Path = cachedOther,
            SizeBytes = info.Length,
            ModifiedAtUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            Hash = new string('E', 64),
        }]);

        var (sweep, reads) = NewSweep();
        Assert.Empty(await sweep.SweepAsync());

        Assert.Empty(reads);
        Assert.Null((await FileOfAsync(hashA)).DownloadSource);
        Assert.Null((await FileOfAsync(hashB)).DownloadSource);
        Assert.Null((await FileOfAsync(hashC)).DownloadSource);
    }

    [Fact]
    public async Task 欄の無い古い行は書き戻しても欄を書かない()
    {
        await SaveItemAsync(new LocalFileRecord { Hash = new string('D', 64), Paths = [Path.Combine(_root, "古い.zip")], SizeBytes = 1 });

        Assert.True(await _store.Items.ChangeLocalAsync(ItemId, local => local with { Memo = "書き戻す" }, [LocalField.Memo]));

        Assert.DoesNotContain("downloadSource", ItemJson(), StringComparison.Ordinal);
    }
}
