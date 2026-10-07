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
        var done = await cleaner.ForgetAsync();

        Assert.Equal(plan, done);
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

        var done = await new MissingRecordCleaner(_store).ForgetAsync();

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

        var done = await new MissingRecordCleaner(_store).ForgetAsync();

        Assert.Equal(1, done.Files);
        Assert.Equal(["h-keep"], (await _store.Items.LoadAsync("9900804"))!.Local.LocalFiles.Select(file => file.Hash));
    }
}
