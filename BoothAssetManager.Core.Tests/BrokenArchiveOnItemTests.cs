using System.IO.Compression;
using System.Net;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 商品に結び付いた壊れた zip にも、開けなかった印が付くこと（ユーザ判断 2026-09-30）。
///
/// ダウンロード元の記録から商品が1つに決まると未確定を通らないので、前は印がどこにも出ず、
/// 商品ページでは中身の一覧が空なだけだった。壊れた zip は試験の中で作る（<see cref="BrokenArchiveImportTests"/> と同じ作り方）。
/// </summary>
public class BrokenArchiveOnItemTests : IDisposable
{
    private const string ItemId = "1111111";
    private const string OtherId = "2222222";
    private const string ItemName = "試験の商品";

    private readonly string _root;
    private readonly string _source;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly ImportPipeline _pipeline;
    private readonly ItemService _service;

    public BrokenArchiveOnItemTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-broken-item-" + Guid.NewGuid().ToString("N"));
        _source = Path.Combine(_root, "source");
        Directory.CreateDirectory(_source);

        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var client = new BoothClient(new HttpClient(new Handler()), settings);
        var images = new ImagePipeline(client, _paths, settings);
        _pipeline = new ImportPipeline(_store, client, images, settings);
        _service = new ItemService(_store, client, images);
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

    /// <summary>どの商品IDにも、同じ形の商品を返す（名前は ID ごとに変える）。商品ページは空。</summary>
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (!url.EndsWith(".json", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html><body></body></html>"),
                });
            }

            var id = url.Contains(OtherId, StringComparison.Ordinal) ? OtherId : ItemId;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""
                    {
                      "id": {{id}},
                      "name": "{{NameOf(id)}}",
                      "shop": { "name": "alpha", "subdomain": "alpha", "url": "https://alpha.booth.pm/" },
                      "images": [],
                      "variations": [ { "id": 1, "name": null, "price": 100 } ]
                    }
                    """),
            });
        }
    }

    private static string NameOf(string itemId) => itemId == ItemId ? ItemName : "もう1つの商品";

    /// <summary>開ける zip。中身は圧縮せずに入れ、途中で切ったときに目録（末尾）が確実に無くなる大きさにする。</summary>
    private string GoodZip(string name, string? itemId = ItemId)
    {
        var path = Path.Combine(_source, name);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var entryName in new[] { "model/body.fbx", "textures/skin.png", "readme.txt" })
            {
                var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
                using var stream = entry.Open();
                stream.Write(new byte[4096]);
            }
        }

        MarkSource(path, itemId);
        return path;
    }

    /// <summary>途中で切れたダウンロード：本物の zip の前半だけ。</summary>
    private string TruncatedZip(string name, string? itemId = ItemId)
    {
        var path = GoodZip(name, itemId: null);
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);
        MarkSource(path, itemId);
        return path;
    }

    /// <summary>ダウンロード元の記録を付ける（商品IDが1つに決まり、未確定を通らない）。null なら付けない。</summary>
    private static void MarkSource(string path, string? itemId)
    {
        if (itemId is not null)
        {
            File.WriteAllText(
                path + ":Zone.Identifier",
                $"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://booth.pm/ja/items/{itemId}\r\n");
        }
    }

    private Task SaveItemAsync(string itemId, params LocalFileRecord[] files)
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = itemId,
            Booth = new BoothBlock { Name = NameOf(itemId), FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { LocalFiles = files },
        });

    private async Task<IReadOnlyList<LocalFileRecord>> FilesOfAsync(string itemId)
    {
        var item = await _store.Items.LoadAsync(itemId);
        Assert.NotNull(item);
        return item!.Local.LocalFiles;
    }

    private Task<ImportSummary> ImportAsync() => _pipeline.RunAsync(new ImportWorkSet([_source]));

    /// <summary>ダウンロード元の記録から新しい商品が決まった壊れた zip。未確定には行かず、商品の記録に印が付く。</summary>
    [Fact]
    public async Task MarksTheFileOfANewItem()
    {
        var path = TruncatedZip("cut.zip");

        var summary = await ImportAsync();

        var file = Assert.Single(await FilesOfAsync(ItemId));
        Assert.Contains(path, file.Paths);
        Assert.True(file.ArchiveBroken);
        Assert.Empty(file.Contents);
        Assert.Empty(_store.Unresolved.Load());

        // 未確定の数とは別に数え、どの商品かを名前で言えるようにする
        Assert.Equal(1, summary.ItemsAdded);
        Assert.Equal(0, summary.FilesBrokenArchive);
        Assert.Equal(1, summary.FilesBrokenArchiveOnItems);
        Assert.Equal([ItemName], summary.BrokenArchiveItemNames);
    }

    [Fact]
    public async Task MarksTheFileAddedToAnExistingItem()
    {
        await SaveItemAsync(ItemId);
        TruncatedZip("cut.zip");

        var summary = await ImportAsync();

        Assert.True(Assert.Single(await FilesOfAsync(ItemId)).ArchiveBroken);
        Assert.Equal(0, summary.ItemsAdded);
        Assert.Equal(1, summary.FilesBrokenArchiveOnItems);
        Assert.Equal([ItemName], summary.BrokenArchiveItemNames);
    }

    /// <summary>記録に書くのは壊れていた事実だけ。開けた物に false を並べない（人が開いて読む JSON）。</summary>
    [Fact]
    public async Task WritesTheMarkOnlyOnBrokenOnes()
    {
        var good = GoodZip("good.zip");
        TruncatedZip("cut.zip");

        var summary = await ImportAsync();

        var files = await FilesOfAsync(ItemId);
        Assert.Equal(2, files.Count);
        Assert.False(files.Single(file => file.Paths.Contains(good)).ArchiveBroken);
        Assert.Equal(1, summary.FilesBrokenArchiveOnItems);

        var json = File.ReadAllText(_paths.ItemFile(ItemId));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"archiveBroken\""));
        Assert.Contains("\"archiveBroken\": true", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// 取り込み直しても印が残り、結果にも同じ数が出る（壊れた zip は中身の一覧が空なので、持っていても毎回開き直す）。
    /// </summary>
    [Fact]
    public async Task KeepsTheMarkAndTheCountOnReimport()
    {
        TruncatedZip("cut.zip");

        await ImportAsync();
        var summary = await ImportAsync();

        Assert.True(Assert.Single(await FilesOfAsync(ItemId)).ArchiveBroken);
        Assert.Equal(1, summary.FilesBrokenArchiveOnItems);
        Assert.Equal([ItemName], summary.BrokenArchiveItemNames);
    }

    /// <summary>
    /// 同じ場所に落とし直したら、壊れた方の記録ごと印が消える。
    /// 場所に何かが在るかだけを見ていると、壊れた方の記録が「その場所に在る」まま残り、商品ページの札が消えなかった。
    /// </summary>
    [Fact]
    public async Task DownloadingAgainClearsTheMark()
    {
        var path = TruncatedZip("pack.zip");
        await ImportAsync();
        Assert.True(Assert.Single(await FilesOfAsync(ItemId)).ArchiveBroken);

        File.Delete(path);
        GoodZip("pack.zip");
        var summary = await ImportAsync();

        var file = Assert.Single(await FilesOfAsync(ItemId));
        Assert.False(file.ArchiveBroken);
        Assert.Equal(3, file.Contents.Count);
        Assert.Contains(path, file.Paths);
        Assert.Equal(0, summary.FilesBrokenArchiveOnItems);
        Assert.Empty(summary.BrokenArchiveItemNames);
    }

    /// <summary>
    /// 別の名前で落とし直して、壊れた方も残っているなら、壊れた方の印は残る（まだそこに在るので）。
    /// </summary>
    [Fact]
    public async Task KeepsTheMarkWhileTheBrokenOneIsStillThere()
    {
        var cut = TruncatedZip("pack.zip");
        await ImportAsync();

        var good = GoodZip("pack (1).zip");
        var summary = await ImportAsync();

        var files = await FilesOfAsync(ItemId);
        Assert.Equal(2, files.Count);
        Assert.True(files.Single(file => file.Paths.Contains(cut)).ArchiveBroken);
        Assert.False(files.Single(file => file.Paths.Contains(good)).ArchiveBroken);
        Assert.Equal(1, summary.FilesBrokenArchiveOnItems);
    }

    /// <summary>
    /// 未確定から商品へ登録するとき、印を引き継ぐ。壊れていても登録は止めないので、引き継がないと商品ページで印が消える。
    /// 手で結んだ物は取り込み直しても「持っている物」として数に入り、同じ場所に落とし直せば記録ごと消える
    /// （手掛かりが無いので、落とし直した物は未確定に出る）。
    /// </summary>
    [Fact]
    public async Task CarriesTheMarkFromUnresolvedToTheItem()
    {
        await SaveItemAsync(ItemId);
        var path = TruncatedZip("cut.zip", itemId: null);
        await ImportAsync();
        var unresolved = Assert.Single(_store.Unresolved.Load());
        Assert.True(unresolved.ArchiveBroken);

        Assert.True(await _service.AssignItemIdAsync(unresolved.Hash, ItemId));

        Assert.True(Assert.Single(await FilesOfAsync(ItemId)).ArchiveBroken);
        Assert.Empty(_store.Unresolved.Load());

        var again = await ImportAsync();
        Assert.Equal(0, again.FilesBrokenArchive);
        Assert.Equal(1, again.FilesBrokenArchiveOnItems);
        Assert.Equal([ItemName], again.BrokenArchiveItemNames);

        File.Delete(path);
        GoodZip("cut.zip", itemId: null);
        var replaced = await ImportAsync();

        Assert.Empty(await FilesOfAsync(ItemId));
        Assert.False(Assert.Single(_store.Unresolved.Load()).ArchiveBroken);
        Assert.Equal(0, replaced.FilesBrokenArchiveOnItems);
    }

    /// <summary>「BOOTHに無い商品として登録」も同じ記録の作り方を通る。</summary>
    [Fact]
    public async Task CarriesTheMarkWhenRegisteringAsALocalItem()
    {
        TruncatedZip("cut.zip", itemId: null);
        await ImportAsync();
        var unresolved = Assert.Single(_store.Unresolved.Load());

        var itemId = await _service.RegisterLocalItemAsync(unresolved.Hash, "手元だけの商品");

        Assert.NotNull(itemId);
        Assert.True(Assert.Single(await FilesOfAsync(itemId!)).ArchiveBroken);
    }

    /// <summary>
    /// 商品から外して未確定へ戻すとき、印を引き継ぐ。未確定の画面は開き直して確かめないので、
    /// 落とすと次の取り込みまで普通の未確定に見える。戻したときも印は残る。
    /// </summary>
    [Fact]
    public async Task CarriesTheMarkWhenDetachingAndReattaching()
    {
        TruncatedZip("cut.zip");
        await ImportAsync();
        var hash = Assert.Single(await FilesOfAsync(ItemId)).Hash;

        Assert.Equal(DetachOutcome.ItemNowEmpty, await _service.DetachFileAsync(ItemId, hash, deleteItemWhenEmpty: false));

        Assert.True(Assert.Single(_store.Unresolved.Load()).ArchiveBroken);
        var detached = Assert.Single(await FilesOfAsync(ItemId));
        Assert.True(detached.Detached);
        Assert.True(detached.ArchiveBroken);

        Assert.Equal(ReattachOutcome.Reattached, await _service.ReattachFileAsync(ItemId, hash));

        var back = Assert.Single(await FilesOfAsync(ItemId));
        Assert.False(back.Detached);
        Assert.True(back.ArchiveBroken);
        Assert.Empty(_store.Unresolved.Load());
    }

    /// <summary>
    /// 外した後の取り込みでは、壊れた zip は未確定の数に入り、外した商品の数には入らない（外した行は持ち物ではない）。
    /// </summary>
    [Fact]
    public async Task CountsADetachedBrokenZipAsUnresolved()
    {
        TruncatedZip("cut.zip");
        await ImportAsync();
        var hash = Assert.Single(await FilesOfAsync(ItemId)).Hash;
        await _service.DetachFileAsync(ItemId, hash, deleteItemWhenEmpty: false);

        var summary = await ImportAsync();

        Assert.Equal(1, summary.FilesBrokenArchive);
        Assert.Equal(0, summary.FilesBrokenArchiveOnItems);
        Assert.True(Assert.Single(_store.Unresolved.Load()).ArchiveBroken);
    }

    /// <summary>
    /// 手で結んだ壊れた zip は場所が変わらないので、取り込みが商品に書く機会が無かった。
    /// 印の無い記録（この印ができる前に結んだ物）にも、取り込み直すと付く。
    /// </summary>
    [Fact]
    public async Task ReimportMarksAHandLinkedFileThatHadNoMark()
    {
        var path = TruncatedZip("cut.zip", itemId: null);
        var hash = await FileHasher.ComputeSha256Async(path);
        await SaveItemAsync(ItemId, new LocalFileRecord { Hash = hash, Paths = [path], SizeBytes = new FileInfo(path).Length });

        var summary = await ImportAsync();

        var file = Assert.Single(await FilesOfAsync(ItemId));
        Assert.True(file.ArchiveBroken);
        Assert.Equal([path], file.Paths);
        Assert.Empty(_store.Unresolved.Load());
        Assert.Equal(1, summary.FilesBrokenArchiveOnItems);
    }

    /// <summary>
    /// 同じ壊れた zip を2つの商品が持つなら、zip は1件、商品は2つと数える（同じ中身を2回数えない）。
    /// </summary>
    [Fact]
    public async Task CountsOneFileHeldByTwoItemsOnce()
    {
        var path = TruncatedZip("cut.zip");
        var hash = await FileHasher.ComputeSha256Async(path);
        var record = new LocalFileRecord { Hash = hash, Paths = [path], SizeBytes = new FileInfo(path).Length };
        await SaveItemAsync(ItemId, record);
        await SaveItemAsync(OtherId, record);

        var summary = await ImportAsync();

        Assert.True(Assert.Single(await FilesOfAsync(ItemId)).ArchiveBroken);
        Assert.True(Assert.Single(await FilesOfAsync(OtherId)).ArchiveBroken);
        Assert.Equal(1, summary.FilesBrokenArchiveOnItems);
        Assert.Equal(2, summary.BrokenArchiveItemNames.Count);
    }

    /// <summary>
    /// 突き合わせ：同じ中身をもう一度見たとき、印は新しく見た方の答えに合わせる。
    /// 見つけた方が開いていない（中身の一覧が空で、印も無い）ときは、壊れていないと分かったわけではないので残す。
    /// </summary>
    [Fact]
    public void MergeFollowsTheLatestLook()
    {
        var broken = new LocalFileRecord { Hash = "AA", Paths = ["a.zip"], SizeBytes = 1, ArchiveBroken = true };
        var unopened = new LocalFileRecord { Hash = "AA", Paths = ["b.zip"], SizeBytes = 1 };
        var opened = new LocalFileRecord { Hash = "AA", Paths = ["b.zip"], SizeBytes = 1, Contents = ["readme.txt"] };

        var kept = Assert.Single(LocalFileMerger.Merge([broken], [unopened], pathExists: _ => true));
        Assert.True(kept.ArchiveBroken);
        Assert.Equal(2, kept.Paths.Count);

        var cleared = Assert.Single(LocalFileMerger.Merge([broken], [opened], pathExists: _ => true));
        Assert.False(cleared.ArchiveBroken);
        Assert.Equal(["readme.txt"], cleared.Contents);

        var marked = Assert.Single(LocalFileMerger.Merge([unopened], [broken], pathExists: _ => true));
        Assert.True(marked.ArchiveBroken);
    }

    /// <summary>突き合わせで無くなった場所を落とすとき、印を落とさない（場所だけを差し替える）。</summary>
    [Fact]
    public void MergeKeepsTheMarkWhenAPathIsGone()
    {
        var broken = new LocalFileRecord
        {
            Hash = "AA",
            Paths = ["gone.zip", "here.zip"],
            SizeBytes = 1,
            ArchiveBroken = true,
        };

        var merged = Assert.Single(LocalFileMerger.Merge(
            [broken], [], pathExists: path => path == "here.zip", onMissingVolume: _ => false));

        Assert.Equal(["here.zip"], merged.Paths);
        Assert.True(merged.ArchiveBroken);
    }
}
