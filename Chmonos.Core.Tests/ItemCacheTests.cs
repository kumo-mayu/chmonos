using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 読んだ商品の写し（<see cref="ItemRepository"/>）と、読んだ登録簿の写し（<see cref="JsonFileStore{T}"/>）。
/// 全件の読み込みは約40か所から呼ばれ、起動だけで5〜6回走っていた（2000件で1回約0.5秒・45MB）。
/// 変わっていないファイルは読み直さないが、**変わった物は必ず読み直す**ことを確かめる。
/// </summary>
public sealed class ItemCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-item-cache-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly DataStore _store;

    public ItemCacheTests()
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

    private Task SaveAsync(string id, string? memo = null)
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = "商品" + id },
            Local = new LocalBlock { Memo = memo },
        });

    /// <summary>
    /// 手で直したことにする。**更新日時をはっきり動かす**（同じ大きさで同じ時刻の刻みの中に外から書き直された物は、
    /// 写しの約束の外にある。<see cref="ItemRepository"/> の説明）。
    /// </summary>
    private void EditByHand(string id, Func<string, string> edit)
    {
        var path = _paths.ItemFile(id);
        var before = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, edit(File.ReadAllText(path)));
        File.SetLastWriteTimeUtc(path, before.AddSeconds(5));
    }

    [Fact]
    public async Task UnchangedFilesAreNotReadAgain()
    {
        await SaveAsync("1");
        await SaveAsync("2");

        var first = await _store.Items.LoadAllAsync();
        var second = await _store.Items.LoadAllAsync();

        Assert.Equal(2, second.Items.Count);
        foreach (var item in second.Items)
        {
            Assert.Same(first.Items.Single(other => other.Id == item.Id), item);
        }
    }

    /// <summary>このアプリが書いた物は、書き終えた直後の読み込みから新しい（1件でも全件でも）。</summary>
    [Fact]
    public async Task OwnWritesAreSeenRightAway()
    {
        await SaveAsync("1", memo: "前");
        await _store.Items.LoadAllAsync();

        await _store.Items.ChangeLocalAsync("1", local => local with { Memo = "後" }, [LocalField.Memo]);

        Assert.Equal("後", (await _store.Items.LoadAsync("1"))!.Local.Memo);
        Assert.Equal("後", (await _store.Items.LoadAllAsync()).Items.Single().Local.Memo);
    }

    /// <summary>同じ大きさのまま、同じ時刻の刻みの中で書き直しても、自分の書き込みなら取り違えない。</summary>
    [Fact]
    public async Task SameSizeRewritesByTheAppAreNotMissed()
    {
        await SaveAsync("1", memo: "あ");
        await _store.Items.LoadAllAsync();

        for (var round = 0; round < 20; round++)
        {
            var memo = round % 2 == 0 ? "い" : "う";
            await _store.Items.ChangeLocalAsync("1", local => local with { Memo = memo }, [LocalField.Memo]);
            Assert.Equal(memo, (await _store.Items.LoadAllAsync()).Items.Single().Local.Memo);
        }
    }

    [Fact]
    public async Task HandEditsAreReadAgain()
    {
        await SaveAsync("1", memo: "前");
        await _store.Items.LoadAllAsync();

        EditByHand("1", json => json.Replace("\"前\"", "\"手で直した\""));

        Assert.Equal("手で直した", (await _store.Items.LoadAllAsync()).Items.Single().Local.Memo);
        Assert.Equal("手で直した", (await _store.Items.LoadAsync("1"))!.Local.Memo);
    }

    /// <summary>壊れたファイルは、前に読めた写しで隠さず「読めなかった」に出す。</summary>
    [Fact]
    public async Task ABrokenFileIsReportedNotHiddenByTheOldCopy()
    {
        await SaveAsync("1");
        await _store.Items.LoadAllAsync();

        EditByHand("1", _ => "{ 壊れた");

        var loaded = await _store.Items.LoadAllAsync();
        Assert.Empty(loaded.Items);
        Assert.Equal(["1"], loaded.FailedItemIds);
    }

    [Fact]
    public async Task DeletedItemsDisappear()
    {
        await SaveAsync("1");
        await SaveAsync("2");
        await _store.Items.LoadAllAsync();

        await _store.Items.DeleteAsync("1");
        File.Delete(_paths.ItemFile("2")); // 手で消した

        Assert.Empty((await _store.Items.LoadAllAsync()).Items);
        Assert.Null(await _store.Items.LoadAsync("1"));
        Assert.Null(await _store.Items.LoadAsync("2"));
    }

    /// <summary>消した後に同じIDで作り直した物は、新しい方を返す。</summary>
    [Fact]
    public async Task RecreatedItemsAreFresh()
    {
        await SaveAsync("1", memo: "古い");
        await _store.Items.LoadAllAsync();
        await _store.Items.DeleteAsync("1");

        await SaveAsync("1", memo: "新しい");

        Assert.Equal("新しい", (await _store.Items.LoadAllAsync()).Items.Single().Local.Memo);
    }

    /// <summary>読み込みと書き込みが重なっても、書き終えた後の読み込みが古い中身を返さない。</summary>
    [Fact]
    public async Task ConcurrentReadsDoNotPinAnOldCopy()
    {
        await SaveAsync("1", memo: "0");

        var reading = Task.Run(async () =>
        {
            for (var index = 0; index < 50; index++)
            {
                await _store.Items.LoadAllAsync();
            }
        });

        for (var index = 1; index <= 30; index++)
        {
            var memo = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await _store.Items.ChangeLocalAsync("1", local => local with { Memo = memo }, [LocalField.Memo]);
        }

        await reading;

        Assert.Equal("30", (await _store.Items.LoadAllAsync()).Items.Single().Local.Memo);
        Assert.Equal("30", (await _store.Items.LoadAsync("1"))!.Local.Memo);
    }

    /// <summary>ファイル名と中のIDのずれ（L6）は、全件の読み込みと同じ写しから引ける。</summary>
    [Fact]
    public async Task FindsFilesWhoseInnerIdDiffers()
    {
        await SaveAsync("1");
        await SaveAsync("2");
        EditByHand("2", json => json.Replace("\"id\": \"2\"", "\"id\": \"3\""));

        var misnamed = await _store.Items.FindMisnamedAsync();

        Assert.Equal([("2", "3")], misnamed);
    }

    /// <summary>登録簿は、変わっていない間は読んだ物を共有する。書いた後・手で直した後は読み直す。</summary>
    [Fact]
    public async Task RegistryIsSharedUntilItChanges()
    {
        await _store.Avatars.UpdateAsync(_ => new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = "10", BoothName = "前" }],
        });

        var first = _store.Avatars.Load();
        Assert.Same(first, _store.Avatars.Load());

        await _store.Avatars.UpdateAsync(current => new AvatarRegistry
        {
            Entries = [current.Entries[0] with { BoothName = "後" }],
        });
        Assert.Equal("後", _store.Avatars.Load().Entries[0].BoothName);

        var path = _paths.AvatarRegistryFile;
        var before = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"後\"", "\"手\""));
        File.SetLastWriteTimeUtc(path, before.AddSeconds(5));
        Assert.Equal("手", _store.Avatars.Load().Entries[0].BoothName);
    }

    /// <summary>足し引きして書き戻す入れ物（一覧）は共有しない。呼んだ所ごとに別の物を返す。</summary>
    [Fact]
    public async Task ListStoresAreNeverShared()
    {
        await _store.Notifications.SaveAsync([]);

        var first = _store.Notifications.Load();
        first.Add(new NotificationRecord { Id = "x", Kind = NotificationKind.ItemUpdated, Title = "t", Detail = "d", CreatedAt = DateTimeOffset.Now });

        Assert.Empty(_store.Notifications.Load());
    }
}
