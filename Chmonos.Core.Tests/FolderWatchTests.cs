using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

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

    /// <summary>
    /// ドライブは在るのに監視フォルダが無い（名前を変えた・移した）なら、見つからない物として返す。
    /// 外付けを外しているのと違い待っても戻らないので、画面が言う（見つからない・移動の点検 9。前は黙っていた）。
    /// </summary>
    [Fact]
    public async Task 名前を変えた監視フォルダを見つからない物として返す()
    {
        WriteFile("衣装_1.00.zip");
        var renamed = Path.Combine(_root, "名前を変える前");

        var result = await _watch.FindNewAsync([_watched, renamed]);

        Assert.Equal([renamed], result.MissingFolders);
        Assert.Equal([_watched], result.Folders);
        Assert.Single(result.NewFiles);
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

    private async Task ExcludeAsync(string path, string hash)
    {
        var excluded = _store.Excluded.Load();
        excluded.Add(new ExcludedEntry
        {
            Hash = hash,
            Paths = [path],
            ExcludedAt = DateTimeOffset.Now,
        });
        await _store.Excluded.SaveAsync(excluded);
    }

    /// <summary>管理から外したファイルは、増えたと数えない。</summary>
    [Fact]
    public async Task IgnoresExcludedFiles()
    {
        var path = WriteFile("unity_backup.zip");
        await RememberAsync(path);
        await ExcludeAsync(path, "CAFEBABE");

        var result = await _watch.FindNewAsync([_watched]);

        Assert.False(result.HasNew);
    }

    /// <summary>
    /// 外したのは中身で、場所ではない（ユーザ判断 2026-09-23）。
    /// 同じ名前で落とし直した更新版は、控えと大きさ・更新日時が合わないので新しいと数える。
    /// 前はパスだけで弾いていたので、起動時の取り込みが拾わなかった。
    /// </summary>
    [Fact]
    public async Task CountsAnExcludedPathAsNewWhenItsContentChanged()
    {
        var path = WriteFile("衣装.zip");
        await RememberAsync(path);
        await ExcludeAsync(path, "CAFEBABE");

        WriteFile("衣装.zip", "更新版の中身");

        var result = await _watch.FindNewAsync([_watched]);

        Assert.Equal([path], result.NewFiles);
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
        var result = await _watch.FindNewAsync([Path.Combine(UnresolvedMergeTests.MissingVolumeFolder(), "外付け")]);

        Assert.False(result.HasNew);
        Assert.Empty(result.Folders);

        // 外付けを外しているだけなので「見つからない」とも言わない（名前を変えた物とは分ける・見つからない・移動の点検 9）
        Assert.Empty(result.MissingFolders);
    }

    [Fact]
    public async Task DoesNothingWithoutWatchedFolders()
    {
        WriteFile("衣装_1.00.zip");

        var result = await _watch.FindNewAsync([]);

        Assert.False(result.HasNew);
    }
}
