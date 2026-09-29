using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 足跡（<c>recent.json</c>）の写し。「最近」で絞るたびに画面のスレッドで3〜4回読んでいた。
/// 変わっていなければ同じ物を返し、変われば読み直し、錠の中の書き換えは写しでなくファイルに当てることを確かめる。
/// 時計には頼らない（時刻は決め打ち・更新日時は試験の中で書き換える）。
/// </summary>
public sealed class RecentStoreCacheTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 9, 29, 12, 0, 0, TimeSpan.FromHours(9));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-recent-cache-" + Guid.NewGuid().ToString("N"));
    private readonly DataStore _store;

    public RecentStoreCacheTests()
    {
        var paths = new AppPaths(_root);
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

    private Task TouchAsync(string itemId, RecentKind kind)
        => _store.Recent.UpdateAsync(log => new RecentLog { Entries = RecentActivity.Touch(log.Entries, itemId, kind, At) });

    private void EditByHand(Func<string, string> edit, int shiftSeconds)
    {
        var path = _store.Recent.Path;
        var before = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, edit(File.ReadAllText(path)));
        File.SetLastWriteTimeUtc(path, before.AddSeconds(shiftSeconds));
    }

    [Fact]
    public async Task SharedUntilItChanges()
    {
        await TouchAsync("11", RecentKind.Viewed);

        var first = _store.Recent.Load();
        Assert.Same(first, _store.Recent.Load());

        await TouchAsync("22", RecentKind.Used);
        var afterWrite = _store.Recent.Load();
        Assert.NotSame(first, afterWrite);
        Assert.Equal(["11", "22"], afterWrite.Entries.Select(entry => entry.ItemId));

        EditByHand(json => json.Replace("\"22\"", "\"33\""), shiftSeconds: 5);
        Assert.Equal(["11", "33"], _store.Recent.Load().Entries.Select(entry => entry.ItemId));
    }

    /// <summary>錠の中の書き換えは、写しと大きさも日時も同じまま外で直された中身に当てる。</summary>
    [Fact]
    public async Task UpdateReadsTheFileNotTheCopy()
    {
        await TouchAsync("11", RecentKind.Viewed);
        _store.Recent.Load();

        EditByHand(json => json.Replace("\"11\"", "\"12\""), shiftSeconds: 0);
        await TouchAsync("22", RecentKind.Added);

        Assert.Equal(["12", "22"], _store.Recent.Load().Entries.Select(entry => entry.ItemId));
    }
}
