using System.IO.Compression;
using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 商品ページからファイルを直にこの商品へ結ぶ（ユーザ指示 2026-10-06）。作者が同じ物を新しいIDで出し直すと、
/// ファイルの手掛かりは古いIDを指すので取り込みでは結べない。事情（外していた・ほかの持ち主・除外・未確定）の扱いは
/// 「zipで登録し直す」と同じ。
/// </summary>
public class FileAttachTests : IDisposable
{
    private const string ItemId = "9900801";
    private const string OtherId = "9900802";

    private readonly string _root;
    private readonly DataStore _store;
    private readonly ItemService _service;

    public FileAttachTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "chmonos-attach-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);

        // 結ぶのは手元の記録だけ。BOOTH へ行けば落ちる
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

    private async Task SaveItemAsync(string id, params LocalFileRecord[] files)
        => await _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = "作り物の商品", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { LocalFiles = files },
        });

    /// <summary>中に1ファイル入った zip を作る。返すのは場所とハッシュ。</summary>
    private async Task<(string Path, string Hash)> MakeZipAsync(string name = "作り物_v2.zip")
    {
        var source = Path.Combine(_root, "src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        File.WriteAllBytes(Path.Combine(source, "作り物.unitypackage"), new byte[50]);
        var zip = Path.Combine(_root, "files", name);
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        ZipFile.CreateFromDirectory(source, zip);
        return (zip, await Scanning.FileHasher.ComputeSha256Async(zip));
    }

    private async Task<LocalBlock> LocalOfAsync(string id) => (await _store.Items.LoadAsync(id))!.Local;

    [Fact]
    public async Task 選んだzipをこの商品に結び_大きさと中身の一覧を書く()
    {
        await SaveItemAsync(ItemId);
        var (zip, hash) = await MakeZipAsync();

        var outcome = await _service.AttachFileAsync(ItemId, zip);

        Assert.Equal(FileAttachResult.Attached, outcome.Result);
        Assert.Equal("作り物_v2.zip", outcome.FileName);
        var file = Assert.Single((await LocalOfAsync(ItemId)).LocalFiles);
        Assert.Equal(hash, file.Hash);
        Assert.Equal([zip], file.Paths);
        Assert.Equal(new FileInfo(zip).Length, file.SizeBytes);
        Assert.Equal(["作り物.unitypackage"], file.Contents);
        Assert.False(file.ArchiveBroken);
    }

    /// <summary>zip 以外は中を読まない（取り込みと同じ）。壊れた印も立てない。</summary>
    [Fact]
    public async Task zip以外のファイルは中を読まずに結ぶ()
    {
        await SaveItemAsync(ItemId);
        var pdf = Path.Combine(_root, "説明書.pdf");
        File.WriteAllBytes(pdf, [1, 2, 3, 4]);

        Assert.Equal(FileAttachResult.Attached, (await _service.AttachFileAsync(ItemId, pdf)).Result);

        var file = Assert.Single((await LocalOfAsync(ItemId)).LocalFiles);
        Assert.Empty(file.Contents);
        Assert.False(file.ArchiveBroken);
    }

    [Fact]
    public async Task 開けないzipは壊れたzipの印を付けて結ぶ()
    {
        await SaveItemAsync(ItemId);
        var zip = Path.Combine(_root, "途中で切れた.zip");
        File.WriteAllBytes(zip, [1, 2, 3, 4, 5]);

        Assert.Equal(FileAttachResult.Attached, (await _service.AttachFileAsync(ItemId, zip)).Result);

        Assert.True(Assert.Single((await LocalOfAsync(ItemId)).LocalFiles).ArchiveBroken);
    }

    /// <summary>単体の unitypackage は取り込みも扱わない（送る道も zip の中しか見ない）。持ち物にしない。</summary>
    [Fact]
    public async Task 取り込む種類でないファイルは結ばない()
    {
        await SaveItemAsync(ItemId);
        var package = Path.Combine(_root, "単体.unitypackage");
        File.WriteAllBytes(package, [1, 2, 3]);

        Assert.Equal(FileAttachResult.NotTarget, (await _service.AttachFileAsync(ItemId, package)).Result);
        Assert.Empty((await LocalOfAsync(ItemId)).LocalFiles);
    }

    [Fact]
    public async Task 同じ場所のファイルを2回結んでも1件のまま()
    {
        await SaveItemAsync(ItemId);
        var (zip, _) = await MakeZipAsync();
        await _service.AttachFileAsync(ItemId, zip);

        Assert.Equal(FileAttachResult.AlreadyAttached, (await _service.AttachFileAsync(ItemId, zip)).Result);
        Assert.Single(Assert.Single((await LocalOfAsync(ItemId)).LocalFiles).Paths);
    }

    /// <summary>同じ中身を別の場所に持っていれば、場所を足す（無い場所も外さない：人の登録の足し方）。</summary>
    [Fact]
    public async Task 同じ中身を別の場所に持っていれば場所を足す()
    {
        var (zip, hash) = await MakeZipAsync();
        await SaveItemAsync(ItemId, new LocalFileRecord { Hash = hash, Paths = [@"X:\作り物\前の場所.zip"], SizeBytes = 1 });

        Assert.Equal(FileAttachResult.Attached, (await _service.AttachFileAsync(ItemId, zip)).Result);

        var file = Assert.Single((await LocalOfAsync(ItemId)).LocalFiles);
        Assert.Equal([@"X:\作り物\前の場所.zip", zip], file.Paths);
    }

    /// <summary>前にこの商品から外していた物は、外した印を下ろして付ける（人が今選んだ方が新しい判断）。</summary>
    [Fact]
    public async Task 外していたファイルは印を下ろして結ぶ()
    {
        var (zip, hash) = await MakeZipAsync();
        await SaveItemAsync(ItemId, new LocalFileRecord { Hash = hash, Paths = [zip], SizeBytes = 1, Detached = true });

        Assert.Equal(FileAttachResult.Attached, (await _service.AttachFileAsync(ItemId, zip)).Result);

        Assert.False(Assert.Single((await LocalOfAsync(ItemId)).LocalFiles).Detached);
    }

    [Fact]
    public async Task ほかの商品が持つファイルは持ち主を返し_付け直すと頼まれたら向こうから外す()
    {
        var (zip, hash) = await MakeZipAsync();
        await SaveItemAsync(ItemId);
        await SaveItemAsync(OtherId, new LocalFileRecord { Hash = hash, Paths = [zip], SizeBytes = 1 });

        var refused = await _service.AttachFileAsync(ItemId, zip);

        Assert.Equal(FileAttachResult.OwnedElsewhere, refused.Result);
        var holder = Assert.Single(refused.Holders);
        Assert.Equal(OtherId, holder.ItemId);
        Assert.True(holder.LosesLastFile);
        Assert.Empty((await LocalOfAsync(ItemId)).LocalFiles);

        var taken = await _service.AttachFileAsync(ItemId, zip, takeFromOtherItems: true);

        Assert.Equal(FileAttachResult.Attached, taken.Result);
        Assert.False(Assert.Single((await LocalOfAsync(ItemId)).LocalFiles).Detached);
        Assert.True(Assert.Single((await LocalOfAsync(OtherId)).LocalFiles).Detached);
    }

    /// <summary>ほかの商品が外している物は、その商品の持ち物ではないので聞かずに付ける。</summary>
    [Fact]
    public async Task ほかの商品が外しているファイルは聞かずに結ぶ()
    {
        var (zip, hash) = await MakeZipAsync();
        await SaveItemAsync(ItemId);
        await SaveItemAsync(OtherId, new LocalFileRecord { Hash = hash, Paths = [zip], SizeBytes = 1, Detached = true });

        Assert.Equal(FileAttachResult.Attached, (await _service.AttachFileAsync(ItemId, zip)).Result);
    }

    [Fact]
    public async Task 除外したファイルは聞かずに結ばず_除外を解くと頼まれたら解いて結ぶ()
    {
        var (zip, hash) = await MakeZipAsync();
        await SaveItemAsync(ItemId);
        await _store.Excluded.SaveAsync([new ExcludedEntry { Hash = hash, Paths = [zip], ExcludedAt = DateTimeOffset.Now }]);

        Assert.Equal(FileAttachResult.Excluded, (await _service.AttachFileAsync(ItemId, zip)).Result);
        Assert.Empty((await LocalOfAsync(ItemId)).LocalFiles);
        Assert.Single(_store.Excluded.Load());

        Assert.Equal(FileAttachResult.Attached, (await _service.AttachFileAsync(ItemId, zip, liftExclusion: true)).Result);
        Assert.Single((await LocalOfAsync(ItemId)).LocalFiles);
        Assert.Empty(_store.Excluded.Load());
    }

    [Fact]
    public async Task 未確定にあったファイルは結んだら未確定から消える()
    {
        var (zip, hash) = await MakeZipAsync();
        await SaveItemAsync(ItemId);
        await _store.Unresolved.SaveAsync(
        [
            new UnresolvedFile { Hash = hash, Paths = [zip], SizeBytes = 1, ModifiedAtUtc = DateTimeOffset.Now, FirstSeenAt = DateTimeOffset.Now },
            new UnresolvedFile { Hash = "CCCC", Paths = [@"X:\別.zip"], SizeBytes = 1, ModifiedAtUtc = DateTimeOffset.Now, FirstSeenAt = DateTimeOffset.Now },
        ]);

        await _service.AttachFileAsync(ItemId, zip);

        Assert.Equal("CCCC", Assert.Single(_store.Unresolved.Load()).Hash);
    }

    [Fact]
    public async Task 商品が無ければ何も書かない()
    {
        var (zip, hash) = await MakeZipAsync();
        await _store.Unresolved.SaveAsync(
            [new UnresolvedFile { Hash = hash, Paths = [zip], SizeBytes = 1, ModifiedAtUtc = DateTimeOffset.Now, FirstSeenAt = DateTimeOffset.Now }]);

        Assert.Equal(FileAttachResult.ItemMissing, (await _service.AttachFileAsync(ItemId, zip)).Result);
        Assert.Single(_store.Unresolved.Load());
    }

    [Fact]
    public async Task 選んだ後に消えたファイルは結ばない()
    {
        await SaveItemAsync(ItemId);

        Assert.Equal(FileAttachResult.FileMissing, (await _service.AttachFileAsync(ItemId, Path.Combine(_root, "無い.zip"))).Result);
        Assert.Empty((await LocalOfAsync(ItemId)).LocalFiles);
    }

    /// <summary>
    /// ハッシュを取っている間に取り込みが同じ商品へファイルを足しても、足したファイルは消えない
    /// （錠の外で読んだ写しで localFiles を書き戻さない）。
    /// </summary>
    [Fact]
    public async Task 結ぶ間に取り込みが足したファイルが残る()
    {
        await SaveItemAsync(ItemId);
        var (zip, _) = await MakeZipAsync();

        FileAttachOutcome? outcome = null;
        await ItemLockRace.WhileAnotherWriterChangesAsync(
            _store,
            ItemId,
            local => ItemLockRace.AddFile(local),
            async () => outcome = await _service.AttachFileAsync(ItemId, zip));

        Assert.Equal(FileAttachResult.Attached, outcome!.Result);
        var after = await LocalOfAsync(ItemId);
        Assert.Contains(after.LocalFiles, file => file.Hash == "BBBB");
        Assert.Contains(after.LocalFiles, file => file.Paths.Contains(zip));
    }
}
