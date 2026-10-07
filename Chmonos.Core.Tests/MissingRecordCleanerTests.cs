using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 見つからないファイル・フォルダの記録をまとめて消す（ユーザ判断 2026-10-07）。
/// 消すのは見つからない記録だけで、今は在る物・つながっていないドライブの上の物・外した物・古い版は残す。商品そのものは消さない
/// </summary>
public sealed class MissingRecordCleanerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-forget-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly DataStore _store;

    public MissingRecordCleanerTests()
    {
        var paths = new AppPaths(Path.Combine(_root, "store"));
        paths.EnsureCreated();
        _store = new DataStore(paths);
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

    private static LocalFileRecord File(string hash, string path, bool missing = true, bool detached = false, bool oldVersion = false) => new()
    {
        Hash = hash,
        // 古い版は、上書きで場所が外れ、印の付いた物
        Paths = oldVersion ? [] : [path],
        SizeBytes = 3,
        MissingSince = missing ? DateTimeOffset.Now.AddDays(-1) : null,
        Detached = detached,
        Replaced = oldVersion ? new ReplacedVersion(path, DateTimeOffset.Now.AddDays(-2)) : null,
    };

    private string Gone(string name) => Path.Combine(_root, "消えた", name);

    private static string UnusedDrive()
    {
        var used = DriveInfo.GetDrives().Select(drive => drive.Name[0]).ToHashSet();
        return $@"{"ZYXWVUTSRQPONMLKJIHGFED".First(letter => !used.Contains(letter))}:\";
    }

    private Task SaveAsync(string id, LocalBlock local)
        => _store.Items.SaveAsync(new ItemRecord { Id = id, Booth = new BoothBlock(), Local = local });

    [Fact]
    public async Task 見つからない記録だけを消し_在る物とつながっていないドライブの物と外した物と古い版は残す()
    {
        var here = Path.Combine(_root, "在る.zip");
        System.IO.File.WriteAllBytes(here, [1, 2, 3]);
        var detachedDrive = Path.Combine(UnusedDrive(), "外付け", "a.zip");
        await SaveAsync("9900801", new LocalBlock
        {
            LocalFiles =
            [
                File("h-gone", Gone("a.zip")),
                File("h-back", here),
                File("h-detached-drive", detachedDrive),
                File("h-detached", Gone("b.zip"), detached: true),
                File("h-old", Gone("c.zip"), oldVersion: true),
            ],
            LocalFolders = [new LocalFolderRecord { Path = Gone("フォルダ"), MissingSince = DateTimeOffset.Now }],
        });

        var cleaner = new MissingRecordCleaner(_store);
        var plan = await cleaner.PlanAsync();
        var done = await cleaner.ForgetAsync(plan);

        Assert.Equal(plan.Counts, done);
        Assert.Equal(new MissingRecordCleanup(Items: 1, Files: 1, Folders: 1, Unowned: 0), done);
        var item = Assert.IsType<ItemRecord>(await _store.Items.LoadAsync("9900801"));
        Assert.Equal(["h-back", "h-detached-drive", "h-detached", "h-old"], item.Local.LocalFiles.Select(file => file.Hash));
        Assert.Empty(item.Local.LocalFolders);
    }

    /// <summary>手元のファイルが全部見つからない商品は、記録を消すと未所持になる。商品そのもの（メモ）は残る。</summary>
    [Fact]
    public async Task 全部見つからない商品は未所持になり_商品は残る()
    {
        await SaveAsync("9900802", new LocalBlock { Memo = "残る", LocalFiles = [File("h1", Gone("x.zip")), File("h2", Gone("y.zip"))] });
        await SaveAsync("9900803", new LocalBlock { LocalFiles = [File("h3", Gone("z.zip"), missing: false)] });

        var cleaner = new MissingRecordCleaner(_store);
        var done = await cleaner.ForgetAsync(await cleaner.PlanAsync());

        Assert.Equal(new MissingRecordCleanup(Items: 1, Files: 2, Folders: 0, Unowned: 1), done);
        var item = Assert.IsType<ItemRecord>(await _store.Items.LoadAsync("9900802"));
        Assert.Equal("残る", item.Local.Memo);
        Assert.False(item.IsOwned);

        // 日時の付いていない物（まだ見回っていない）は消さない
        Assert.Single((await _store.Items.LoadAsync("9900803"))!.Local.LocalFiles);
    }

    /// <summary>場所が空の記録（取り込みが無い場所を全部外した物）も、見つからない物として消す。</summary>
    [Fact]
    public async Task 場所が空の記録も消す()
    {
        await SaveAsync("9900804", new LocalBlock
        {
            LocalFiles = [new LocalFileRecord { Hash = "h-empty", Paths = [], SizeBytes = 3 }, File("h-keep", Gone("k.zip"), missing: false)],
        });

        var cleaner = new MissingRecordCleaner(_store);
        var done = await cleaner.ForgetAsync(await cleaner.PlanAsync());

        Assert.Equal(1, done.Files);
        Assert.Equal(["h-keep"], (await _store.Items.LoadAsync("9900804"))!.Local.LocalFiles.Select(file => file.Hash));
    }

    /// <summary>
    /// 確かめられない物（親のフォルダを読む権限が無い等）は消さない（外部の点検 2026-10-07）。
    /// 前は在るかを File.Exists だけで見ていて、確かめられない物も「無い」として消せた
    /// </summary>
    [Fact]
    public async Task 確かめられない物は消さない()
    {
        var locked = Gone("読めない.zip");
        await SaveAsync("9900805", new LocalBlock { LocalFiles = [File("h-locked", locked), File("h-gone", Gone("d.zip"))] });
        var cleaner = new MissingRecordCleaner(_store, () => new FilePresenceProbe(
            fileState: path => path == locked ? DiskAnswer.Unknown : DiskAnswer.Missing,
            rootExists: _ => true));

        var done = await cleaner.ForgetAsync(await cleaner.PlanAsync());

        Assert.Equal(1, done.Files);
        Assert.Equal(["h-locked"], (await _store.Items.LoadAsync("9900805"))!.Local.LocalFiles.Select(file => file.Hash));
    }

    /// <summary>
    /// 数えた後で日時が付いた物は消さない（外部の点検 2026-10-07）。確かめの窓を出している間に見回りが付けた物まで消すと、
    /// 窓で見せた件数を超えた
    /// </summary>
    [Fact]
    public async Task 数えた後で見つからなくなった物は消さない()
    {
        await SaveAsync("9900806", new LocalBlock { LocalFiles = [File("h-first", Gone("e.zip")), File("h-later", Gone("f.zip"), missing: false)] });
        var cleaner = new MissingRecordCleaner(_store);
        var plan = await cleaner.PlanAsync();

        await _store.Items.ChangeLocalAsync("9900806", local => local with
        {
            LocalFiles = [.. local.LocalFiles.Select(file => file.Hash == "h-later" ? file with { MissingSince = DateTimeOffset.Now } : file)],
        }, [LocalField.LocalFiles]);
        var done = await cleaner.ForgetAsync(plan);

        Assert.Equal(1, done.Files);
        Assert.Equal(["h-later"], (await _store.Items.LoadAsync("9900806"))!.Local.LocalFiles.Select(file => file.Hash));
    }

    /// <summary>消した場所は走査の控えからも外す。外さないと、同じファイルを戻しても監視が新しいと数えない。</summary>
    [Fact]
    public async Task 消した場所は走査の控えからも外す()
    {
        var gone = Gone("g.zip");
        var other = Gone("ほかの.zip");
        await SaveAsync("9900807", new LocalBlock { LocalFiles = [File("h-g", gone)] });
        await _store.ScanCache.UpdateAsync(_ => [
            new ScanCacheEntry { Path = gone, SizeBytes = 3, ModifiedAtUtc = DateTimeOffset.UtcNow, Hash = "h-g" },
            new ScanCacheEntry { Path = other, SizeBytes = 3, ModifiedAtUtc = DateTimeOffset.UtcNow, Hash = "h-o" },
        ]);
        var cleaner = new MissingRecordCleaner(_store);

        await cleaner.ForgetAsync(await cleaner.PlanAsync());

        Assert.Equal([other], (await _store.ScanCache.LoadAsync()).Select(entry => entry.Path));
    }
}
