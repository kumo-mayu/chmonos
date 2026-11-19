using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 統計の「空けられる場所」から、同じ中身のファイルの1か所をごみ箱へ送る（ユーザ判断 2026-11-20）。
/// 統計の集計は古い写しなので、送る直前に今の記録とディスクで、同じ中身のほかのファイルが残ると確かめ直す
/// </summary>
public sealed class DuplicateFileRemoverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-dupremove-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly DataStore _store;
    private readonly List<string> _deleted = [];

    public DuplicateFileRemoverTests()
    {
        var paths = new AppPaths(Path.Combine(_root, "store"));
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

    /// <summary>ごみ箱の代わり（本物のごみ箱へは送らない）。送ったことにして、記録だけ残す。</summary>
    private DuplicateFileRemover Remover() => new(
        _store,
        (path, _) =>
        {
            _deleted.Add(path);
            File.Delete(path);
            return Task.CompletedTask;
        },
        // ハッシュは中身の文字そのものを返す（本物の SHA-256 を計算しない）
        (path, _) => Task.FromResult(File.ReadAllText(path)));

    private string Make(string name, string content)
    {
        var path = Path.Combine(_root, "files", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private Task SaveAsync(string id, string hash, long size, params string[] paths)
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock(),
            Local = new LocalBlock { LocalFiles = [new LocalFileRecord { Hash = hash, Paths = paths, SizeBytes = size }] },
        });

    [Fact]
    public async Task 同じ中身のほかのファイルが在れば_送る()
    {
        var a = Make("a.zip", "same");
        var b = Make("b.zip", "same");
        await SaveAsync("9901001", "same", 4, a, b);

        var result = await Remover().RemoveAsync("same", a);

        Assert.True(result.Removed);
        Assert.Equal(4, result.FreedBytes);
        Assert.Equal([a], _deleted);
        Assert.True(File.Exists(b));
    }

    /// <summary>集計の後に、ほかの場所を手で消していた。最後の1つを送らない。</summary>
    [Fact]
    public async Task ほかの場所が今は無ければ_最後の1つなので送らない()
    {
        var a = Make("a.zip", "same");
        var gone = Path.Combine(_root, "files", "消した.zip");
        await SaveAsync("9901002", "same", 4, a, gone);

        var result = await Remover().RemoveAsync("same", a);

        Assert.False(result.Removed);
        Assert.Empty(_deleted);
        Assert.True(File.Exists(a));
    }

    /// <summary>ほかの場所が在っても、中身が変わっていれば同じ中身とは言えない。</summary>
    [Fact]
    public async Task ほかの場所の中身が記録と違えば_送らない()
    {
        var a = Make("a.zip", "same");
        var changed = Make("b.zip", "diff");
        await SaveAsync("9901003", "same", 4, a, changed);

        var result = await Remover().RemoveAsync("same", a);

        Assert.False(result.Removed);
        Assert.Empty(_deleted);
    }

    [Fact]
    public async Task 記録に無い場所は送らない()
    {
        var a = Make("a.zip", "same");
        var b = Make("b.zip", "same");
        var stranger = Make("c.zip", "same");
        await SaveAsync("9901004", "same", 4, a, b);

        var result = await Remover().RemoveAsync("same", stranger);

        Assert.False(result.Removed);
        Assert.Empty(_deleted);
    }

    /// <summary>1つの場所を2つの商品が持っていても、別の場所が残るなら送れる（記録は見回りが外す）。</summary>
    [Fact]
    public async Task 別の商品の記録にあるほかの場所も_残る場所に数える()
    {
        var a = Make("a.zip", "same");
        var b = Make("b.zip", "same");
        await SaveAsync("9901005", "same", 4, a);
        await SaveAsync("9901006", "same", 4, b);

        var result = await Remover().RemoveAsync("same", a);

        Assert.True(result.Removed);
        Assert.Equal([a], _deleted);
    }
}
