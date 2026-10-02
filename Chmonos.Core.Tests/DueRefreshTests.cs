using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

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

        /// <summary>取り直しが投げる商品（想定外の応答・ディスクの失敗の代わり）。</summary>
        public HashSet<string> Throws { get; } = [];

        /// <summary>商品ごとの取り直しの結果。無ければ取れた（Updated）。</summary>
        public Dictionary<string, RefreshOutcome> Outcomes { get; } = [];

        public Task<RefreshOutcome> RefreshAsync(string itemId, CancellationToken cancellationToken = default)
        {
            Refreshed.Add(itemId);
            return Throws.Contains(itemId)
                ? throw new IOException("想定外")
                : Task.FromResult(Outcomes.GetValueOrDefault(itemId, RefreshOutcome.Updated));
        }

        public Task<Booth.BoothFetchStatus> RegisterItemAsync(string itemId, CancellationToken cancellationToken = default)
            => Task.FromResult(Booth.BoothFetchStatus.Success);

        public Task<int> FetchImagesAsync(string itemId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("⑦は画像を落とさないはず");

        public Task<ItemPreview?> PreviewAsync(string itemId, CancellationToken cancellationToken = default)
            => Task.FromResult<ItemPreview?>(null);

        public Task<(ItemPreview? Preview, string? Error, bool NotOnBooth)> PreviewWithReasonAsync(string itemId, CancellationToken cancellationToken = default)
            => Task.FromResult<(ItemPreview?, string?, bool)>((null, null, false));

        public Task<bool> AssignUnpublishedItemIdAsync(string hash, string itemId, string displayName, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<int> ReconcileUnresolvedAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<bool> RegisterFolderAsync(string itemId, string folderPath, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> UnregisterFolderAsync(string itemId, string folderPath, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<ArchiveSwapOutcome> SwapFolderForArchiveAsync(string itemId, string folderPath, CancellationToken cancellationToken = default)
            => Task.FromResult(new ArchiveSwapOutcome(ArchiveSwapResult.Registered, "x.zip"));

        public Task<bool> AssignItemIdAsync(string hash, string itemId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<string?> RegisterLocalItemAsync(
            IReadOnlyList<string> hashes,
            string displayName,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(LocalItemId.For(hashes[0]));

        public Task<string?> AddUserImageAsync(
            string itemId,
            byte[] bytes,
            string? caption = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("user-00000000.webp");

        public Task<bool> RemoveUserImageAsync(
            string itemId,
            string fileName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> MoveUserImageAsync(
            string itemId,
            string fileName,
            int delta,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> PinThumbnailAsync(
            string itemId,
            string? fileName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> SetImageRoleAsync(
            string itemId,
            string fileName,
            Models.ImageRole role,
            bool isUserAdded,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<ItemIdChangePlan?> PlanItemIdChangeAsync(
            string fromId,
            string toId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<ItemIdChangePlan?>(null);

        public Task<ItemIdChangeOutcome> ChangeItemIdAsync(
            string fromId,
            string toId,
            IReadOnlySet<int>? skippedPurchases = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ItemIdChangeOutcome.Moved);

        public Task<DetachOutcome> DetachFileAsync(
            string itemId, string hash, bool deleteItemWhenEmpty, CancellationToken cancellationToken = default)
            => Task.FromResult(DetachOutcome.Detached);

        public Task<ReattachOutcome> ReattachFileAsync(
            string itemId, string hash, CancellationToken cancellationToken = default)
            => Task.FromResult(ReattachOutcome.Reattached);

        public Task<bool> SetFileVariationsAsync(
            string itemId, IReadOnlyDictionary<string, long?> variationByHash, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task UndoExcludeAsync(IReadOnlyList<UnresolvedFile> files, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ExcludeAsync(IReadOnlyList<UnresolvedFile> files, string? reason, CancellationToken cancellationToken = default)
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

    /// <summary>
    /// **1件が投げても残りは取り直す。**前は1件で残りが全部止まり、期限の古い順なので
    /// 次の起動でも同じ商品が先頭に来て、毎回そこで止まっていた。
    /// </summary>
    [Fact]
    public async Task KeepsGoingWhenOneItemFails()
    {
        var now = DateTimeOffset.Now;

        await SaveAsync("broken", now.AddDays(-9));
        await SaveAsync("fine", now.AddDays(-2));
        _items.Throws.Add("broken");

        Assert.Equal(1, await _due.RunAsync());
        Assert.Equal(["broken", "fine"], _items.Refreshed);
    }

    /// <summary>期限の来たものが無ければ、何も呼ばない。</summary>
    [Fact]
    public async Task DoesNothingWhenNothingIsDue()
    {
        await SaveAsync("future", DateTimeOffset.Now.AddDays(5));

        Assert.Equal(0, await _due.RunAsync());
        Assert.Empty(_items.Refreshed);
    }

    /// <summary>
    /// 届かない失敗（応答が無い・5xx）が3件続いたら、この回の残りは取り直さない（ユーザ判断 2026-09-29）。
    /// 予定日は動かさない決まりなので、残りは次の起動でまた来る。
    /// </summary>
    [Theory]
    [InlineData(RefreshOutcome.Unreachable)]
    [InlineData(RefreshOutcome.ServerError)]
    public async Task StopsAfterThreeFailuresThatNeverReachedBooth(RefreshOutcome failure)
    {
        var past = DateTimeOffset.Now.AddDays(-10);
        for (var index = 1; index <= 5; index++)
        {
            await SaveAsync($"due{index}", past.AddMinutes(index));
            _items.Outcomes[$"due{index}"] = failure;
        }

        Assert.Equal(0, await _due.RunAsync());
        Assert.Equal(["due1", "due2", "due3"], _items.Refreshed);
    }

    /// <summary>
    /// 途中で取れた・429（こちらの出し過ぎ）なら数え直す。問い合わせていない結果（手元に無い）は数えも数え直しもしない。
    /// </summary>
    [Fact]
    public async Task CountsAgainAfterAnAnswerFromBooth()
    {
        var past = DateTimeOffset.Now.AddDays(-10);
        RefreshOutcome[] outcomes =
        [
            RefreshOutcome.ServerError,
            RefreshOutcome.Unreachable,
            RefreshOutcome.TemporaryFailure,
            RefreshOutcome.ServerError,
            RefreshOutcome.Missing,
            RefreshOutcome.ServerError,
            RefreshOutcome.Updated,
            RefreshOutcome.ServerError,
        ];
        for (var index = 0; index < outcomes.Length; index++)
        {
            await SaveAsync($"due{index}", past.AddMinutes(index));
            _items.Outcomes[$"due{index}"] = outcomes[index];
        }

        await _due.RunAsync();

        Assert.Equal(outcomes.Length, _items.Refreshed.Count);
    }

    /// <summary>手元に無い結果は数え直しにもならない：失敗2件・手元に無い・失敗1件で打ち切る。</summary>
    [Fact]
    public async Task OutcomesWithoutAskingDoNotBreakTheStreak()
    {
        var past = DateTimeOffset.Now.AddDays(-10);
        RefreshOutcome[] outcomes =
        [
            RefreshOutcome.Unreachable,
            RefreshOutcome.Unreachable,
            RefreshOutcome.Missing,
            RefreshOutcome.Unreachable,
            RefreshOutcome.Updated,
        ];
        for (var index = 0; index < outcomes.Length; index++)
        {
            await SaveAsync($"due{index}", past.AddMinutes(index));
            _items.Outcomes[$"due{index}"] = outcomes[index];
        }

        await _due.RunAsync();

        Assert.Equal(4, _items.Refreshed.Count);
    }
}
