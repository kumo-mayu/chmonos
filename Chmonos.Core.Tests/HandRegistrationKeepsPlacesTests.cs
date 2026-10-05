using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 人の登録操作（このIDで登録・BOOTHに無い商品として登録・IDを変える・zipで登録し直す）は、
/// 同じ商品の**ほかのファイルの無い場所を外さない**（2026-10-05・file-lifecycle.md「気になった所」13）。
///
/// 見つからない物は場所を残して日時（<c>missingSince</c>）で示すのが決まり（data-model.md「この欄のために場所は外さない」）。
/// 前は取り込みと同じ突き合わせ（<see cref="Scanning.LocalFileMerger.Merge"/>）を通っていて、1件登録しただけで、
/// 同じ商品のほかのファイルの覚えていた場所がその場で消えていた（「見つからないファイルを探す」の手掛かりも消える）。
/// </summary>
public sealed class HandRegistrationKeepsPlacesTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _store;
    private readonly ItemService _service;

    public HandRegistrationKeepsPlacesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-keep-places-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);

        // 商品は先に置いておくので、BOOTH へは行かない
        var client = new BoothClient(new HttpClient(new UnreachableHandler()), new AppSettings { FetchIntervalMs = 0 }, TestWait.None);
        _service = new ItemService(_store, client, new ImagePipeline(client, paths));
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("この試験では BOOTH へ行かないはず");
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

    /// <summary>ディスクに無い場所（取り込み元の外へ移した物の代わり）。ドライブはつながっているので、取り込みなら外す場所。</summary>
    private string GonePath(string name) => Path.Combine(_root, "gone", name);

    private string RealFile(string name)
    {
        var path = Path.Combine(_root, "files", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[10]);
        return path;
    }

    private LocalFileRecord Missing(string hash) => new()
    {
        Hash = hash,
        Paths = [GonePath(hash + ".zip")],
        SizeBytes = 10,
        MissingSince = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private async Task SaveItemAsync(string id, params LocalFileRecord[] files)
        => await _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = "作り物の商品", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { LocalFiles = files },
        });

    private async Task PutUnresolvedAsync(string hash, string path)
        => await _store.Unresolved.SaveAsync(
        [
            new UnresolvedFile
            {
                Hash = hash,
                Paths = [path],
                SizeBytes = 10,
                ModifiedAtUtc = DateTimeOffset.Now,
                FirstSeenAt = DateTimeOffset.Now,
            },
        ]);

    private async Task<LocalFileRecord> FileOfAsync(string itemId, string hash)
        => (await _store.Items.LoadAsync(itemId))!.Local.LocalFiles.Single(file => file.Hash == hash);

    [Fact]
    public async Task このIDで登録しても_同じ商品のほかのファイルの無い場所は残る()
    {
        await SaveItemAsync("9900101", Missing("AAAA"));
        await PutUnresolvedAsync("BBBB", RealFile("b.zip"));

        Assert.True(await _service.AssignItemIdAsync("BBBB", "9900101"));

        var kept = await FileOfAsync("9900101", "AAAA");
        Assert.Equal([GonePath("AAAA.zip")], kept.Paths);
        Assert.NotNull(kept.MissingSince);
    }

    [Fact]
    public async Task BOOTHに無い商品へもう一度登録しても_ほかのファイルの無い場所は残る()
    {
        var id = LocalItemId.For("BBBB");
        await SaveItemAsync(id, Missing("AAAA"));
        await PutUnresolvedAsync("BBBB", RealFile("b.zip"));

        Assert.Equal(id, await _service.RegisterLocalItemAsync("BBBB", "作り物"));

        Assert.Equal([GonePath("AAAA.zip")], (await FileOfAsync(id, "AAAA")).Paths);
    }

    [Fact]
    public async Task IDを変えても_無い場所は外さない()
    {
        await SaveItemAsync("9900102", Missing("AAAA"));
        await SaveItemAsync("9900103", Missing("CCCC"));

        Assert.Equal(ItemIdChangeOutcome.Moved, await _service.ChangeItemIdAsync("9900102", "9900103"));

        Assert.Equal([GonePath("AAAA.zip")], (await FileOfAsync("9900103", "AAAA")).Paths);
        Assert.Equal([GonePath("CCCC.zip")], (await FileOfAsync("9900103", "CCCC")).Paths);
    }

    [Fact]
    public async Task zipで登録し直しても_ほかのファイルの無い場所は残る()
    {
        var folder = Path.Combine(_root, "files", "作り物_v1");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "a.txt"), new byte[5]);
        System.IO.Compression.ZipFile.CreateFromDirectory(folder, folder + ".zip");

        await SaveItemAsync("9900104", Missing("AAAA"));
        Assert.True(await _service.RegisterFolderAsync("9900104", folder));

        Assert.Equal(ArchiveSwapResult.Registered, (await _service.SwapFolderForArchiveAsync("9900104", folder)).Result);

        Assert.Equal([GonePath("AAAA.zip")], (await FileOfAsync("9900104", "AAAA")).Paths);
    }

    /// <summary>在る場所が1つでもあれば日時を消すのは今のまま（取り込みと同じ）。登録した物そのものの場所も残る。</summary>
    [Fact]
    public async Task 登録した物が在れば見つからなくなった日時は消える()
    {
        var path = RealFile("a.zip");
        await SaveItemAsync("9900105", Missing("AAAA"));
        await PutUnresolvedAsync("AAAA", path);

        Assert.True(await _service.AssignItemIdAsync("AAAA", "9900105"));

        var file = await FileOfAsync("9900105", "AAAA");
        Assert.Null(file.MissingSince);
        Assert.Equal([GonePath("AAAA.zip"), path], file.Paths);
    }
}
