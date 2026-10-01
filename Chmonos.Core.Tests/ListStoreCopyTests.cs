using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 写しを持ち、呼び手には入れ物の複製を渡す形（<see cref="JsonFileStore{T}"/> の copyOnLoad。知らせ）。
/// 要確認を開くと知らせのファイル（上限2000件で約1MB）を3回読んでいた。
/// 変わっていなければ読まず、変われば必ず読み直し、呼び手が足し引きしても写しが壊れないことを確かめる。
/// 時計には頼らない（更新日時は試験の中で書き換える）。
/// </summary>
public sealed class ListStoreCopyTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 9, 29, 12, 0, 0, TimeSpan.FromHours(9));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-list-copy-" + Guid.NewGuid().ToString("N"));
    private readonly DataStore _store;

    public ListStoreCopyTests()
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

    private static NotificationRecord Notice(string id, string title = "あ")
        => new() { Id = id, Kind = NotificationKind.ItemUpdated, Title = title, Detail = "d", CreatedAt = At };

    /// <summary>外から書き換える。<paramref name="shiftSeconds"/> が0なら更新日時を元へ戻す。</summary>
    private void EditByHand(Func<string, string> edit, int shiftSeconds)
    {
        var path = _store.Notifications.Path;
        var before = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, edit(File.ReadAllText(path)));
        File.SetLastWriteTimeUtc(path, before.AddSeconds(shiftSeconds));
    }

    [Fact]
    public async Task UnchangedFileIsNotReadAgainButEachCallerGetsItsOwnList()
    {
        await _store.Notifications.SaveAsync([Notice("1"), Notice("2")]);

        var first = _store.Notifications.Load();
        var second = _store.Notifications.Load();

        Assert.NotSame(first, second);
        Assert.Same(first[0], second[0]);
    }

    /// <summary>大きさも日時も同じなら中身を見ない（読んでいないことの確かめ。アプリが書く物ではこうならない）。</summary>
    [Fact]
    public async Task SameSizeAndTimeIsServedFromTheCopy()
    {
        await _store.Notifications.SaveAsync([Notice("1", title: "あ")]);
        _store.Notifications.Load();

        EditByHand(json => json.Replace("\"あ\"", "\"い\""), shiftSeconds: 0);

        Assert.Equal("あ", Assert.Single(_store.Notifications.Load()).Title);
    }

    [Fact]
    public async Task ANewTimeOrSizeIsReadAgain()
    {
        await _store.Notifications.SaveAsync([Notice("1", title: "あ")]);
        _store.Notifications.Load();

        EditByHand(json => json.Replace("\"あ\"", "\"い\""), shiftSeconds: 5);
        Assert.Equal("い", Assert.Single(_store.Notifications.Load()).Title);

        EditByHand(json => json.Replace("\"い\"", "\"長くした\""), shiftSeconds: 0);
        Assert.Equal("長くした", Assert.Single(_store.Notifications.Load()).Title);
    }

    /// <summary>呼び手が返った一覧を足し引きしても、写しと次の呼び手は変わらない（写しを持った後でも）。</summary>
    [Fact]
    public async Task ChangingTheReturnedListDoesNotChangeTheCopy()
    {
        await _store.Notifications.SaveAsync([Notice("1")]);
        _store.Notifications.Load();

        var handed = _store.Notifications.Load();
        handed.Add(Notice("2"));
        handed.RemoveAt(0);

        Assert.Equal("1", Assert.Single(_store.Notifications.Load()).Id);
    }

    /// <summary>このアプリが書いた物は、同じ大きさのまま続けて書き直しても直後の読み込みから新しい。</summary>
    [Fact]
    public async Task OwnWritesAreSeenRightAway()
    {
        await _store.Notifications.SaveAsync([Notice("1", title: "あ")]);
        _store.Notifications.Load();

        for (var round = 0; round < 20; round++)
        {
            var title = round % 2 == 0 ? "い" : "う";
            await _store.Notifications.UpdateAsync(_ => [Notice("1", title: title)]);
            Assert.Equal(title, Assert.Single(_store.Notifications.Load()).Title);
        }
    }

    /// <summary>
    /// 錠の中の読み直しは写しを使わない。写しと大きさも日時も同じまま外で直された物でも、
    /// 書き換えは直された中身に当てる。
    /// </summary>
    [Fact]
    public async Task UpdateReadsTheFileNotTheCopy()
    {
        await _store.Notifications.SaveAsync([Notice("1", title: "あ")]);
        _store.Notifications.Load();

        EditByHand(json => json.Replace("\"あ\"", "\"い\""), shiftSeconds: 0);
        await _store.Notifications.UpdateAsync(list => [.. list, Notice("2")]);

        var saved = _store.Notifications.Load();
        Assert.Equal(["い", "あ"], saved.Select(record => record.Title));
    }

    [Fact]
    public async Task ADeletedFileGivesAnEmptyList()
    {
        await _store.Notifications.SaveAsync([Notice("1")]);
        _store.Notifications.Load();

        File.Delete(_store.Notifications.Path);

        Assert.Empty(_store.Notifications.Load());
    }
}
