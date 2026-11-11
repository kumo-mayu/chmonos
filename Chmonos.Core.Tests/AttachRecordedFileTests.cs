using Chmonos.Core.Booth;
using Chmonos.Core.Commands;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// ほかの商品の記録にあるファイルを、記録を写して付ける（ファイルの割り振りのサブ画面・plan 30）。
/// ファイルは読まない（見つからない物も付けられ、大きい zip を読み直さない）。除外・ほかの持ち主・錠は人が場所を選んで付けるのと同じ。
/// 名前・番号・ハッシュは作り物。
/// </summary>
public sealed class AttachRecordedFileTests : IDisposable
{
    private const string SetId = "9900951";
    private const string PartId = "9900952";
    private const string ThirdId = "9900953";
    private const string Hash = "AAAA000000000000000000000000000000000000000000000000000000000951";

    private readonly string _root;
    private readonly DataStore _store;
    private readonly ItemService _service;

    public AttachRecordedFileTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "chmonos-recorded-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);

        // 手元の記録だけを扱う。BOOTH へ行けば落ちる
        var client = new BoothClient(new HttpClient(new UnreachableHandler()), new AppSettings { FetchIntervalMs = 0 }, TestWait.None);
        _service = new ItemService(_store, client, new ImagePipeline(client, paths));
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("このテストではBOOTHへ行かないはず");
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

    /// <summary>ディスクに無い場所（作らない）。記録から付ける道はファイルを読まないので、無くても付く。</summary>
    private string MissingPath => Path.Combine(_root, "files", "作り物_セット.zip");

    private LocalFileRecord SetFile(bool detached = false) => new()
    {
        Hash = Hash,
        Paths = [MissingPath],
        SizeBytes = 123_456,
        VariationId = 7,
        Contents = ["作り物_上着.unitypackage", "readme.txt"],
        UnityPackages = [],
        ArchiveBroken = true,
        MissingSince = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
        RegisteredAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        DownloadSource = new DownloadSource(),
        Detached = detached,
    };

    private Task SaveItemAsync(string id, params LocalFileRecord[] files)
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = "作り物の商品" + id, FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { LocalFiles = files },
        });

    private async Task<LocalBlock> LocalOfAsync(string id) => (await _store.Items.LoadAsync(id))!.Local;

    [Fact]
    public async Task 記録から付けると_ファイルを読まずに中身の事実を写し_商品ごとの欄は写さない()
    {
        await SaveItemAsync(SetId, SetFile());
        await SaveItemAsync(PartId);
        var before = await LocalOfAsync(SetId);
        var started = DateTimeOffset.Now;

        var outcome = await _service.AttachRecordedFileAsync(PartId, Hash, SetId, otherHolders: OtherHolders.Share);

        Assert.Equal(FileAttachResult.Attached, outcome.Result);
        Assert.Equal("作り物_セット.zip", outcome.FileName);
        var mine = Assert.Single((await LocalOfAsync(PartId)).LocalFiles);
        Assert.Equal(Hash, mine.Hash);
        Assert.Equal([MissingPath], mine.Paths);
        Assert.Equal(123_456, mine.SizeBytes);
        Assert.Equal(["作り物_上着.unitypackage", "readme.txt"], mine.Contents);
        Assert.NotNull(mine.UnityPackages);
        Assert.True(mine.ArchiveBroken);
        Assert.NotNull(mine.DownloadSource);

        // 見つからないのは全体の事実なので写す（付けたから在ることにはしない）
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), mine.MissingSince);

        // 種類と記録した日時は商品ごと（shared-files.md「データ」）
        Assert.Null(mine.VariationId);
        Assert.False(mine.Detached);
        Assert.True(mine.RegisteredAt >= started);

        // 元の記録は変えない
        Assert.Equal(before.LocalFiles, (await LocalOfAsync(SetId)).LocalFiles);
    }

    /// <summary>選んだ商品の最後の1件から外した物も、外した行から写して付け直せる。未確定に出ていれば消える。</summary>
    [Fact]
    public async Task 外した行からも写せ_付けると未確定から消える()
    {
        var path = Path.Combine(_root, "files", "作り物_単品.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "作り物の中身");
        var hash = await Scanning.FileHasher.ComputeSha256Async(path);
        await SaveItemAsync(SetId, new LocalFileRecord { Hash = hash, Paths = [path], SizeBytes = 1 });
        await SaveItemAsync(PartId);

        Assert.Equal(DetachOutcome.ItemNowEmpty, await _service.DetachFileAsync(SetId, hash, deleteItemWhenEmpty: false));
        Assert.Single(_store.Unresolved.Load());

        var outcome = await _service.AttachRecordedFileAsync(PartId, hash, SetId, otherHolders: OtherHolders.Share);

        Assert.Equal(FileAttachResult.Attached, outcome.Result);
        Assert.False(Assert.Single((await LocalOfAsync(PartId)).LocalFiles).Detached);
        Assert.True(Assert.Single((await LocalOfAsync(SetId)).LocalFiles).Detached);
        Assert.Empty(_store.Unresolved.Load());
    }

    [Fact]
    public async Task 前に外した行を持つ商品へ付けると_その行の印を下ろし_記録した日時は保つ()
    {
        await SaveItemAsync(SetId, SetFile());
        await SaveItemAsync(PartId, SetFile(detached: true) with { VariationId = 3 });

        var outcome = await _service.AttachRecordedFileAsync(PartId, Hash, SetId, otherHolders: OtherHolders.Share);

        Assert.Equal(FileAttachResult.Attached, outcome.Result);
        var mine = Assert.Single((await LocalOfAsync(PartId)).LocalFiles);
        Assert.False(mine.Detached);
        Assert.Equal(3, mine.VariationId);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), mine.RegisteredAt);
    }

    [Fact]
    public async Task 除外していれば何も書かずに除外と返す()
    {
        await SaveItemAsync(SetId, SetFile());
        await SaveItemAsync(PartId);
        await _store.Excluded.SaveAsync([new ExcludedEntry { Hash = Hash, ExcludedAt = DateTimeOffset.Now }]);

        var outcome = await _service.AttachRecordedFileAsync(PartId, Hash, SetId, otherHolders: OtherHolders.Share);

        Assert.Equal(FileAttachResult.Excluded, outcome.Result);
        Assert.Empty((await LocalOfAsync(PartId)).LocalFiles);
    }

    [Fact]
    public async Task 聞く形で呼ぶと_ほかの持ち主を返して何も書かない()
    {
        await SaveItemAsync(SetId, SetFile());
        await SaveItemAsync(PartId);

        var outcome = await _service.AttachRecordedFileAsync(PartId, Hash, SetId);

        Assert.Equal(FileAttachResult.OwnedElsewhere, outcome.Result);
        Assert.Equal([SetId], outcome.Holders.Select(holder => holder.ItemId));
        Assert.Empty((await LocalOfAsync(PartId)).LocalFiles);
    }

    [Fact]
    public async Task もう持っていれば書かない()
    {
        await SaveItemAsync(SetId, SetFile());
        await SaveItemAsync(PartId, SetFile() with { VariationId = 3 });

        var outcome = await _service.AttachRecordedFileAsync(PartId, Hash, SetId, otherHolders: OtherHolders.Share);

        Assert.Equal(FileAttachResult.AlreadyAttached, outcome.Result);
        Assert.Equal(3, Assert.Single((await LocalOfAsync(PartId)).LocalFiles).VariationId);
    }

    [Fact]
    public async Task 元の記録に無ければ記録が無いと返し_付ける先が無ければ商品が無いと返す()
    {
        await SaveItemAsync(SetId);
        await SaveItemAsync(PartId);

        Assert.Equal(
            FileAttachResult.RecordMissing,
            (await _service.AttachRecordedFileAsync(PartId, Hash, SetId, otherHolders: OtherHolders.Share)).Result);
        Assert.Equal(
            FileAttachResult.RecordMissing,
            (await _service.AttachRecordedFileAsync(PartId, Hash, ThirdId, otherHolders: OtherHolders.Share)).Result);

        await SaveItemAsync(SetId, SetFile());
        Assert.Equal(
            FileAttachResult.ItemMissing,
            (await _service.AttachRecordedFileAsync(ThirdId, Hash, SetId, otherHolders: OtherHolders.Share)).Result);
    }

    /// <summary>サブ画面が読み直すための知らせ：書いたときも消したときも、その商品の ID で鳴る。</summary>
    [Fact]
    public async Task 置き場は書いたときと消したときに商品のIDを知らせる()
    {
        var heard = new List<string>();
        _store.Items.Written += heard.Add;

        await SaveItemAsync(SetId, SetFile());
        await _store.Items.ChangeLocalAsync(SetId, local => local with { Memo = "作り物のメモ" }, [LocalField.Memo]);
        await _store.Items.DeleteAsync(SetId);

        Assert.Equal([SetId, SetId, SetId], heard);
    }
}
