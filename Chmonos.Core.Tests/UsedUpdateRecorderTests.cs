using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests;

/// <summary>
/// 取り直しで中身の更新を見つけたら、検出し直してから見分けて控えを書く（<c>docs/spec/used-updates.md</c>）。
/// 名前・ID は作り物。
/// </summary>
public sealed class UsedUpdateRecorderTests : IDisposable
{
    private const string ItemId = "9907101";
    private const string AvatarId = "9907102";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-usedupdate-" + Guid.NewGuid().ToString("N"));
    private readonly DataStore _store;
    private int _detections;

    /// <summary>検出の代わり。呼ばれたら、商品に作り物のアバター「アオイ」を足す（取り直しで足された対応を拾った、という状況）。</summary>
    private Func<CancellationToken, Task> Detect => async token =>
    {
        _detections++;
        await _store.Items.ChangeLocalAsync(
            ItemId,
            local => local with { Avatars = [new AvatarLink { AvatarItemId = AvatarId, Name = "アオイ" }] },
            LocalOwners.Detection,
            token);
    };

    public UsedUpdateRecorderTests()
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

    private static BoothBlock Booth(string history) => new()
    {
        FetchedAt = DateTimeOffset.Now,
        Name = "作り物の衣装",
        H2Sections = [new H2Section { Heading = "更新履歴", Text = history }],
    };

    private async Task<ItemRecord> SaveAsync(bool notify = true)
    {
        var item = new ItemRecord
        {
            Id = ItemId,
            Booth = Booth("2026/10/01 v1.0 公開"),
            Local = new LocalBlock
            {
                NotifyOnUpdate = notify,
                LocalFiles = [new LocalFileRecord { Hash = "OLD", Paths = [@"D:\fake\a.zip"], SizeBytes = 1 }],
            },
        };
        await _store.Items.SaveAsync(item);
        return item;
    }

    private async Task<ContentUpdate?> NoteAsync(ItemRecord before, string history)
    {
        await new UsedUpdateRecorder(_store, Detect).NoteAsync(before, Booth(history), CancellationToken.None);
        return (await _store.Items.LoadAsync(ItemId))!.Local.ContentUpdate;
    }

    [Fact]
    public async Task 修正の行が足されたら_その時のファイルを控える()
    {
        var before = await SaveAsync();

        var update = await NoteAsync(before, "2026/10/01 v1.0 公開\n2026/11/01 v1.1 スカートの破綻を修正");

        Assert.NotNull(update);
        Assert.Equal(["OLD"], update.FilesBefore);
        Assert.Equal(1, _detections);
    }

    [Fact]
    public async Task 検出し直して入ったアバターの名前だけを足した更新なら促さない()
    {
        var before = await SaveAsync();

        // 「・アオイ」は型に当たらない。検出し直した後の対応アバターの名前で、追加と見分ける
        var update = await NoteAsync(before, "2026/10/01 v1.0 公開\n2026/11/01\n・アオイ");

        Assert.Null(update);
        Assert.Equal(1, _detections);
    }

    [Fact]
    public async Task 更新を知らせるを切っていても控える()
    {
        var before = await SaveAsync(notify: false);

        Assert.NotNull(await NoteAsync(before, "2026/10/01 v1.0 公開\n2026/11/01 v1.1 不具合を修正"));
    }

    [Fact]
    public async Task 更新履歴が動いていなければ検出もしない()
    {
        var before = await SaveAsync();
        var after = Booth("2026/10/01 v1.0 公開") with { Name = "作り物の衣装（改名）" };

        await new UsedUpdateRecorder(_store, Detect).NoteAsync(before, after, CancellationToken.None);

        Assert.Null((await _store.Items.LoadAsync(ItemId))!.Local.ContentUpdate);
        Assert.Equal(0, _detections);
    }
}
