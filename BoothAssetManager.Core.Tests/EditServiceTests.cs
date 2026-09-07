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

    private async Task<ItemRecord> SaveItemAsync(string id)
    {
        var record = new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = "テスト商品", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { Memo = "元のメモ" },
        };

        await _store.Items.SaveAsync(record);
        return record;
    }

    [Fact]
    public async Task AddsTopLevelTag()
    {
        var master = await _service.AddAppTagAsync("アバター", null);

        Assert.Single(master.Tops);
        Assert.Equal("アバター", master.Tops[0].Name);
    }

    /// <summary>同じ名前が2つ並ぶと、item側の名前参照がどちらを指すか決まらなくなる。</summary>
    [Fact]
    public async Task DoesNotAddTheSameTopTwice()
    {
        await _service.AddAppTagAsync("アバター", null);
        var master = await _service.AddAppTagAsync("アバター", null);

        Assert.Single(master.Tops);
    }

    [Fact]
    public async Task AddsSubUnderExistingTop()
    {
        await _service.AddAppTagAsync("衣装", null);
        var master = await _service.AddAppTagAsync("衣装", "トップス");

        Assert.Single(master.Tops);
        Assert.Single(master.Tops[0].Subs);
        Assert.Equal("トップス", master.Tops[0].Subs[0].Name);
    }

    /// <summary>トップがまだ無くてもサブを足せる。作りながら分類を組み立てられるようにする。</summary>
    [Fact]
    public async Task CreatesTopWhenAddingSubToUnknownTop()
    {
        var master = await _service.AddAppTagAsync("ギミック", "音");

        Assert.Equal("ギミック", master.Tops[0].Name);
        Assert.Equal("音", master.Tops[0].Subs[0].Name);
    }

    [Fact]
    public async Task DoesNotAddTheSameSubTwice()
    {
        await _service.AddAppTagAsync("衣装", "トップス");
        var master = await _service.AddAppTagAsync("衣装", "トップス");

        Assert.Single(master.Tops[0].Subs);
    }

    [Fact]
    public async Task IgnoresBlankTagNames()
    {
        var master = await _service.AddAppTagAsync("   ", null);

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

        var saved = await _service.SaveLocalAsync("123", new LocalBlock
        {
            AppTags = [new AppTagAssignment { Top = "アバター", Subs = ["女性"] }],
            Attributes = new Dictionary<string, int> { ["かわいい"] = 70 },
            Memo = "新しいメモ",
        });

        Assert.True(saved);

        var reloaded = await _store.Items.LoadAsync("123");
        Assert.Equal("テスト商品", reloaded!.Booth.Name);
        Assert.Equal("アバター", reloaded.Local.AppTags[0].Top);
        Assert.Equal(70, reloaded.Local.Attributes["かわいい"]);
        Assert.Equal("新しいメモ", reloaded.Local.Memo);
    }

    /// <summary>キューを積んだ後にitemが消えていることがある。</summary>
    [Fact]
    public async Task ReportsFailureWhenItemIsMissing()
    {
        Assert.False(await _service.SaveLocalAsync("does-not-exist", new LocalBlock()));
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
