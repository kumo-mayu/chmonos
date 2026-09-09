using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

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
}
