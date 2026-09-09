using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// ⑦ 期限の来た商品を取り直す。
///
/// 設定（7日±3日）もデータ（<c>NextFetchDueAt</c>）も前からあったのに、
/// **走らせる部分だけが無かった。**設定が嘘をついている状態だった。
/// </summary>
public class DueRefreshTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _store;
    private readonly FakeItems _items = new();
    private readonly DueRefresh _due;

    public DueRefreshTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-due-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _due = new DueRefresh(_store, _items);
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

        GC.SuppressFinalize(this);
    }

    /// <summary>取り直しの呼ばれた順を覚えるだけ。中身は ItemRefreshTests で見ている。</summary>
    private sealed class FakeItems : IItemService
    {
        public List<string> Refreshed { get; } = [];

        public Task<RefreshOutcome> RefreshAsync(string itemId, CancellationToken cancellationToken = default)
        {
            Refreshed.Add(itemId);
            return Task.FromResult(RefreshOutcome.Updated);
        }

        public Task<bool> RegisterItemAsync(string itemId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<int> FetchImagesAsync(string itemId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("⑦は画像を落とさないはず");

        public Task<ItemPreview?> PreviewAsync(string itemId, CancellationToken cancellationToken = default)
            => Task.FromResult<ItemPreview?>(null);

        public Task<(ItemPreview? Preview, string? Error)> PreviewWithReasonAsync(string itemId, CancellationToken cancellationToken = default)
            => Task.FromResult<(ItemPreview?, string?)>((null, null));

        public Task<int> ReconcileUnresolvedAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<bool> RegisterFolderAsync(string itemId, string folderPath, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> UnregisterFolderAsync(string itemId, string folderPath, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> AssignItemIdAsync(string hash, string itemId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<DetachOutcome> DetachFileAsync(
            string itemId, string hash, bool deleteItemWhenEmpty, CancellationToken cancellationToken = default)
            => Task.FromResult(DetachOutcome.Detached);

        public Task ExcludeAsync(string hash, IReadOnlyList<string> paths, string? reason, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private Task SaveAsync(string id, DateTimeOffset? due)
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = $"商品 {id}", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { NextFetchDueAt = due },
        });

    /// <summary>期限が来たものだけ。まだのものは触らない。</summary>
    [Fact]
    public async Task PicksOnlyTheItemsWhoseDueDateHasPassed()
    {
        var now = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);

        await SaveAsync("past", now.AddDays(-1));
        await SaveAsync("future", now.AddDays(1));

        Assert.Equal(["past"], await _due.FindDueAsync(now));
    }

    /// <summary>期限の古い順。長く放ってあるものから片付ける。</summary>
    [Fact]
    public async Task TakesTheOldestDueDateFirst()
    {
        var now = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);

        await SaveAsync("newer", now.AddDays(-1));
        await SaveAsync("oldest", now.AddDays(-30));
        await SaveAsync("middle", now.AddDays(-7));

        Assert.Equal(["oldest", "middle", "newer"], await _due.FindDueAsync(now));
    }

    /// <summary>予定日を持たないものは対象にしない（古い形のデータだけがそうなる）。</summary>
    [Fact]
    public async Task IgnoresItemsWithNoDueDateAtAll()
    {
        await SaveAsync("nodue", null);

        Assert.Empty(await _due.FindDueAsync(DateTimeOffset.Now));
    }

    /// <summary>
    /// **⑦は画像を落とさない。**梯子の規則をここだけ破らないため。
    /// 落とそうとすれば FakeItems が例外を投げる。
    /// </summary>
    [Fact]
    public async Task RefreshesTheDueItemsWithoutTouchingImages()
    {
        var now = DateTimeOffset.Now;

        await SaveAsync("a", now.AddDays(-2));
        await SaveAsync("b", now.AddDays(-9));

        Assert.Equal(2, await _due.RunAsync());
        Assert.Equal(["b", "a"], _items.Refreshed);
    }

    /// <summary>期限の来たものが無ければ、何も呼ばない。</summary>
    [Fact]
    public async Task DoesNothingWhenNothingIsDue()
    {
        await SaveAsync("future", DateTimeOffset.Now.AddDays(5));

        Assert.Equal(0, await _due.RunAsync());
        Assert.Empty(_items.Refreshed);
    }
}
