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
/// 同じ場所の中身が変わったら、古い中身の記録からその場所を外すこと（ユーザ判断 2026-09-30「3A」）と、
/// 新しい中身が未確定に出たとき、同じ場所にあった商品を候補として残すこと（同「2A」）。
///
/// 前は場所に何かが在るかしか見ていなかったので、更新版を同じ名前で上書きすると、古い中身の記録も「その場所に在る」として残り、
/// 商品ページに同じ名前の行が2つ並んだ。手で結んだ物を上書きすると、新しい方は未確定に出て、どの商品の物だったかが分からなくなった。
/// </summary>
public class ReplacedAtSamePathTests : IDisposable
{
    private const string ItemId = "1111111";
    private const string OtherId = "2222222";

    private readonly string _root;
    private readonly string _source;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly ImportPipeline _pipeline;
    private readonly ItemService _service;

    public ReplacedAtSamePathTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-replaced-" + Guid.NewGuid().ToString("N"));
        _source = Path.Combine(_root, "source");
        Directory.CreateDirectory(_source);

        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var client = new BoothClient(new HttpClient(new Handler()), settings, TestWait.None);
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

    /// <summary>どの商品IDにも、同じ形の商品を返す。商品ページは空。</summary>
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
                      "name": "試験の商品 {{id}}",
                      "shop": { "name": "alpha", "subdomain": "alpha", "url": "https://alpha.booth.pm/" },
                      "images": [],
                      "variations": [ { "id": 1, "name": null, "price": 100 } ]
                    }
                    """),
            });
        }
    }

    /// <summary>
    /// zip を置く（在れば上書きする）。版ごとに中のファイルの名前と数を変えるので、版が違えば中身（ハッシュ）も大きさも違う。
    /// </summary>
    /// <param name="itemId">ダウンロード元の記録に書く商品ID（商品が1つに決まり、未確定を通らない）。null なら付けない（手掛かり無し）。</param>
    private string PutZip(string name, int version, string? itemId, string? folder = null)
    {
        var path = Path.Combine(folder ?? _source, name);
        File.Delete(path);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            for (var index = 0; index <= version; index++)
            {
                var entry = archive.CreateEntry($"v{version}/file{index}.txt", CompressionLevel.NoCompression);
                using var stream = entry.Open();
                stream.Write(new byte[1024]);
            }
        }

        if (itemId is not null)
        {
            File.WriteAllText(
                path + ":Zone.Identifier",
                $"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://booth.pm/ja/items/{itemId}\r\n");
        }

        return path;
    }

    /// <summary>途中で切れたダウンロード（手掛かり無し）。</summary>
    private string PutTruncatedZip(string name)
    {
        var path = PutZip(name, version: 9, itemId: null);
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);
        return path;
    }

    private Task SaveItemAsync(string itemId, params LocalFileRecord[] files)
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = itemId,
            Booth = new BoothBlock { Name = "試験の商品 " + itemId, FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { LocalFiles = files },
        });

    /// <summary>今そこに在るファイルを、手で結んだ物として商品の記録に書く。</summary>
    private static async Task<LocalFileRecord> RecordOfAsync(string path, bool detached = false) => new()
    {
        Hash = await FileHasher.ComputeSha256Async(path),
        Paths = [path],
        SizeBytes = new FileInfo(path).Length,
        Contents = ["readme.txt"],
        Detached = detached,
    };

    private async Task<IReadOnlyList<LocalFileRecord>> FilesOfAsync(string itemId)
    {
        var item = await _store.Items.LoadAsync(itemId);
        Assert.NotNull(item);
        return item!.Local.LocalFiles;
    }

    private Task<ImportSummary> ImportAsync() => _pipeline.RunAsync(new ImportWorkSet([_source]));

    /// <summary>
    /// 更新版を同じ名前で上書きした（手掛かりは同じ商品）。古い方は「見つかりません」になり、新しい方が1行だけ出る。
    /// 前は古い方の記録も同じ場所を指したままで、同じ名前の行が2つ並んだ。
    /// </summary>
    [Fact]
    public async Task OverwritingWithAnUpdateLeavesTheOldRecordWithoutAPlace()
    {
        var path = PutZip("pack.zip", version: 1, ItemId);
        await ImportAsync();
        var old = Assert.Single(await FilesOfAsync(ItemId));

        PutZip("pack.zip", version: 2, ItemId);
        await ImportAsync();

        var files = await FilesOfAsync(ItemId);
        Assert.Equal(2, files.Count);
        Assert.Empty(files.Single(file => file.Hash == old.Hash).Paths);
        var fresh = files.Single(file => file.Hash != old.Hash);
        Assert.Equal([path], fresh.Paths);
        Assert.Empty(_store.Unresolved.Load());
    }

    /// <summary>場所が残らなくなっても、記録は残す。種類の結び付きなど、人が付けた物を失わない。</summary>
    [Fact]
    public async Task KeepsWhatThePersonSetOnTheOldRecord()
    {
        var path = PutZip("pack.zip", version: 1, itemId: null);
        var record = await RecordOfAsync(path) with { VariationId = 1 };
        await SaveItemAsync(ItemId, record);

        PutZip("pack.zip", version: 2, itemId: null);
        await ImportAsync();

        var kept = Assert.Single(await FilesOfAsync(ItemId));
        Assert.Equal(record.Hash, kept.Hash);
        Assert.Empty(kept.Paths);
        Assert.Equal(1, kept.VariationId);
        Assert.Equal(["readme.txt"], kept.Contents);
    }

    /// <summary>ほかの場所にも同じ中身が在るなら、上書きされた場所だけを外す。</summary>
    [Fact]
    public async Task DropsOnlyTheOverwrittenPlace()
    {
        var path = PutZip("pack.zip", version: 1, itemId: null);
        var copy = Path.Combine(_source, "copy.zip");
        File.Copy(path, copy);
        var record = await RecordOfAsync(path) with { Paths = [path, copy] };
        await SaveItemAsync(ItemId, record);

        PutZip("pack.zip", version: 2, itemId: null);
        await ImportAsync();

        Assert.Equal([copy], Assert.Single(await FilesOfAsync(ItemId)).Paths);
    }

    /// <summary>
    /// 手で結んだ物を同じ名前で上書きした（手掛かり無し）。新しい方は未確定に出て、同じ場所にあった商品が候補に残る。
    /// 自動では結ばない。
    /// </summary>
    [Fact]
    public async Task AHandLinkedFileOverwrittenGoesToUnresolvedWithTheItemAsACandidate()
    {
        var path = PutZip("pack.zip", version: 1, itemId: null);
        var record = await RecordOfAsync(path);
        await SaveItemAsync(ItemId, record);

        PutZip("pack.zip", version: 2, itemId: null);
        await ImportAsync();

        Assert.Empty(Assert.Single(await FilesOfAsync(ItemId)).Paths);
        var unresolved = Assert.Single(_store.Unresolved.Load());
        Assert.NotEqual(record.Hash, unresolved.Hash);
        Assert.Equal([path], unresolved.Paths);
        Assert.Equal([ItemId], unresolved.SamePathItemIds);
        Assert.Empty(unresolved.CandidateItemIds);
        Assert.Contains("\"samePathItemIds\"", File.ReadAllText(_paths.UnresolvedFile), StringComparison.Ordinal);
    }

    /// <summary>
    /// 候補は次の取り込みでも残る。未確定の一覧は取り込みのたびに作り直し、古い記録はもうその場所を指していないので、
    /// 引き継がないと1回で消える。
    /// </summary>
    [Fact]
    public async Task TheCandidateSurvivesTheNextImport()
    {
        var path = PutZip("pack.zip", version: 1, itemId: null);
        await SaveItemAsync(ItemId, await RecordOfAsync(path));
        PutZip("pack.zip", version: 2, itemId: null);
        await ImportAsync();

        await ImportAsync();

        Assert.Equal([ItemId], Assert.Single(_store.Unresolved.Load()).SamePathItemIds);
    }

    /// <summary>人が候補を選んで登録すると、その商品に新しい方が1行入る（古い方は「見つかりません」のまま）。</summary>
    [Fact]
    public async Task ChoosingTheCandidateRegistersTheNewFile()
    {
        var path = PutZip("pack.zip", version: 1, itemId: null);
        var record = await RecordOfAsync(path);
        await SaveItemAsync(ItemId, record);
        PutZip("pack.zip", version: 2, itemId: null);
        await ImportAsync();
        var unresolved = Assert.Single(_store.Unresolved.Load());

        Assert.True(await _service.AssignItemIdAsync(unresolved.Hash, unresolved.SamePathItemIds[0]));

        var files = await FilesOfAsync(ItemId);
        Assert.Equal(2, files.Count);
        Assert.Empty(files.Single(file => file.Hash == record.Hash).Paths);
        Assert.Equal([path], files.Single(file => file.Hash == unresolved.Hash).Paths);
        Assert.Empty(_store.Unresolved.Load());

        // 登録した後の取り込みで、未確定に戻らない
        await ImportAsync();
        Assert.Empty(_store.Unresolved.Load());
        Assert.Equal(2, (await FilesOfAsync(ItemId)).Count);
    }

    /// <summary>
    /// 手で結んだ壊れた zip を同じ場所に落とし直した。壊れた方の記録は記録ごと消え（今の決まりのまま）、
    /// 新しい方は未確定に出て、結んでいた商品が候補に残る。前はどの商品の物だったかが画面から分からなくなった。
    /// </summary>
    [Fact]
    public async Task ABrokenZipDownloadedAgainKeepsTheItemAsACandidate()
    {
        await SaveItemAsync(ItemId);
        PutTruncatedZip("pack.zip");
        await ImportAsync();
        var broken = Assert.Single(_store.Unresolved.Load());
        Assert.True(broken.ArchiveBroken);
        Assert.True(await _service.AssignItemIdAsync(broken.Hash, ItemId));

        PutZip("pack.zip", version: 2, itemId: null);
        await ImportAsync();

        Assert.Empty(await FilesOfAsync(ItemId));
        var unresolved = Assert.Single(_store.Unresolved.Load());
        Assert.False(unresolved.ArchiveBroken);
        Assert.Equal([ItemId], unresolved.SamePathItemIds);
    }

    /// <summary>同じ場所を2つの商品が持っているなら、どちらの記録からも外し、どちらも候補に残す。</summary>
    [Fact]
    public async Task DropsThePlaceFromEveryItemThatHeldIt()
    {
        var path = PutZip("pack.zip", version: 1, itemId: null);
        var record = await RecordOfAsync(path);
        await SaveItemAsync(ItemId, record);
        await SaveItemAsync(OtherId, record);

        PutZip("pack.zip", version: 2, itemId: null);
        await ImportAsync();

        Assert.Empty(Assert.Single(await FilesOfAsync(ItemId)).Paths);
        Assert.Empty(Assert.Single(await FilesOfAsync(OtherId)).Paths);
        Assert.Equal(
            [ItemId, OtherId],
            Assert.Single(_store.Unresolved.Load()).SamePathItemIds.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// 走査していない場所は触らない。中身が変わったと言えるのは、今回その場所のハッシュを取ったときだけ
    /// （対象は「今積んだ物」だけなので、ほかの取り込み元の物は見ていない）。
    /// </summary>
    [Fact]
    public async Task LeavesPlacesThatWereNotScanned()
    {
        var elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        var path = PutZip("pack.zip", version: 1, itemId: null, folder: elsewhere);
        var record = await RecordOfAsync(path);
        await SaveItemAsync(ItemId, record);

        // 走査しない場所で上書きし、別の取り込み元だけを取り込む
        PutZip("pack.zip", version: 2, itemId: null, folder: elsewhere);
        PutZip("another.zip", version: 3, OtherId);
        await ImportAsync();

        Assert.Equal([path], Assert.Single(await FilesOfAsync(ItemId)).Paths);
        Assert.Empty(_store.Unresolved.Load());
    }

    /// <summary>中身が同じなら（同じ物を落とし直した）何も変えない。</summary>
    [Fact]
    public async Task LeavesTheRecordWhenTheContentIsTheSame()
    {
        var path = PutZip("pack.zip", version: 1, itemId: null);
        var bytes = File.ReadAllBytes(path);
        var record = await RecordOfAsync(path);
        await SaveItemAsync(ItemId, record);

        File.Delete(path);
        File.WriteAllBytes(path, bytes);
        await ImportAsync();

        Assert.Equal([path], Assert.Single(await FilesOfAsync(ItemId)).Paths);
        Assert.Empty(_store.Unresolved.Load());
    }

    /// <summary>
    /// 外した印の行からも場所は外す（そこに在るのは別の中身）。ただしその商品は候補にしない——
    /// 前の中身を「この商品のものではない」と人が外している。
    /// </summary>
    [Fact]
    public async Task ADetachedRecordLosesThePlaceButIsNotACandidate()
    {
        var path = PutZip("pack.zip", version: 1, itemId: null);
        var record = await RecordOfAsync(path, detached: true);
        await SaveItemAsync(ItemId, record);

        PutZip("pack.zip", version: 2, itemId: null);
        await ImportAsync();

        var kept = Assert.Single(await FilesOfAsync(ItemId));
        Assert.True(kept.Detached);
        Assert.Empty(kept.Paths);
        Assert.Empty(Assert.Single(_store.Unresolved.Load()).SamePathItemIds);
    }

    /// <summary>
    /// 新しい中身を別の商品がもう持っているなら（ほかの場所で手で結んである）、未確定には出さずにその商品へ場所を足す。
    /// 古い中身の記録からは外す。
    /// </summary>
    [Fact]
    public async Task TheNewContentAlreadyOwnedElsewhereMovesThePlaceToItsOwner()
    {
        var path = PutZip("pack.zip", version: 1, itemId: null);
        var old = await RecordOfAsync(path);
        await SaveItemAsync(ItemId, old);

        PutZip("pack.zip", version: 2, itemId: null);
        var fresh = await RecordOfAsync(path) with { Paths = [] };
        await SaveItemAsync(OtherId, fresh);
        await ImportAsync();

        Assert.Empty(Assert.Single(await FilesOfAsync(ItemId)).Paths);
        Assert.Equal([path], Assert.Single(await FilesOfAsync(OtherId)).Paths);
        Assert.Empty(_store.Unresolved.Load());
    }
}
