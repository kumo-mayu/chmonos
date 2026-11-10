using System.IO.Compression;
using System.Net;
using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 手元のファイルの行に、最初に記録した日時（<see cref="LocalFileRecord.RegisteredAt"/>）を書くこと（ユーザ判断 2026-11-10）。
/// 新しく行ができる道では今の時刻が入り、今ある行を書き直す道（場所の追加・外す・戻す）と、商品 ID の付け替え・まとめでは元の日時が残る。
/// 欄の無い古い行は空のまま（推して埋めない）。サービスは時計を差し替える口を持たないので、新しい行は呼ぶ前後の時刻の間にあるかで見る。
/// </summary>
public class FileRegisteredAtTests : IDisposable
{
    private const string ItemId = "9900811";

    /// <summary>元の日時。今の時刻と取り違えないよう、ずっと前の日にする。</summary>
    private static readonly DateTimeOffset Earlier = new(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(9));

    private readonly string _root;
    private readonly string _source;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly ImportPipeline _pipeline;
    private readonly ItemService _service;

    public FileRegisteredAtTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "chmonos-regat-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>中に1ファイル入った zip。<paramref name="fromBooth"/> なら、この商品から落としたというダウンロード元の記録を付ける。</summary>
    private async Task<(string Path, string Hash)> MakeZipAsync(string name, bool fromBooth = false)
    {
        var path = Path.Combine(_source, name);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var stream = archive.CreateEntry("作り物.unitypackage").Open();
            stream.Write(new byte[64]);
        }

        if (fromBooth)
        {
            File.WriteAllText(path + ":Zone.Identifier", $"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://booth.pm/ja/items/{ItemId}\r\n");
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

    private async Task<LocalFileRecord> FileOfAsync(string hash)
        => Assert.Single((await _store.Items.LoadAsync(ItemId))!.Local.LocalFiles,
            file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase));

    private static void AssertBetween(DateTimeOffset before, DateTimeOffset? actual, DateTimeOffset after)
    {
        Assert.NotNull(actual);
        Assert.InRange(actual!.Value, before, after);
    }

    // ---- 新しく行ができる道 ----

    [Fact]
    public async Task 取り込みで新しい商品にできた行に今の日時が入る()
    {
        var (_, hash) = await MakeZipAsync("new.zip", fromBooth: true);

        var before = DateTimeOffset.Now;
        await _pipeline.RunAsync(new ImportWorkSet([_source]));
        var after = DateTimeOffset.Now;

        AssertBetween(before, (await FileOfAsync(hash)).RegisteredAt, after);
    }

    [Fact]
    public async Task 取り込みで既にある商品に足した行に今の日時が入り_前からの行は元の日時のまま()
    {
        await SaveItemAsync(new LocalFileRecord { Hash = new string('B', 64), Paths = [Path.Combine(_root, "前からの.zip")], SizeBytes = 1, RegisteredAt = Earlier });
        var (_, hash) = await MakeZipAsync("added.zip", fromBooth: true);

        var before = DateTimeOffset.Now;
        await _pipeline.RunAsync(new ImportWorkSet([_source]));
        var after = DateTimeOffset.Now;

        AssertBetween(before, (await FileOfAsync(hash)).RegisteredAt, after);
        Assert.Equal(Earlier, (await FileOfAsync(new string('B', 64))).RegisteredAt);
    }

    [Fact]
    public async Task 未確定から仮の商品として登録した行に今の日時が入る()
    {
        var (_, hash) = await MakeZipAsync("loose.zip");
        await _pipeline.RunAsync(new ImportWorkSet([_source]));
        Assert.Contains(_store.Unresolved.Load(), file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase));

        var before = DateTimeOffset.Now;
        var id = await _service.RegisterLocalItemAsync(hash, "手で付けた名前");
        var after = DateTimeOffset.Now;

        var file = Assert.Single((await _store.Items.LoadAsync(id!))!.Local.LocalFiles);
        AssertBetween(before, file.RegisteredAt, after);
    }

    [Fact]
    public async Task 商品ページから手で結んだ行に今の日時が入る()
    {
        await SaveItemAsync();
        var (zip, hash) = await MakeZipAsync("hand.zip");

        var before = DateTimeOffset.Now;
        Assert.Equal(FileAttachResult.Attached, (await _service.AttachFileAsync(ItemId, zip)).Result);
        var after = DateTimeOffset.Now;

        AssertBetween(before, (await FileOfAsync(hash)).RegisteredAt, after);
    }

    // ---- 今ある行を書き直す道 ----

    [Fact]
    public async Task 取り込みが別の場所を足しても元の日時を保つ()
    {
        var (zip, hash) = await MakeZipAsync("moved.zip", fromBooth: true);
        await SaveItemAsync(new LocalFileRecord { Hash = hash, Paths = [Path.Combine(_root, "前の場所", "moved.zip")], SizeBytes = new FileInfo(zip).Length, RegisteredAt = Earlier });

        await _pipeline.RunAsync(new ImportWorkSet([_source]));

        var file = await FileOfAsync(hash);
        Assert.Contains(zip, file.Paths, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Earlier, file.RegisteredAt);
    }

    [Fact]
    public async Task 欄の無い行は取り込みが場所を足しても空のまま()
    {
        var (zip, hash) = await MakeZipAsync("old.zip", fromBooth: true);
        await SaveItemAsync(new LocalFileRecord { Hash = hash, Paths = [Path.Combine(_root, "前の場所", "old.zip")], SizeBytes = new FileInfo(zip).Length });

        await _pipeline.RunAsync(new ImportWorkSet([_source]));

        var file = await FileOfAsync(hash);
        Assert.Contains(zip, file.Paths, StringComparer.OrdinalIgnoreCase);
        Assert.Null(file.RegisteredAt);
    }

    [Fact]
    public async Task 手で同じファイルを結び直しても元の日時を保つ()
    {
        var (zip, hash) = await MakeZipAsync("again.zip");
        await SaveItemAsync(new LocalFileRecord { Hash = hash, Paths = [Path.Combine(_root, "前の場所", "again.zip")], SizeBytes = new FileInfo(zip).Length, RegisteredAt = Earlier });

        Assert.Equal(FileAttachResult.Attached, (await _service.AttachFileAsync(ItemId, zip)).Result);

        Assert.Equal(Earlier, (await FileOfAsync(hash)).RegisteredAt);
    }

    [Fact]
    public async Task 外して戻しても元の日時を保つ()
    {
        var (zip, hash) = await MakeZipAsync("detach.zip");
        await SaveItemAsync(
            new LocalFileRecord { Hash = hash, Paths = [zip], SizeBytes = new FileInfo(zip).Length, RegisteredAt = Earlier },
            new LocalFileRecord { Hash = new string('C', 64), Paths = [Path.Combine(_root, "残す.zip")], SizeBytes = 1 });

        await _service.DetachFileAsync(ItemId, hash, deleteItemWhenEmpty: false);
        Assert.Equal(Earlier, (await FileOfAsync(hash)).RegisteredAt);

        Assert.Equal(ReattachOutcome.Reattached, await _service.ReattachFileAsync(ItemId, hash));
        Assert.Equal(Earlier, (await FileOfAsync(hash)).RegisteredAt);
    }

    // ---- 突き合わせ・ID の付け替え ----

    [Fact]
    public void 突き合わせは今ある行の日時を保ち_欄の無い行を埋めない()
    {
        var now = DateTimeOffset.Now;
        LocalFileRecord Row(string hash, DateTimeOffset? at) => new() { Hash = hash, Paths = [@"Z:\x\" + hash + ".zip"], SizeBytes = 1, RegisteredAt = at };

        var merged = LocalFileMerger.MergeByHand(
            [Row("AA", Earlier), Row("BB", null)],
            [Row("AA", now), Row("BB", now), Row("CC", now)]);

        Assert.Equal(Earlier, merged.Single(file => file.Hash == "AA").RegisteredAt);
        Assert.Null(merged.Single(file => file.Hash == "BB").RegisteredAt);
        Assert.Equal(now, merged.Single(file => file.Hash == "CC").RegisteredAt);
    }

    [Fact]
    public void IDの付け替えとまとめでは移す元の日時を保ち_両方にあれば早い方()
    {
        var later = Earlier.AddDays(10);
        var source = new LocalBlock
        {
            LocalFiles =
            [
                new LocalFileRecord { Hash = "AA", Paths = [@"Z:\a.zip"], SizeBytes = 1, RegisteredAt = Earlier },
                new LocalFileRecord { Hash = "BB", Paths = [@"Z:\b.zip"], SizeBytes = 1, RegisteredAt = Earlier },
                new LocalFileRecord { Hash = "DD", Paths = [@"Z:\d.zip"], SizeBytes = 1 },
            ],
        };
        var target = new LocalBlock
        {
            LocalFiles = [new LocalFileRecord { Hash = "BB", Paths = [@"Z:\b2.zip"], SizeBytes = 1, RegisteredAt = later }],
        };

        var merged = ItemIdChange.Merge(source, target, new HashSet<int>(), "1", "2").LocalFiles;

        Assert.Equal(Earlier, merged.Single(file => file.Hash == "AA").RegisteredAt);
        Assert.Equal(Earlier, merged.Single(file => file.Hash == "BB").RegisteredAt);
        Assert.Null(merged.Single(file => file.Hash == "DD").RegisteredAt);
    }

    // ---- JSON ----

    [Fact]
    public async Task 欄の無い古い行を読んで書き戻しても欄は書かれない_持つ行は書かれる()
    {
        await SaveItemAsync(new LocalFileRecord { Hash = new string('D', 64), Paths = [Path.Combine(_root, "古い.zip")], SizeBytes = 1 });
        Assert.DoesNotContain("registeredAt", File.ReadAllText(_paths.ItemFile(ItemId)), StringComparison.Ordinal);

        Assert.True(await _store.Items.ChangeLocalAsync(ItemId, local => local with { Memo = "書き戻す" }, [LocalField.Memo]));

        Assert.DoesNotContain("registeredAt", File.ReadAllText(_paths.ItemFile(ItemId)), StringComparison.Ordinal);
        Assert.Null((await FileOfAsync(new string('D', 64))).RegisteredAt);

        await SaveItemAsync(new LocalFileRecord { Hash = new string('E', 64), Paths = [Path.Combine(_root, "新しい.zip")], SizeBytes = 1, RegisteredAt = Earlier });
        Assert.Contains("\"registeredAt\"", File.ReadAllText(_paths.ItemFile(ItemId)), StringComparison.Ordinal);
        Assert.Equal(Earlier, (await FileOfAsync(new string('E', 64))).RegisteredAt);
    }
}
