using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 監視対象フォルダに新しいファイルが増えていないかを見る経路。
/// 起動時に走らせるので、ハッシュまで計算しないことが要件のうち。
/// </summary>
public class FolderWatchTests : IDisposable
{
    private readonly string _root;
    private readonly string _watched;
    private readonly DataStore _store;
    private readonly FolderWatch _watch;

    public FolderWatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-watch-" + Guid.NewGuid().ToString("N"));
        _watched = Path.Combine(_root, "download");
        Directory.CreateDirectory(_watched);

        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _watch = new FolderWatch(_store);
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

    private string WriteFile(string name, string content = "dummy")
    {
        var path = Path.Combine(_watched, name);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task RememberAsync(string path)
    {
        var info = new FileInfo(path);
        var cache = _store.ScanCache.Load();
        cache.Add(new ScanCacheEntry
        {
            Path = path,
            SizeBytes = info.Length,
            ModifiedAtUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            Hash = "CAFEBABE",
        });

        await _store.ScanCache.SaveAsync(cache);
    }

    [Fact]
    public async Task FindsAFileThatWasNeverScanned()
    {
        WriteFile("衣装_1.00.zip");

        var result = await _watch.FindNewAsync([_watched]);

        Assert.True(result.HasNew);
        Assert.Single(result.NewFiles);
    }

    /// <summary>走査キャッシュに同じパス・サイズ・更新日時があれば、前に見たものと分かる。</summary>
    [Fact]
    public async Task IgnoresAFileAlreadyInTheScanCache()
    {
        var path = WriteFile("衣装_1.00.zip");
        await RememberAsync(path);

        var result = await _watch.FindNewAsync([_watched]);

        Assert.False(result.HasNew);
    }

    /// <summary>同じ名前でも中身が差し替われば、取り込み直す対象になる。</summary>
    [Fact]
    public async Task FindsAFileThatChangedAfterItWasScanned()
    {
        var path = WriteFile("衣装_1.00.zip");
        await RememberAsync(path);

        File.WriteAllText(path, "もっと長い中身に差し替えた");

        var result = await _watch.FindNewAsync([_watched]);

        Assert.True(result.HasNew);
    }

    /// <summary>管理から外したファイルは、増えたと数えない。</summary>
    [Fact]
    public async Task IgnoresExcludedFiles()
    {
        var path = WriteFile("unity_backup.zip");
        var excluded = _store.Excluded.Load();
        excluded.Add(new ExcludedEntry
        {
            Hash = "AAAA",
            Paths = [path],
            ExcludedAt = DateTimeOffset.Now,
        });
        await _store.Excluded.SaveAsync(excluded);

        var result = await _watch.FindNewAsync([_watched]);

        Assert.False(result.HasNew);
    }

    /// <summary>商品へ紐付けたフォルダの中は管理済みなので、増えたと数えない。</summary>
    [Fact]
    public async Task IgnoresFilesInsideAFolderRegisteredToAnItem()
    {
        var inner = Path.Combine(_watched, "展開済み");
        Directory.CreateDirectory(inner);
        File.WriteAllText(Path.Combine(inner, "model.unitypackage"), "dummy");

        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "111",
            Booth = new BoothBlock { Name = "テスト商品", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock
            {
                LocalFolders = [new LocalFolderRecord { Path = inner, RegisteredAt = DateTimeOffset.Now }],
            },
        });

        var result = await _watch.FindNewAsync([_watched]);

        Assert.False(result.HasNew);
    }

    /// <summary>
    /// 外付けを外している間は「増えていない」と見なす。
    /// 消えたことにして監視から外すと、つなぎ直したときに戻す手が要る。
    /// </summary>
    [Fact]
    public async Task SkipsAFolderThatIsNotThereRightNow()
    {
        var result = await _watch.FindNewAsync([Path.Combine(_root, "外付け")]);

        Assert.False(result.HasNew);
        Assert.Empty(result.Folders);
    }

    [Fact]
    public async Task DoesNothingWithoutWatchedFolders()
    {
        WriteFile("衣装_1.00.zip");

        var result = await _watch.FindNewAsync([]);

        Assert.False(result.HasNew);
    }
}
