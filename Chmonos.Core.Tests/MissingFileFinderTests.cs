using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 動かしたファイルを中身で見つけて結び直す（G17）。
/// ファイルの同一性はハッシュで持っているので、移した・名前を変えただけなら元に戻せる。
/// </summary>
public class MissingFileFinderTests : IDisposable
{
    private readonly string _root;
    private readonly string _watched;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly MissingFileFinder _finder;

    public MissingFileFinderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-missing-" + Guid.NewGuid().ToString("N"));
        _watched = Path.Combine(_root, "watched");
        Directory.CreateDirectory(_watched);

        _paths = new AppPaths(Path.Combine(_root, "store"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);
        _finder = new MissingFileFinder(_store);
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

    private async Task<string> SaveItemWithFileAsync(string itemId, string fileName, string content)
    {
        var path = Path.Combine(_watched, fileName);
        await File.WriteAllTextAsync(path, content);

        var hash = await FileHasher.ComputeSha256Async(path);
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = itemId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "テスト" },
            Local = new LocalBlock
            {
                LocalFiles =
                [
                    new LocalFileRecord
                    {
                        Hash = hash,
                        SizeBytes = new FileInfo(path).Length,
                        Paths = [path],
                    },
                ],
            },
        });

        return path;
    }

    [Fact]
    public async Task RelinksAFileThatMovedWithinTheWatchedFolder()
    {
        var path = await SaveItemWithFileAsync("111", "衣装.zip", "なかみ");

        // 移した（名前も変えた）
        var moved = Path.Combine(_watched, "sub", "衣装_v2.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.Move(path, moved);

        var result = await _finder.FindAsync([_watched]);

        Assert.Equal(1, result.MissingBefore);
        Assert.Equal(1, result.Relinked);
        Assert.Equal(0, result.StillMissing);

        var item = await _store.Items.LoadAsync("111");
        Assert.Equal(moved, Assert.Single(Assert.Single(item!.Local.LocalFiles).Paths));
    }

    [Fact]
    public async Task SaysNothingIsMissingWhenEveryFileIsWhereItShouldBe()
    {
        await SaveItemWithFileAsync("111", "衣装.zip", "なかみ");

        var result = await _finder.FindAsync([_watched]);

        Assert.Equal(0, result.MissingBefore);
        Assert.Equal(0, result.Relinked);
    }

    /// <summary>監視フォルダの外へ出た物は見つからない。そう言えるようにしておく。</summary>
    [Fact]
    public async Task ReportsWhatItCouldNotFind()
    {
        var path = await SaveItemWithFileAsync("111", "衣装.zip", "なかみ");
        File.Delete(path);

        var result = await _finder.FindAsync([_watched]);

        Assert.Equal(1, result.MissingBefore);
        Assert.Equal(0, result.Relinked);
        Assert.Equal(1, result.StillMissing);
    }

    /// <summary>今つながっていないフォルダは「無い」ではなく「見られなかった」。</summary>
    [Fact]
    public async Task TellsApartFoldersItCouldNotReach()
    {
        var path = await SaveItemWithFileAsync("111", "衣装.zip", "なかみ");
        File.Delete(path);

        var result = await _finder.FindAsync([Path.Combine(_root, "外付け")]);

        Assert.Equal(Path.Combine(_root, "外付け"), Assert.Single(result.Unreachable));
        Assert.Equal(0, result.Relinked);
    }

    /// <summary>
    /// 計算したハッシュは走査の控えに足す（2026-09-24）。前は捨てていたので、探すたびに同じファイルを読み直していた。
    /// 控えにあった物は残す（錠の中で今の控えに足す）。
    /// </summary>
    [Fact]
    public async Task HashesAreKeptInTheScanCache()
    {
        var path = await SaveItemWithFileAsync("111", "衣装.zip", "なかみ");
        var moved = Path.Combine(_watched, "移した.zip");
        File.Move(path, moved);
        File.WriteAllText(Path.Combine(_watched, "別の.zip"), "べつの"); // 同じ大きさで中身が違う物
        await _store.ScanCache.SaveAsync(
        [
            new ScanCacheEntry { Path = @"Z:\前から.zip", SizeBytes = 1, ModifiedAtUtc = DateTimeOffset.UnixEpoch, Hash = "ab" },
        ]);

        var first = await _finder.FindAsync([_watched]);

        Assert.Equal((1, 1, 2), (first.MissingBefore, first.Relinked, first.Hashed));
        var cached = _store.ScanCache.Load();
        Assert.Contains(cached, entry => entry.Path == moved);
        Assert.Contains(cached, entry => entry.Path == Path.Combine(_watched, "別の.zip"));
        Assert.Contains(cached, entry => entry.Path == @"Z:\前から.zip");

        // 記録がまた古い場所を指しても、控えから引けるので読み直さない
        await _store.Items.ChangeLocalAsync(
            "111",
            local => local with { LocalFiles = [local.LocalFiles[0] with { Paths = [path] }] },
            [LocalField.LocalFiles]);

        var second = await _finder.FindAsync([_watched]);

        Assert.Equal(1, second.Relinked);
        Assert.Equal(0, second.Hashed);
    }
}
