using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 走査の控え（scan-cache.json）の掃除と書き方（2026-09-24）。
/// 前は無くなったパスを落とす所がどこからも呼ばれず、取り込みは読んだ時の写しで丸ごと書いていた。
/// </summary>
public sealed class ScanCacheCleanupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-scan-cache-clean-" + Guid.NewGuid().ToString("N"));

    public ScanCacheCleanupTests() => Directory.CreateDirectory(_root);

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

    private static ScanCacheEntry Entry(string path) => new()
    {
        Path = path,
        SizeBytes = 1,
        ModifiedAtUtc = DateTimeOffset.UnixEpoch,
        Hash = "h" + path.Length,
    };

    [Fact]
    public void DropsOnlyMissingPathsUnderTheImportedFolders()
    {
        var index = new ScanCacheIndex(
        [
            Entry(@"D:\取り込み元\消した.zip"),
            Entry(@"D:\取り込み元\ある.zip"),
            Entry(@"D:\取り込み元の隣\消した.zip"),
            Entry(@"E:\外付け\消した.zip"),
            Entry(@"F:\直下の消した.zip"),
        ]);
        HashSet<string> present = [@"D:\取り込み元\ある.zip"];

        var dropped = index.RemoveMissingUnder(
            [@"D:\取り込み元\", @"E:\外付け", @"F:\"],
            exists: present.Contains,
            onMissingVolume: path => path.StartsWith(@"E:\", StringComparison.Ordinal));

        Assert.Equal(2, dropped);
        Assert.Equal(
            [@"D:\取り込み元\ある.zip", @"D:\取り込み元の隣\消した.zip", @"E:\外付け\消した.zip"],
            index.ToList().Select(entry => entry.Path).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// 今回見た場所と同じ中身の、もう無い場所（移した元）だけを落とす。在る場所・外付けの上・別の中身は残す
    /// （見つからない・移動の点検 5）。
    /// </summary>
    [Fact]
    public void DropsOnlyTheGonePlacesOfContentSeenElsewhere()
    {
        static ScanCacheEntry With(string path, string hash) => new()
        {
            Path = path,
            SizeBytes = 1,
            ModifiedAtUtc = DateTimeOffset.UnixEpoch,
            Hash = hash,
        };

        var index = new ScanCacheIndex(
        [
            With(@"D:\監視\移した先.zip", "A"),
            With(@"D:\監視\移した元.zip", "A"),
            With(@"D:\監視\写し.zip", "A"),
            With(@"E:\外付け\同じ中身.zip", "A"),
            With(@"D:\監視\別の中身.zip", "B"),
        ]);
        HashSet<string> present = [@"D:\監視\移した先.zip", @"D:\監視\写し.zip"];

        var dropped = index.RemoveMovedAway(
            [@"D:\監視\移した先.zip"],
            exists: present.Contains,
            onMissingVolume: path => path.StartsWith(@"E:\", StringComparison.Ordinal));

        Assert.Equal(1, dropped);
        Assert.Equal(
            new[] { @"D:\監視\移した先.zip", @"D:\監視\写し.zip", @"D:\監視\別の中身.zip", @"E:\外付け\同じ中身.zip" }.Order(StringComparer.Ordinal),
            index.ToList().Select(entry => entry.Path).Order(StringComparer.Ordinal));
    }

    /// <summary>書くときは、今の控えに足した・落とした分だけを重ねる（ほかの書き手が足した分を消さない）。</summary>
    [Fact]
    public void MergesOnlyItsOwnChangesIntoTheCurrentFile()
    {
        var index = new ScanCacheIndex([Entry(@"D:\a\古い.zip"), Entry(@"D:\a\残す.zip")]);
        index.Set(@"D:\a\新しい.zip", 2, DateTimeOffset.UnixEpoch, "new");
        index.RemoveMissingUnder([@"D:\a"], exists: path => path.EndsWith("残す.zip", StringComparison.Ordinal) || path.EndsWith("新しい.zip", StringComparison.Ordinal), onMissingVolume: _ => false);

        // 読んだ後に、ほかの書き手（見つからないファイルを探す所）が足した
        var current = new List<ScanCacheEntry> { Entry(@"D:\a\古い.zip"), Entry(@"D:\a\残す.zip"), Entry(@"G:\監視\見つけた.zip") };

        Assert.True(index.HasChanges);
        var merged = index.MergeInto(current);

        Assert.Equal(
            [@"D:\a\新しい.zip", @"D:\a\残す.zip", @"G:\監視\見つけた.zip"],
            merged.Select(entry => entry.Path).Order(StringComparer.Ordinal));
        Assert.False(index.HasChanges);
    }

    /// <summary>取り込みの終わりに、取り込み元から消えたファイルの控えを落とす。取り込み元の外の控えは残す。</summary>
    [Fact]
    public async Task AnImportDropsFilesThatLeftItsFolder()
    {
        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        var store = new DataStore(paths);
        var source = Path.Combine(_root, "取り込み元");
        Directory.CreateDirectory(source);
        var kept = Path.Combine(source, "残す.psd");
        var removed = Path.Combine(source, "消す.psd");
        File.WriteAllBytes(kept, [1]);
        File.WriteAllBytes(removed, [2]);
        await store.ScanCache.SaveAsync([Entry(Path.Combine(_root, "別の場所", "x.zip"))]);

        var client = new OffUiThreadTests.OfflineClient();
        var settings = new AppSettings { SaveImages = false };
        var pipeline = new ImportPipeline(store, client, new ImagePipeline(client, paths, settings), settings);

        await pipeline.RunAsync([source]);
        Assert.Contains(store.ScanCache.Load(), entry => entry.Path == removed);

        File.Delete(removed);
        await pipeline.RunAsync([source]);

        var cached = store.ScanCache.Load().Select(entry => entry.Path).ToList();
        Assert.Contains(kept, cached);
        Assert.DoesNotContain(removed, cached);
        Assert.Contains(Path.Combine(_root, "別の場所", "x.zip"), cached);
        Assert.Equal(0, client.Calls);
    }
}
