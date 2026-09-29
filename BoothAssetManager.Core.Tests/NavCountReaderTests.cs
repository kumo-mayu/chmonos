using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// ナビの件数（<see cref="NavCountReader"/>）。取り込み中は商品1件あたり約2回数え直しが頼まれ、そのたびに
/// 未確定と知らせ（約1MB）を読んでいた。2つのファイルが同じなら読まず、変われば必ず読み直すことを確かめる。
/// 時計には頼らない（更新日時は試験の中で書き換える）。
/// </summary>
public sealed class NavCountReaderTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 9, 29, 12, 0, 0, TimeSpan.FromHours(9));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-nav-counts-" + Guid.NewGuid().ToString("N"));
    private readonly DataStore _store;

    public NavCountReaderTests()
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

    private static NotificationRecord Notice(string id, bool isRead = false, bool isResolved = false,
        NotificationKind kind = NotificationKind.ItemUpdated, string detail = "", int minutes = 0)
        => new()
        {
            Id = id,
            Kind = kind,
            Title = "知らせ" + id,
            Detail = detail,
            CreatedAt = At.AddMinutes(minutes),
            IsRead = isRead,
            IsResolved = isResolved,
        };

    private static UnresolvedFile Unresolved(string hash)
        => new()
        {
            Hash = hash,
            Paths = [@"C:\x\" + hash + ".zip"],
            SizeBytes = 1,
            ModifiedAtUtc = At,
            FirstSeenAt = At,
        };

    /// <summary>外から中身だけ差し替える。大きさと更新日時は元のまま（読めば数が変わる・読まなければ前の数のまま）。</summary>
    private static void ReplaceKeepingSizeAndTime(string path)
    {
        var info = new FileInfo(path);
        var time = info.LastWriteTimeUtc;
        File.WriteAllBytes(path, Enumerable.Repeat((byte)' ', (int)info.Length).ToArray());
        File.SetLastWriteTimeUtc(path, time);
    }

    [Fact]
    public void CountsUnreadUnresolvedAndTheNewestStructureAlert()
    {
        var counts = NavCountReader.Count(3,
        [
            Notice("1"),
            Notice("2", isRead: true),
            Notice("3", isResolved: true),
            Notice("4", kind: NotificationKind.PageStructureChanged, detail: "古い", minutes: 1, isRead: true),
            Notice("5", kind: NotificationKind.PageStructureChanged, detail: "新しい", minutes: 2, isRead: true),
            Notice("6", kind: NotificationKind.PageStructureChanged, detail: "解消済み", minutes: 3, isResolved: true),
        ]);

        Assert.Equal(new NavCounts(3, 1, "新しい"), counts);
    }

    [Fact]
    public void NoFilesCountsNothing()
        => Assert.Equal(new NavCounts(0, 0, string.Empty), new NavCountReader(_store).Read());

    [Fact]
    public async Task UnchangedFilesAreNotReadAgain()
    {
        await _store.Notifications.SaveAsync([Notice("1"), Notice("2")]);
        await _store.Unresolved.SaveAsync([Unresolved("a")]);
        var reader = new NavCountReader(_store);
        Assert.Equal(new NavCounts(1, 2, string.Empty), reader.Read());

        ReplaceKeepingSizeAndTime(_store.Notifications.Path);
        ReplaceKeepingSizeAndTime(_store.Unresolved.Path);

        Assert.Equal(new NavCounts(1, 2, string.Empty), reader.Read());
    }

    [Fact]
    public async Task ANewTimeIsReadAgain()
    {
        await _store.Notifications.SaveAsync([Notice("1"), Notice("2")]);
        var reader = new NavCountReader(_store);
        reader.Read();

        var path = _store.Notifications.Path;
        var time = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "[]");
        File.SetLastWriteTimeUtc(path, time.AddSeconds(5));

        Assert.Equal(0, reader.Read().Unread);
    }

    [Fact]
    public async Task ANewSizeIsReadAgainEvenWithTheSameTime()
    {
        await _store.Unresolved.SaveAsync([Unresolved("a")]);
        var reader = new NavCountReader(_store);
        Assert.Equal(1, reader.Read().Unresolved);

        var path = _store.Unresolved.Path;
        var time = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "[]");
        File.SetLastWriteTimeUtc(path, time);

        Assert.Equal(0, reader.Read().Unresolved);
    }

    /// <summary>このアプリの書き込みは、同じ大きさのまま続けて書き直しても取り違えない（書き込みの数も見る）。</summary>
    [Fact]
    public async Task OwnWritesAreSeenRightAway()
    {
        var reader = new NavCountReader(_store);

        for (var round = 0; round < 20; round++)
        {
            var detail = round % 2 == 0 ? "あ" : "い";
            await _store.Notifications.UpdateAsync(
                _ => [Notice("1", kind: NotificationKind.PageStructureChanged, detail: detail)]);
            Assert.Equal(detail, reader.Read().StructureAlert);
        }
    }

    [Fact]
    public async Task ADeletedFileCountsAsEmpty()
    {
        await _store.Unresolved.SaveAsync([Unresolved("a")]);
        var reader = new NavCountReader(_store);
        reader.Read();

        File.Delete(_store.Unresolved.Path);

        Assert.Equal(0, reader.Read().Unresolved);
    }
}
