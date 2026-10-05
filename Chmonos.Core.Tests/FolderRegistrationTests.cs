using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// zipが手元に無く展開したものだけが残っている商品を、フォルダとして紐付ける経路。
/// </summary>
public class FolderRegistrationTests : IDisposable
{
    private readonly string _root;
    private readonly string _library;
    private readonly DataStore _store;
    private readonly ItemService _service;

    public FolderRegistrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-folder-" + Guid.NewGuid().ToString("N"));
        _library = Path.Combine(_root, "library");
        Directory.CreateDirectory(_root);

        var paths = new AppPaths(_library);
        paths.EnsureCreated();
        _store = new DataStore(paths);

        // 既にitemがある場合しか試さないので、BOOTHへは行かない
        var client = new BoothClient(new HttpClient(new UnreachableHandler()), new AppSettings { FetchIntervalMs = 0 }, TestWait.None);
        _service = new ItemService(_store, client, new ImagePipeline(client, paths));
    }

    /// <summary>呼ばれたら失敗させるハンドラ。通信していないことを保証する。</summary>
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

    private async Task<string> SaveItemAsync(string id = "5957830")
    {
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = "テスト商品", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock(),
        });

        return id;
    }

    /// <summary>展開済みフォルダを作る。中身とサイズを決められるようにしておく。</summary>
    private string CreateExtractedFolder(string name = "rurune_v1.1.3")
    {
        var folder = Path.Combine(_root, name, "rurune");
        Directory.CreateDirectory(Path.Combine(folder, "texture"));
        File.WriteAllBytes(Path.Combine(folder, "rurune.unitypackage"), new byte[300]);
        File.WriteAllBytes(Path.Combine(folder, "texture", "hair.psd"), new byte[200]);
        File.WriteAllBytes(Path.Combine(folder, "texture", "cloth.psd"), new byte[100]);
        return Path.Combine(_root, name);
    }

    [Fact]
    public async Task RegistersTheFolderWithItsFileCountAndSize()
    {
        var itemId = await SaveItemAsync();
        var folder = CreateExtractedFolder();

        Assert.True(await _service.RegisterFolderAsync(itemId, folder));

        var item = await _store.Items.LoadAsync(itemId);
        var registered = Assert.Single(item!.Local.LocalFolders);
        Assert.Equal(folder, registered.Path);
        Assert.Equal(3, registered.FileCount);
        Assert.Equal(600, registered.TotalBytes);
        Assert.NotNull(registered.LastSeenAt);
    }

    /// <summary>行き先が決まったので、配下の未確定は取り除く。</summary>
    [Fact]
    public async Task RemovesUnresolvedFilesUnderTheFolder()
    {
        var itemId = await SaveItemAsync();
        var folder = CreateExtractedFolder();

        await _store.Unresolved.SaveAsync(
        [
            new UnresolvedFile
            {
                Hash = "AAAA",
                Paths = [Path.Combine(folder, "rurune", "texture", "hair.psd")],
                SizeBytes = 200,
                ModifiedAtUtc = DateTimeOffset.Now,
                FirstSeenAt = DateTimeOffset.Now,
            },
            new UnresolvedFile
            {
                Hash = "BBBB",
                Paths = [Path.Combine(_root, "other.zip")],
                SizeBytes = 10,
                ModifiedAtUtc = DateTimeOffset.Now,
                FirstSeenAt = DateTimeOffset.Now,
            },
        ]);

        await _service.RegisterFolderAsync(itemId, folder);

        var remaining = _store.Unresolved.Load();
        Assert.Single(remaining);
        Assert.Equal("BBBB", remaining[0].Hash);
    }

    /// <summary>同じフォルダを登録し直しても増えない。数え直した値で置き換わる。</summary>
    [Fact]
    public async Task ReplacesAnExistingRegistrationForTheSamePath()
    {
        var itemId = await SaveItemAsync();
        var folder = CreateExtractedFolder();

        await _service.RegisterFolderAsync(itemId, folder);
        File.WriteAllBytes(Path.Combine(folder, "rurune", "texture", "extra.png"), new byte[50]);
        await _service.RegisterFolderAsync(itemId, folder);

        var item = await _store.Items.LoadAsync(itemId);
        var registered = Assert.Single(item!.Local.LocalFolders);
        Assert.Equal(4, registered.FileCount);
        Assert.Equal(650, registered.TotalBytes);
    }

    [Fact]
    public async Task RefusesAMissingFolder()
    {
        var itemId = await SaveItemAsync();

        Assert.False(await _service.RegisterFolderAsync(itemId, Path.Combine(_root, "does-not-exist")));
    }

    /// <summary>登録を解除すると記録から消える。ファイルには触らない。</summary>
    [Fact]
    public async Task UnregistersAFolderWithoutTouchingTheFiles()
    {
        var itemId = await SaveItemAsync();
        var folder = CreateExtractedFolder();
        await _service.RegisterFolderAsync(itemId, folder);

        Assert.True(await _service.UnregisterFolderAsync(itemId, folder));

        var item = await _store.Items.LoadAsync(itemId);
        Assert.Empty(item!.Local.LocalFolders);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task UnregisterReportsFalseWhenNothingWasRegistered()
    {
        var itemId = await SaveItemAsync();

        Assert.False(await _service.UnregisterFolderAsync(itemId, Path.Combine(_root, "nope")));
    }

    /// <summary>隣にzipが現れたら、それを商品に付けてフォルダの登録を外す（ユーザ指示 2026-09-18）。</summary>
    [Fact]
    public async Task SwapsTheFolderForTheArchiveNextToIt()
    {
        var itemId = await SaveItemAsync();
        var folder = CreateExtractedFolder();
        await _service.RegisterFolderAsync(itemId, folder);

        var archive = folder + ".zip";
        System.IO.Compression.ZipFile.CreateFromDirectory(folder, archive);

        var outcome = await _service.SwapFolderForArchiveAsync(itemId, folder);

        Assert.Equal(ArchiveSwapResult.Registered, outcome.Result);
        Assert.Equal(Path.GetFileName(archive), outcome.ArchiveName);

        var item = await _store.Items.LoadAsync(itemId);
        Assert.Empty(item!.Local.LocalFolders);
        var file = Assert.Single(item.Local.LocalFiles);
        Assert.Equal(archive, Assert.Single(file.Paths));
        Assert.NotEmpty(file.Contents);

        // ディスクのファイルには触らない
        Assert.True(Directory.Exists(folder));
        Assert.True(File.Exists(archive));
    }

    /// <summary>zipが隣に無ければ、登録はそのままにして知らせる（外付けを外している場合など）。</summary>
    [Fact]
    public async Task KeepsTheFolderWhenThereIsNoArchive()
    {
        var itemId = await SaveItemAsync();
        var folder = CreateExtractedFolder();
        await _service.RegisterFolderAsync(itemId, folder);

        var outcome = await _service.SwapFolderForArchiveAsync(itemId, folder);

        Assert.Equal(ArchiveSwapResult.ArchiveMissing, outcome.Result);
        var item = await _store.Items.LoadAsync(itemId);
        Assert.Single(item!.Local.LocalFolders);
    }

    /// <summary>zipを既に取り込んであれば、フォルダの登録だけ外す（同じファイルを二重に持たない）。</summary>
    [Fact]
    public async Task OnlyDropsTheFolderWhenTheArchiveIsAlreadyRegistered()
    {
        var itemId = await SaveItemAsync();
        var folder = CreateExtractedFolder();
        await _service.RegisterFolderAsync(itemId, folder);

        var archive = folder + ".zip";
        System.IO.Compression.ZipFile.CreateFromDirectory(folder, archive);

        var item = await _store.Items.LoadAsync(itemId);
        await _store.Items.SaveLocalAsync(
            itemId,
            item!.Local with
            {
                LocalFiles = [new LocalFileRecord { Hash = "ABCD", Paths = [archive], SizeBytes = 10 }],
            },
            LocalOwners.Import);

        var outcome = await _service.SwapFolderForArchiveAsync(itemId, folder);

        Assert.Equal(ArchiveSwapResult.AlreadyRegistered, outcome.Result);
        var after = await _store.Items.LoadAsync(itemId);
        Assert.Empty(after!.Local.LocalFolders);
        Assert.Single(after.Local.LocalFiles);
    }

    // ---- zip に事情があるとき（2026-10-05・file-lifecycle.md「気になった所」7・ユーザ判断）----

    /// <summary>フォルダを登録し、隣に zip を置く。zip のパスとハッシュを返す。</summary>
    private async Task<(string Folder, string Archive, string Hash)> FolderWithArchiveAsync(string itemId)
    {
        await SaveItemAsync(itemId);
        var folder = CreateExtractedFolder("作り物_" + itemId);
        await _service.RegisterFolderAsync(itemId, folder);
        var archive = folder + ".zip";
        System.IO.Compression.ZipFile.CreateFromDirectory(folder, archive);
        return (folder, archive, await Scanning.FileHasher.ComputeSha256Async(archive));
    }

    private async Task SetFilesAsync(string itemId, params LocalFileRecord[] files)
        => await _store.Items.ChangeLocalAsync(itemId, local => local with { LocalFiles = files }, LocalOwners.Import);

    /// <summary>
    /// 前にこの商品から外した zip でも、押した方が新しい判断なので外した印を下ろして付け、フォルダを外す。
    /// 前は外した行が同じ場所を持つのを「登録済み」と読んでフォルダだけ外し、商品の所持が無くなっていた。
    /// </summary>
    [Fact]
    public async Task 外していたzipで登録し直すと印を下ろして付ける()
    {
        var (folder, archive, hash) = await FolderWithArchiveAsync("9900121");
        await SetFilesAsync("9900121", new LocalFileRecord { Hash = hash, Paths = [archive], SizeBytes = 1, Detached = true });

        var outcome = await _service.SwapFolderForArchiveAsync("9900121", folder);

        Assert.Equal(ArchiveSwapResult.Registered, outcome.Result);
        var after = (await _store.Items.LoadAsync("9900121"))!;
        Assert.False(Assert.Single(after.Local.LocalFiles).Detached);
        Assert.Empty(after.Local.LocalFolders);
        Assert.True(after.HasOwnedFiles);
    }

    /// <summary>除外した zip は、聞かずには付けない（画面が窓で聞いてから、除外を解除して付ける）。</summary>
    [Fact]
    public async Task 除外したzipは聞くまで付けず_解除を頼まれたら除外を解いて付ける()
    {
        var (folder, archive, hash) = await FolderWithArchiveAsync("9900122");
        await _store.Excluded.SaveAsync(
            [new ExcludedEntry { Hash = hash, Paths = [archive], ExcludedAt = DateTimeOffset.Now, Reason = "試験" }]);

        var refused = await _service.SwapFolderForArchiveAsync("9900122", folder);

        Assert.Equal(ArchiveSwapResult.Excluded, refused.Result);
        var untouched = (await _store.Items.LoadAsync("9900122"))!.Local;
        Assert.Empty(untouched.LocalFiles);
        Assert.Single(untouched.LocalFolders);
        Assert.Single(_store.Excluded.Load());

        var lifted = await _service.SwapFolderForArchiveAsync("9900122", folder, liftExclusion: true);

        Assert.Equal(ArchiveSwapResult.Registered, lifted.Result);
        Assert.Equal(hash, Assert.Single((await _store.Items.LoadAsync("9900122"))!.Local.LocalFiles).Hash);
        Assert.Empty(_store.Excluded.Load());
    }

    /// <summary>
    /// ほかの商品が持つ zip は、聞かずには付けない。持ち主と、外すとファイルが無くなるかを返す。
    /// 付け直すと頼まれたら、この商品に付けて、ほかの商品からは外す（外した印。行は残す）。
    /// </summary>
    [Fact]
    public async Task ほかの商品が持つzipは持ち主を返し_付け直すと頼まれたら向こうから外す()
    {
        var (folder, archive, hash) = await FolderWithArchiveAsync("9900123");
        await SaveItemAsync("9900124");
        await SetFilesAsync("9900124", new LocalFileRecord { Hash = hash, Paths = [archive], SizeBytes = 1 });

        var refused = await _service.SwapFolderForArchiveAsync("9900123", folder);

        Assert.Equal(ArchiveSwapResult.OwnedElsewhere, refused.Result);
        var holder = Assert.Single(refused.Holders);
        Assert.Equal("9900124", holder.ItemId);
        Assert.Equal("テスト商品", holder.Name);
        Assert.True(holder.LosesLastFile);
        Assert.Empty((await _store.Items.LoadAsync("9900123"))!.Local.LocalFiles);

        var taken = await _service.SwapFolderForArchiveAsync("9900123", folder, takeFromOtherItems: true);

        Assert.Equal(ArchiveSwapResult.Registered, taken.Result);
        Assert.False(Assert.Single((await _store.Items.LoadAsync("9900123"))!.Local.LocalFiles).Detached);
        Assert.True(Assert.Single((await _store.Items.LoadAsync("9900124"))!.Local.LocalFiles).Detached);
    }

    /// <summary>ほかの商品が外している zip は、その商品の持ち物ではないので聞かずに付ける。</summary>
    [Fact]
    public async Task ほかの商品が外しているzipは聞かずに付ける()
    {
        var (folder, archive, hash) = await FolderWithArchiveAsync("9900125");
        await SaveItemAsync("9900126");
        await SetFilesAsync("9900126", new LocalFileRecord { Hash = hash, Paths = [archive], SizeBytes = 1, Detached = true });

        Assert.Equal(ArchiveSwapResult.Registered, (await _service.SwapFolderForArchiveAsync("9900125", folder)).Result);
    }

    // ---- 錠の外で読んだ写しで書き戻さない（2026-10-05・file-lifecycle.md「気になった所」3）----

    /// <summary>
    /// zip をハッシュしている間に取り込みが同じ商品へファイルを足しても、足したファイルは消えない。
    /// 前は錠の外で読んだ写しに zip を足して <c>localFiles</c> ごと書いていたので、ハッシュの数秒の間に足された物が消えていた。
    /// </summary>
    [Fact]
    public async Task zipで登録し直す間に取り込みが足したファイルが残る()
    {
        var itemId = await SaveItemAsync("9900011");
        var folder = CreateExtractedFolder();
        await _service.RegisterFolderAsync(itemId, folder);
        System.IO.Compression.ZipFile.CreateFromDirectory(folder, folder + ".zip");

        ArchiveSwapOutcome? outcome = null;
        await ItemLockRace.WhileAnotherWriterChangesAsync(
            _store,
            itemId,
            local => ItemLockRace.AddFile(local),
            async () => outcome = await _service.SwapFolderForArchiveAsync(itemId, folder));

        Assert.Equal(ArchiveSwapResult.Registered, outcome!.Result);
        var after = (await _store.Items.LoadAsync(itemId))!.Local;
        Assert.Contains(after.LocalFiles, file => file.Hash == "BBBB");
        Assert.Contains(after.LocalFiles, file => file.Paths.Contains(folder + ".zip"));
        Assert.Empty(after.LocalFolders);
    }

    /// <summary>フォルダの登録を外す間に取り込みが足したファイルも消えない（外す方は localFiles の写しも書いていた）。</summary>
    [Fact]
    public async Task フォルダの登録を外す間に取り込みが足したファイルが残る()
    {
        var itemId = await SaveItemAsync("9900012");
        var folder = CreateExtractedFolder();
        await _service.RegisterFolderAsync(itemId, folder);

        var removed = false;
        await ItemLockRace.WhileAnotherWriterChangesAsync(
            _store,
            itemId,
            local => ItemLockRace.AddFile(local),
            async () => removed = await _service.UnregisterFolderAsync(itemId, folder));

        Assert.True(removed);
        var after = (await _store.Items.LoadAsync(itemId))!.Local;
        Assert.Equal("BBBB", Assert.Single(after.LocalFiles).Hash);
        Assert.Empty(after.LocalFolders);
    }
}
