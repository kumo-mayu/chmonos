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
