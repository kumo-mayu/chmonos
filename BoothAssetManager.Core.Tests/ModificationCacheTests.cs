using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 読んだ改変の写し（<see cref="ModificationRepository"/>）。商品ページを開くたびに改変を全部読んでいた（300件で毎回300ファイル）。
/// 変わっていないファイルは読み直さないが、**変わった物は必ず読み直す**ことと、錠の中の読み直しは写しを使わないことを確かめる。
/// 時計には頼らない（更新日時は試験の中で書き換える）。
/// </summary>
public sealed class ModificationCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-mod-cache-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly DataStore _store;

    public ModificationCacheTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new DataStore(_paths);
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

    private Task SaveAsync(string id, string memo = "前")
        => _store.Modifications.SaveAsync(new ModificationRecord
        {
            Id = id,
            AvatarItemId = "100",
            Name = "改変" + id,
            Memo = memo,
            Members = [new ModificationMember { ItemId = "200" }],
        });

    /// <summary>外から書き換える。<paramref name="shiftSeconds"/> が0なら更新日時を元へ戻す（大きさだけで見分けるか・見分けられないか）。</summary>
    private void EditByHand(string id, Func<string, string> edit, int shiftSeconds)
    {
        var path = _paths.ModificationFile(id);
        var before = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, edit(File.ReadAllText(path)));
        File.SetLastWriteTimeUtc(path, before.AddSeconds(shiftSeconds));
    }

    [Fact]
    public async Task UnchangedFilesAreNotReadAgain()
    {
        await SaveAsync("mod-00000001");
        await SaveAsync("mod-00000002");

        var first = await _store.Modifications.LoadAllAsync();
        var second = await _store.Modifications.LoadAllAsync();

        Assert.Equal(2, second.Modifications.Count);
        foreach (var record in second.Modifications)
        {
            Assert.Same(first.Modifications.Single(other => other.Id == record.Id), record);
        }
    }

    /// <summary>
    /// 大きさも日時も同じなら中身を見ない（読んでいないことの確かめ）。
    /// これは写しの約束の外（同じ大きさで同じ時刻の刻みの中に外から書かれた）で、アプリが書く物ではこうならない。
    /// </summary>
    [Fact]
    public async Task SameSizeAndTimeIsServedFromTheCopy()
    {
        await SaveAsync("mod-00000001", memo: "あい");
        await _store.Modifications.LoadAllAsync();

        EditByHand("mod-00000001", json => json.Replace("\"あい\"", "\"うえ\""), shiftSeconds: 0);

        Assert.Equal("あい", (await _store.Modifications.LoadAsync("mod-00000001"))!.Memo);
    }

    [Fact]
    public async Task ANewTimeIsReadAgain()
    {
        await SaveAsync("mod-00000001", memo: "あい");
        await _store.Modifications.LoadAllAsync();

        EditByHand("mod-00000001", json => json.Replace("\"あい\"", "\"うえ\""), shiftSeconds: 5);

        Assert.Equal("うえ", (await _store.Modifications.LoadAllAsync()).Modifications.Single().Memo);
        Assert.Equal("うえ", (await _store.Modifications.LoadAsync("mod-00000001"))!.Memo);
    }

    [Fact]
    public async Task ANewSizeIsReadAgainEvenWithTheSameTime()
    {
        await SaveAsync("mod-00000001", memo: "前");
        await _store.Modifications.LoadAllAsync();

        EditByHand("mod-00000001", json => json.Replace("\"前\"", "\"手で直した\""), shiftSeconds: 0);

        Assert.Equal("手で直した", (await _store.Modifications.LoadAllAsync()).Modifications.Single().Memo);
    }

    /// <summary>このアプリが書いた物は、書き終えた直後の読み込みから新しい。同じ大きさで続けて書き直しても取り違えない。</summary>
    [Fact]
    public async Task OwnWritesAreSeenRightAway()
    {
        await SaveAsync("mod-00000001", memo: "あ");
        await _store.Modifications.LoadAllAsync();

        for (var round = 0; round < 20; round++)
        {
            var memo = round % 2 == 0 ? "い" : "う";
            await _store.Modifications.UpdateAsync("mod-00000001", record => record with { Memo = memo });
            Assert.Equal(memo, (await _store.Modifications.LoadAllAsync()).Modifications.Single().Memo);
        }
    }

    /// <summary>
    /// 錠の中の読み直しは写しを使わない。写しと大きさも日時も同じまま外で直された物でも、
    /// 書き換えは直された中身に当てる（写しに当てると外で直した分が消える）。
    /// </summary>
    [Fact]
    public async Task UpdateReadsTheFileNotTheCopy()
    {
        await SaveAsync("mod-00000001", memo: "あい");
        await _store.Modifications.LoadAllAsync();

        EditByHand("mod-00000001", json => json.Replace("\"あい\"", "\"うえ\""), shiftSeconds: 0);
        await _store.Modifications.UpdateAsync("mod-00000001", record => record with { Name = "変えた" });

        var reread = (await _store.Modifications.LoadAsync("mod-00000001"))!;
        Assert.Equal("うえ", reread.Memo);
        Assert.Equal("変えた", reread.Name);
    }

    [Fact]
    public async Task DeletedFilesDisappear()
    {
        await SaveAsync("mod-00000001");
        await SaveAsync("mod-00000002");
        await _store.Modifications.LoadAllAsync();

        _store.Modifications.Delete("mod-00000001");
        File.Delete(_paths.ModificationFile("mod-00000002"));

        Assert.Empty((await _store.Modifications.LoadAllAsync()).Modifications);
        Assert.Null(await _store.Modifications.LoadAsync("mod-00000002"));
    }

    /// <summary>呼び手が <c>with</c> で作り替えても、共有している写しは変わらない。</summary>
    [Fact]
    public async Task ChangingWhatWasReturnedDoesNotChangeTheCopy()
    {
        await SaveAsync("mod-00000001", memo: "前");
        var loaded = (await _store.Modifications.LoadAllAsync()).Modifications.Single();

        var changed = loaded with { Memo = "変えた", Members = [] };

        Assert.NotSame(loaded, changed);
        var again = (await _store.Modifications.LoadAllAsync()).Modifications.Single();
        Assert.Equal("前", again.Memo);
        Assert.Single(again.Members);
    }

    /// <summary>壊れたファイルは、前に読めた写しで隠さず「読めなかった」に出す。</summary>
    [Fact]
    public async Task ABrokenFileIsReportedNotHiddenByTheOldCopy()
    {
        await SaveAsync("mod-00000001");
        await _store.Modifications.LoadAllAsync();

        EditByHand("mod-00000001", _ => "{ 壊れた", shiftSeconds: 5);

        var result = await _store.Modifications.LoadAllAsync();
        Assert.Empty(result.Modifications);
        Assert.Equal(["mod-00000001"], result.FailedIds);
    }
}
