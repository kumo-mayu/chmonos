using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 登録したフォルダが見つからなくなったことを記録する（<see cref="LocalFolderRecord.MissingSince"/>・ユーザ判断 2026-10-04）。
/// 前は取り込みが無いフォルダを黙って飛ばすだけで、フォルダが消えても所持のまま、カードの印・検索の条件・統計のどれも数えなかった。
/// </summary>
public sealed class FolderMissingSinceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-folder-missing-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly string _watched;

    public FolderMissingSinceTests()
    {
        _watched = Path.Combine(_root, "watched");
        Directory.CreateDirectory(_watched);
        _paths = new AppPaths(Path.Combine(_root, "library"));
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

    private async Task<string> RegisterAsync(string path, DateTimeOffset? missingSince = null)
    {
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "local-1",
            Local = new LocalBlock
            {
                LocalFolders = [new LocalFolderRecord { Path = path, RegisteredAt = DateTimeOffset.UnixEpoch, MissingSince = missingSince }],
            },
        });
        return path;
    }

    private async Task ImportAsync()
    {
        var client = new OffUiThreadTests.OfflineClient();
        var settings = new AppSettings { SaveImages = false };
        var pipeline = new ImportPipeline(_store, client, new ImagePipeline(client, _paths, settings), settings);
        await pipeline.RunAsync(new ImportWorkSet([_watched]));
        Assert.Equal(0, client.Calls);
    }

    private async Task<ItemRecord> ItemAsync() => (await _store.Items.LoadAsync("local-1"))!;

    [Fact]
    public async Task 取り込みが無いと見たフォルダには_見つからなくなった日時が入り_印と条件に当たる()
    {
        var before = DateTimeOffset.Now;
        await RegisterAsync(Path.Combine(_root, "消したフォルダ"));

        await ImportAsync();

        var item = await ItemAsync();
        var folder = Assert.Single(item.Local.LocalFolders);
        Assert.NotNull(folder.MissingSince);
        Assert.InRange(folder.MissingSince!.Value, before, DateTimeOffset.Now);
        Assert.True(item.HasMissingFile);

        // JSON は人が読める形で、欄の名前で残る
        Assert.Contains("\"missingSince\"", await File.ReadAllTextAsync(_paths.ItemFile("local-1")));
    }

    [Fact]
    public async Task 無い間は_最初に無いと見た日時のまま()
    {
        var first = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        await RegisterAsync(Path.Combine(_root, "消したフォルダ"), first);

        await ImportAsync();

        Assert.Equal(first, Assert.Single((await ItemAsync()).Local.LocalFolders).MissingSince);
    }

    [Fact]
    public async Task また見つかったら_日時を消し_印と条件から外れる()
    {
        var path = Path.Combine(_root, "戻したフォルダ");
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, "a.psd"), [1, 2, 3]);
        await RegisterAsync(path, DateTimeOffset.UnixEpoch);

        await ImportAsync();

        var item = await ItemAsync();
        var folder = Assert.Single(item.Local.LocalFolders);
        Assert.Null(folder.MissingSince);
        Assert.Equal(1, folder.FileCount);
        Assert.False(item.HasMissingFile);
        Assert.DoesNotContain("missingSince", await File.ReadAllTextAsync(_paths.ItemFile("local-1")));
    }

    /// <summary>ドライブごと見えない（外付けを外している）ときは「無い」と書かない。ファイルの取り込みが外付けの上の場所を残すのと同じ。</summary>
    [Fact]
    public async Task 見に行けないドライブの上のフォルダには_書かない()
    {
        await RegisterAsync(Path.Combine(UnresolvedMergeTests.MissingVolumeFolder(), "登録したフォルダ"));

        await ImportAsync();

        var item = await ItemAsync();
        Assert.Null(Assert.Single(item.Local.LocalFolders).MissingSince);
        Assert.False(item.HasMissingFile);
    }

    /// <summary>統計の「見つからないファイル」の件数も同じ式（記録だけを見る）。</summary>
    [Fact]
    public void 統計の件数も_見つからなくなったフォルダを数える()
    {
        static ItemRecord WithFolder(string id, DateTimeOffset? missingSince) => new()
        {
            Id = id,
            Local = new LocalBlock { LocalFolders = [new LocalFolderRecord { Path = $@"D:\folders\{id}", MissingSince = missingSince }] },
        };

        var snapshot = StatsService.Build([WithFolder("1", DateTimeOffset.UnixEpoch), WithFolder("2", null)], new AvatarRegistry(), unresolvedCount: 0);

        Assert.Equal(1, snapshot.Backlog.MissingFileCount);
    }
}
