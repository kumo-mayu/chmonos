using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

public class EditServiceTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _store;
    private readonly EditService _service;

    public EditServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-edit-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _service = new EditService(_store);
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

    private async Task<ItemRecord> SaveItemAsync(string id, BoothBlock? booth = null)
    {
        var record = new ItemRecord
        {
            Id = id,
            Booth = booth ?? new BoothBlock { Name = "テスト商品", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { Memo = "元のメモ" },
        };

        await _store.Items.SaveAsync(record);
        return record;
    }

    [Fact]
    public async Task AddsTopLevelTag()
    {
        var master = await _service.AddUserTagAsync("アバター", null);

        Assert.Single(master.Tops);
        Assert.Equal("アバター", master.Tops[0].Name);
    }

    /// <summary>同じ名前が2つ並ぶと、item側の名前参照がどちらを指すか決まらなくなる。</summary>
    [Fact]
    public async Task DoesNotAddTheSameTopTwice()
    {
        await _service.AddUserTagAsync("アバター", null);
        var master = await _service.AddUserTagAsync("アバター", null);

        Assert.Single(master.Tops);
    }

    [Fact]
    public async Task AddsSubUnderExistingTop()
    {
        await _service.AddUserTagAsync("衣装", null);
        var master = await _service.AddUserTagAsync("衣装", "トップス");

        Assert.Single(master.Tops);
        Assert.Single(master.Tops[0].Subs);
        Assert.Equal("トップス", master.Tops[0].Subs[0].Name);
    }

    /// <summary>トップがまだ無くてもサブを足せる。作りながら分類を組み立てられるようにする。</summary>
    [Fact]
    public async Task CreatesTopWhenAddingSubToUnknownTop()
    {
        var master = await _service.AddUserTagAsync("ギミック", "音");

        Assert.Equal("ギミック", master.Tops[0].Name);
        Assert.Equal("音", master.Tops[0].Subs[0].Name);
    }

    [Fact]
    public async Task DoesNotAddTheSameSubTwice()
    {
        await _service.AddUserTagAsync("衣装", "トップス");
        var master = await _service.AddUserTagAsync("衣装", "トップス");

        Assert.Single(master.Tops[0].Subs);
    }

    [Fact]
    public async Task IgnoresBlankTagNames()
    {
        var master = await _service.AddUserTagAsync("   ", null);

        Assert.Empty(master.Tops);
    }

    [Fact]
    public async Task AddsAttributeAndSkipsDuplicates()
    {
        await _service.AddAttributeAsync("かわいい");
        var master = await _service.AddAttributeAsync("かわいい");

        Assert.Single(master.Attributes);
        Assert.Equal("かわいい", master.Attributes[0].Name);
    }

    /// <summary>
    /// 保存は local だけを差し替える。booth 側はそのまま残す
    /// （再取得と編集の担当範囲を分けている前提が、ここでも崩れないこと）。
    /// </summary>
    [Fact]
    public async Task SavesLocalWithoutTouchingBoothBlock()
    {
        await SaveItemAsync("123");

        var saved = await _service.SaveLocalAsync(
            "123",
            new LocalBlock
            {
                UserTags = [new UserTagAssignment { Top = "アバター", Subs = ["女性"] }],
                Attributes = new Dictionary<string, int> { ["かわいい"] = 70 },
                Memo = "新しいメモ",
            },
            LocalOwners.EditScreen);

        Assert.True(saved);

        var reloaded = await _store.Items.LoadAsync("123");
        Assert.Equal("テスト商品", reloaded!.Booth.Name);
        Assert.Equal("アバター", reloaded.Local.UserTags[0].Top);
        Assert.Equal(70, reloaded.Local.Attributes["かわいい"]);
        Assert.Equal("新しいメモ", reloaded.Local.Memo);
    }

    /// <summary>キューを積んだ後にitemが消えていることがある。</summary>
    [Fact]
    public async Task ReportsFailureWhenItemIsMissing()
    {
        Assert.False(await _service.SaveLocalAsync("does-not-exist", new LocalBlock(), LocalOwners.EditScreen));
    }

    /// <summary>
    /// 取り込み中に商品を消せると決めたので、書く直前に消えていることがある。
    /// そのとき**作り直してはいけない**。消したのに戻ってくることになる。
    /// </summary>
    [Fact]
    public async Task DoesNotRecreateAnItemDeletedBeforeTheSave()
    {
        await SaveItemAsync("123");
        File.Delete(Path.Combine(_root, "items", "123.json"));

        Assert.False(await _service.SaveLocalAsync(
            "123",
            new LocalBlock { Memo = "書いてはいけない" },
            LocalOwners.EditScreen));

        Assert.False(_store.Items.Exists("123"));
    }

    /// <summary>
    /// **これがA1の本体。**
    ///
    /// 編集画面は開いた時点の写しを抱えている。開いている間に検出が対応アバターを入れ、
    /// 取り込みがファイルを足した後で保存すると、丸ごと書き戻す作りでは両方が消えていた。
    /// </summary>
    [Fact]
    public async Task KeepsFieldsWrittenByOthersWhileTheScreenWasOpen()
    {
        var opened = await SaveItemAsync("123");

        // 画面が開いている間に、検出と取り込みが書いた
        await _store.Items.SaveLocalAsync(
            "123",
            opened.Local with
            {
                Avatars = [new AvatarLink { AvatarItemId = "999", Name = "くうた" }],
                AvatarsDetectedAt = DateTimeOffset.Now,
            },
            LocalOwners.Detection);

        await _store.Items.SaveLocalAsync(
            "123",
            opened.Local with
            {
                LocalFiles = [new LocalFileRecord { Hash = "abc", SizeBytes = 100, Paths = ["D:/a.zip"] }],
            },
            LocalOwners.Import);

        // 画面は古い写し（Avatars も LocalFiles も空）のまま保存する
        await _service.SaveLocalAsync(
            "123",
            opened.Local with { Memo = "新しいメモ" },
            LocalOwners.EditScreen);

        var reloaded = await _store.Items.LoadAsync("123");

        Assert.Equal("新しいメモ", reloaded!.Local.Memo);
        Assert.Single(reloaded.Local.Avatars);
        Assert.Equal("くうた", reloaded.Local.Avatars[0].Name);
        Assert.Single(reloaded.Local.LocalFiles);
        Assert.NotNull(reloaded.Local.AvatarsDetectedAt);
    }

    /// <summary>名指ししなかった項目は、いくら詰めて渡しても書かれない。</summary>
    [Fact]
    public async Task IgnoresFieldsTheSaveDoesNotOwn()
    {
        await SaveItemAsync("123");

        await _service.SaveLocalAsync(
            "123",
            new LocalBlock
            {
                Memo = "編集画面が持つ",
                Avatars = [new AvatarLink { AvatarItemId = "999", Name = "勝手に書いた" }],
                LocalFiles = [new LocalFileRecord { Hash = "x", SizeBytes = 1, Paths = ["D:/x.zip"] }],
                IsDelisted = true,
            },
            LocalOwners.EditScreen);

        var reloaded = await _store.Items.LoadAsync("123");

        Assert.Equal("編集画面が持つ", reloaded!.Local.Memo);
        Assert.Empty(reloaded.Local.Avatars);
        Assert.Empty(reloaded.Local.LocalFiles);
        Assert.False(reloaded.Local.IsDelisted);
    }

    /// <summary>
    /// <c>ExistsOnBooth</c> は導ける値なので、どの経路から保存しても入れ直す。
    /// 画面が古い写しを持ち回っても、保存の時点で正しくなる。
    /// </summary>
    [Fact]
    public async Task RecomputesWhetherEachPurchasedVariationStillExists()
    {
        await SaveItemAsync("123", new BoothBlock
        {
            Name = "テスト商品",
            FetchedAt = DateTimeOffset.Now,
            Variations = [new BoothVariation { Id = 1, Name = "本体" }],
        });

        await _service.SaveLocalAsync(
            "123",
            new LocalBlock
            {
                Purchases =
                [
                    // 手元の写しでは両方「まだある」ことになっている
                    new Purchase { VariationId = 1, Price = 1000, ExistsOnBooth = true },
                    new Purchase { VariationId = 2, Price = 500, ExistsOnBooth = true },
                ],
            },
            LocalOwners.EditScreen);

        var reloaded = await _store.Items.LoadAsync("123");

        Assert.True(reloaded!.Local.Purchases[0].ExistsOnBooth);
        Assert.False(reloaded.Local.Purchases[1].ExistsOnBooth);

        // 消えた記録そのものは残す。実際に払っているため
        Assert.Equal(2, reloaded.Local.Purchases.Count);
        Assert.Equal(500, reloaded.Local.Purchases[1].Price);
    }

    [Fact]
    public async Task RemembersQueuePosition()
    {
        await _service.StartSessionAsync(["1", "2", "3"]);
        await _service.AdvanceSessionAsync(2);

        var session = _store.EditSession.Load();
        Assert.Equal(["1", "2", "3"], session.ItemIds);
        Assert.Equal(2, session.Index);
        Assert.False(session.IsFinished);
    }

    [Fact]
    public async Task MarksSessionFinishedAtTheEnd()
    {
        await _service.StartSessionAsync(["1", "2"]);
        await _service.AdvanceSessionAsync(2);

        Assert.True(_store.EditSession.Load().IsFinished);
    }

    [Fact]
    public async Task ClearsSession()
    {
        await _service.StartSessionAsync(["1", "2"]);
        await _service.ClearSessionAsync();

        Assert.Empty(_store.EditSession.Load().ItemIds);
    }

    /// <summary>
    /// 保存した商品は続きの記録に残る。編集画面の上の帯で、保存した物と飛ばした物を見分けるため。
    /// 同じ商品を2回保存しても1件。積み直すと空に戻る。
    /// </summary>
    [Fact]
    public async Task RemembersWhichItemsWereSaved()
    {
        await _service.StartSessionAsync(["1", "2", "3"]);
        await _service.NoteSavedAsync("1");
        await _service.NoteSavedAsync("3");
        await _service.NoteSavedAsync("1");
        await _service.AdvanceSessionAsync(2);

        var session = _store.EditSession.Load();
        Assert.Equal(["1", "3"], session.SavedItemIds);
        Assert.Equal(2, session.Index);

        await _service.StartSessionAsync(["4"]);
        Assert.Empty(_store.EditSession.Load().SavedItemIds);
    }

    /// <summary>
    /// IDの付け替えも錠の中で今の記録に当てる。ここだけ錠の外で読んで書いていたので、
    /// 「保存して次へ」と重なると、進めた位置か付け替えのどちらかが消えていた。
    /// </summary>
    [Fact]
    public async Task ReplacingAnItemIdKeepsTheIndexWrittenMeanwhile()
    {
        await _service.StartSessionAsync(["1", "local-aaaa1111", "3"]);
        await _service.NoteSavedAsync("local-aaaa1111");

        // 位置を書いている最中（錠を持ったまま）に付け替えが来る
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var advance = Task.Run(() => _store.EditSession.UpdateAsync(session =>
        {
            entered.Set();
            release.Wait();
            return session with { Index = 2 };
        }));
        entered.Wait();

        var replace = _service.ReplaceItemIdAsync("local-aaaa1111", "222");
        await Task.WhenAny(replace, Task.Delay(500));
        release.Set();
        await Task.WhenAll(advance, replace);

        var saved = _store.EditSession.Load();
        Assert.Equal(["1", "222", "3"], saved.ItemIds);
        Assert.Equal(["222"], saved.SavedItemIds);
        Assert.Equal(2, saved.Index);
        Assert.Equal(saved.ItemIds, (await replace).ItemIds);
    }

    /// <summary>
    /// 積み直しも錠の中で書く。錠の外で丸ごと書いていたので、位置・保存した印・IDの付け替えが
    /// 古い記録を読んだ後に積み直すと、その書き手が後から古い順番を書き戻し、積み直した順番が消えていた。
    /// </summary>
    [Fact]
    public async Task 位置を書いている途中に積み直しても_積み直した順番が残る()
    {
        await _service.StartSessionAsync(["1", "2", "3"]);

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var advance = Task.Run(() => _store.EditSession.UpdateAsync(session =>
        {
            entered.Set();
            release.Wait();
            return session with { Index = 2 };
        }));
        entered.Wait();

        var start = _service.StartSessionAsync(["7", "8"]);
        await Task.WhenAny(start, Task.Delay(300));
        release.Set();
        await Task.WhenAll(advance, start);

        var saved = _store.EditSession.Load();
        Assert.Equal(["7", "8"], saved.ItemIds);
        Assert.Equal(0, saved.Index);
    }

    /// <summary>
    /// 終えて消すのも錠の中で書く。錠の外で消していたので、古い記録を読んだ書き手が後から書き戻し、
    /// 終えたはずの順番が次に入ったときに続きとして出ていた。
    /// </summary>
    [Fact]
    public async Task 保存した印を書いている途中に終えても_順番は消えたまま()
    {
        await _service.StartSessionAsync(["1", "2"]);

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var note = Task.Run(() => _store.EditSession.UpdateAsync(session =>
        {
            entered.Set();
            release.Wait();
            return session with { SavedItemIds = ["2"], Index = 2 };
        }));
        entered.Wait();

        var clear = _service.ClearSessionAsync();
        await Task.WhenAny(clear, Task.Delay(300));
        release.Set();
        await Task.WhenAll(note, clear);

        var saved = _store.EditSession.Load();
        Assert.Empty(saved.ItemIds);
        Assert.Empty(saved.SavedItemIds);
    }

    /// <summary>
    /// 入り直して順番を詰めたときは、順番と位置を1回で書く。2回に分けていたので、
    /// 間で落ちると位置が先頭に戻り、間に入った書き手からは位置の無い順番が見えた。
    /// </summary>
    [Fact]
    public async Task 積み直すときに位置も一緒に書ける()
    {
        await _service.StartSessionAsync(["1", "2", "3"]);
        await _service.NoteSavedAsync("1");

        var written = await _service.StartSessionAsync(["2", "3"], index: 1);

        var saved = _store.EditSession.Load();
        Assert.Equal(["2", "3"], saved.ItemIds);
        Assert.Equal(1, saved.Index);
        Assert.Empty(saved.SavedItemIds);
        Assert.Equal(saved.ItemIds, written.ItemIds);
        Assert.Equal(1, written.Index);
    }

    /// <summary>順番に無いIDなら書かずに今の記録を返す。</summary>
    [Fact]
    public async Task ReplacingAnIdThatIsNotQueuedWritesNothing()
    {
        await _service.StartSessionAsync(["1"]);

        var session = await _service.ReplaceItemIdAsync("9", "10");

        Assert.Equal(["1"], session.ItemIds);
    }
}
