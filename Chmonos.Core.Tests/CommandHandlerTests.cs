using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

public class CommandHandlerTests
{
    private sealed class FakeImportPipeline : IImportPipeline
    {
        public IReadOnlyList<string>? ReceivedFolders { get; private set; }

        public Task<ImportSummary> RunAsync(
            IReadOnlyList<string> folders,
            IProgress<ImportProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => RunAsync(new ImportWorkSet(folders), progress, cancellationToken);

        public Task<ImportSummary> RunAsync(
            ImportWorkSet work,
            IProgress<ImportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ReceivedFolders = work.TakePending();
            return Task.FromResult(new ImportSummary { FilesScanned = 3, ItemsAdded = 2 });
        }
    }

    private sealed class FakeItemService : IItemService
    {
        public Task<bool> UnregisterFolderAsync(string itemId, string folderPath, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<FolderRelocation> RelocateFolderAsync(string itemId, string fromPath, string toPath, CancellationToken cancellationToken = default) => Task.FromResult(FolderRelocation.Moved);

        public Task<ArchiveSwapOutcome> SwapFolderForArchiveAsync(string itemId, string folderPath, bool liftExclusion = false, bool takeFromOtherItems = false, CancellationToken cancellationToken = default)
            => Task.FromResult(new ArchiveSwapOutcome(ArchiveSwapResult.Registered, "x.zip"));

        public Task<DetachOutcome> DetachFileAsync(string itemId, string hash, bool deleteItemWhenEmpty, CancellationToken cancellationToken = default) => Task.FromResult(DetachOutcome.Detached);

        public Task<ReattachOutcome> ReattachFileAsync(string itemId, string hash, CancellationToken cancellationToken = default) => Task.FromResult(ReattachOutcome.Reattached);

        public Task<bool> ForgetOldVersionAsync(string itemId, string hash, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> SetFileVariationsAsync(string itemId, IReadOnlyDictionary<string, long?> variationByHash, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> NoteFilePresenceAsync(string itemId, IReadOnlyCollection<FileSighting> sightings, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> RegisterFolderAsync(string itemId, string folderPath, CancellationToken cancellationToken = default, IProgress<int>? requestsLeft = null) => Task.FromResult(true);

        public Task<int> FetchImagesAsync(string itemId, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<Chmonos.Core.Booth.BoothFetchStatus> RegisterItemAsync(string itemId, CancellationToken cancellationToken = default)
            => Task.FromResult(Chmonos.Core.Booth.BoothFetchStatus.Success);

        public Task<int> ReconcileUnresolvedAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<ItemPreview?> PreviewAsync(string itemId, CancellationToken cancellationToken = default)
            => Task.FromResult<ItemPreview?>(new ItemPreview { Id = itemId, Name = "テスト商品" });

        /// <summary>BOOTHが「無い」と答えた形を返す（見つからないIDのまま登録する道の入口）。</summary>
        public bool PreviewNotOnBooth { get; set; }

        public Task<(ItemPreview? Preview, string? Error, bool NotOnBooth)> PreviewWithReasonAsync(
            string itemId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<(ItemPreview?, string?, bool)>(PreviewNotOnBooth
                ? (null, "見つかりませんでした", true)
                : (new ItemPreview { Id = itemId, Name = "テスト商品" }, null, false));

        public (string Hash, string ItemId, string Name)? AssignedUnpublished { get; private set; }

        public Task<bool> AssignUnpublishedItemIdAsync(
            string hash,
            string itemId,
            string displayName,
            CancellationToken cancellationToken = default)
        {
            AssignedUnpublished = (hash, itemId, displayName);
            return Task.FromResult(AssignSucceeds);
        }

        public RefreshOutcome Outcome { get; set; } = RefreshOutcome.Updated;

        public bool AssignSucceeds { get; set; } = true;

        public string? RefreshedItemId { get; private set; }

        public string? AssignedHash { get; private set; }

        public IReadOnlyList<string> ExcludedHashes { get; private set; } = [];

        public Task<RefreshOutcome> RefreshAsync(string itemId, CancellationToken cancellationToken = default)
        {
            RefreshedItemId = itemId;
            return Task.FromResult(Outcome);
        }

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

        public Task<bool> AssignItemIdAsync(string hash, string itemId, CancellationToken cancellationToken = default, IProgress<int>? requestsLeft = null)
        {
            AssignedHash = hash;
            return Task.FromResult(AssignSucceeds);
        }

        public Task UndoExcludeAsync(
            IReadOnlyList<UnresolvedFile> files, IReadOnlyCollection<string> excludedHashes, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public int ExcludeCalls { get; private set; }

        public Task<IReadOnlyList<string>> ExcludeAsync(
            IReadOnlyList<UnresolvedFile> files,
            string? reason,
            CancellationToken cancellationToken = default)
        {
            ExcludeCalls++;
            ExcludedHashes = [.. files.Select(file => file.Hash)];
            return Task.FromResult<IReadOnlyList<string>>(ExcludedHashes);
        }
    }

    /// <summary>アバターの登録簿の編集を覚えておく作り物。</summary>
    private sealed class FakeAvatarEditor : IAvatarRegistryEditor
    {
        public List<string> Calls { get; } = [];

        public bool RecheckSucceeds { get; set; } = true;

        public Task SetDisplayNameAsync(string itemId, string name, CancellationToken cancellationToken = default) => Note($"name {itemId} {name}");

        public Task SetMemoAsync(string itemId, string? memo, CancellationToken cancellationToken = default) => Note($"memo {itemId}");

        public Task SetOwnedManuallyAsync(string itemId, bool owned, CancellationToken cancellationToken = default) => Note($"owned {itemId} {owned}");

        public Task SetAvatarOverrideAsync(string itemId, bool? value, CancellationToken cancellationToken = default) => Note($"override {itemId}");

        public Task SetBaseAsync(string itemId, string? baseName, CancellationToken cancellationToken = default) => Note($"base {itemId} {baseName}");

        public Task RemoveFromBaseAsync(string itemId, string baseName, CancellationToken cancellationToken = default) => Note($"base- {itemId} {baseName}");

        public Task SetInferClothingAsync(string name, bool infer, CancellationToken cancellationToken = default) => Note($"infer {name}");

        public Task SetBaseItemIdAsync(string name, string? itemId, CancellationToken cancellationToken = default) => Note($"baseItem {name}");

        public Task<int> RenameBaseAsync(string oldName, string newName, CancellationToken cancellationToken = default)
        {
            Calls.Add($"rename {oldName} {newName}");
            return Task.FromResult(7);
        }

        public Task<int> DeleteBaseAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(3);

        public Task<AvatarBaseAddOutcome> AddBaseAsync(string name, CancellationToken cancellationToken = default)
        {
            Calls.Add($"base+ {name}");
            return Task.FromResult(AvatarBaseAddOutcome.Added);
        }

        public Task AddAliasAsync(string itemId, string text, CancellationToken cancellationToken = default) => Note($"alias+ {itemId}");

        public Task RemoveAliasAsync(string itemId, string text, CancellationToken cancellationToken = default) => Note($"alias- {itemId}");

        public Task<Chmonos.Core.Booth.BoothFetchStatus> RecheckAsync(string itemId, CancellationToken cancellationToken = default)
            => Task.FromResult(RecheckSucceeds
                ? Chmonos.Core.Booth.BoothFetchStatus.Success
                : Chmonos.Core.Booth.BoothFetchStatus.TemporaryFailure);

        private Task Note(string call)
        {
            Calls.Add(call);
            return Task.CompletedTask;
        }
    }

    /// <summary>画面がサービスを直に呼んでいた書き込みを UiCommand に寄せた（技術的負債 3-1）。振り分けを確かめる。</summary>
    [Fact]
    public async Task RoutesAvatarRegistryEditsAndReportsCounts()
    {
        var editor = new FakeAvatarEditor();
        var handler = new CommandHandler(new FakeImportPipeline(), new FakeItemService(), avatarEditor: editor);

        Assert.IsType<CommandResult.Done>(await handler.ExecuteAsync(new UiCommand.SetAvatarBase("111", "素体A")));
        Assert.IsType<CommandResult.Done>(await handler.ExecuteAsync(new UiCommand.RemoveAvatarFromBase("222", "素体A")));
        var renamed = Assert.IsType<CommandResult.Counted>(await handler.ExecuteAsync(new UiCommand.RenameBase("素体A", "素体B")));

        Assert.Equal(7, renamed.Count);
        Assert.Equal(["base 111 素体A", "base- 222 素体A", "rename 素体A 素体B"], editor.Calls);
    }

    /// <summary>共通素体を手で足す操作も UiCommand から通り、足したかどうかを返す（2026-09-28）。</summary>
    [Fact]
    public async Task RoutesAddingABaseByHand()
    {
        var editor = new FakeAvatarEditor();
        var handler = new CommandHandler(new FakeImportPipeline(), new FakeItemService(), avatarEditor: editor);

        var added = Assert.IsType<CommandResult.BaseAdded>(await handler.ExecuteAsync(new UiCommand.AddBase("素体C")));

        Assert.Equal(AvatarBaseAddOutcome.Added, added.Outcome);
        Assert.Equal(["base+ 素体C"], editor.Calls);
    }

    [Fact]
    public async Task ReportsARecheckThatCouldNotReachBooth()
    {
        var editor = new FakeAvatarEditor { RecheckSucceeds = false };
        var handler = new CommandHandler(new FakeImportPipeline(), new FakeItemService(), avatarEditor: editor);

        var failed = Assert.IsType<CommandResult.Failed>(await handler.ExecuteAsync(new UiCommand.RecheckAvatar("111")));

        // 届かなかったことと、もう一度押せばよいことを言う（E3）
        Assert.Contains("BOOTHに問い合わせできませんでした", failed.Message);
        Assert.Contains("もう一度", failed.Message);
    }

    /// <summary>
    /// 依存を渡し忘れた組み立ては**不具合として落ちる**（ユーザ判断 2026-09-20・E5）。
    /// 前は画面に「〜手段が設定されていません。」と出していたが、利用者には意味が取れず、
    /// 利用者の操作では起こらない（本番の組み立ては1か所で全部を渡している）。
    /// </summary>
    [Fact]
    public async Task ThrowsForAvatarEditsWithoutAnEditor()
    {
        var (handler, _, _) = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(new UiCommand.SetAvatarMemo("111", "メモ")));
    }

    [Fact]
    public async Task RoutesReconcileUnresolvedToItems()
    {
        var (handler, _, _) = Create();

        Assert.Equal(0, Assert.IsType<CommandResult.Counted>(await handler.ExecuteAsync(new UiCommand.ReconcileUnresolved())).Count);
    }

    private static (CommandHandler Handler, FakeImportPipeline Import, FakeItemService Items) Create()
    {
        var import = new FakeImportPipeline();
        var items = new FakeItemService();
        return (new CommandHandler(import, items), import, items);
    }

    [Fact]
    public async Task RoutesScanFoldersToImportPipeline()
    {
        var (handler, import, _) = Create();

        var result = await handler.ExecuteAsync(new UiCommand.ScanFolders([@"D:\storage"]));

        Assert.Equal([@"D:\storage"], import.ReceivedFolders);
        var imported = Assert.IsType<CommandResult.Imported>(result);
        Assert.Equal(2, imported.Summary.ItemsAdded);
    }

    [Fact]
    public async Task RoutesAssignItemIdAndReportsSavedItem()
    {
        var (handler, _, items) = Create();

        var result = await handler.ExecuteAsync(new UiCommand.AssignItemId("AAAA", "5813187"));

        Assert.Equal("AAAA", items.AssignedHash);
        Assert.Equal("5813187", Assert.IsType<CommandResult.ItemSaved>(result).ItemId);
    }

    /// <summary>
    /// 確定の後に裏へ投げる検出は、走っている間「済んでいない裏の作業」に数える（2026-10-03）。
    /// 数えずに投げていたので、App の試験が「投げた作業が全部済んだ」と見て保存先を消した後も検出が走り続け、
    /// 一時ファイルごと消されて落ち、その失敗が次の試験のログに混ざっていた
    /// </summary>
    [Fact]
    public async Task CountsTheDetectionAfterAssignmentAsPendingBackgroundWork()
    {
        var avatars = new HeldAvatarService();
        var handler = new CommandHandler(new FakeImportPipeline(), new FakeItemService(), avatars: avatars);

        Assert.IsType<CommandResult.ItemSaved>(await handler.ExecuteAsync(new UiCommand.AssignItemId("AAAA", "5813187")));
        await avatars.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(Chmonos.Core.Diagnostics.BackgroundWork.Pending > 0);

        avatars.Release.SetResult();
        await avatars.Finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>検出が呼ばれたら、放されるまで返らない。</summary>
    private sealed class HeldAvatarService : IAvatarService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RequestDetectAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task;
            Finished.TrySetResult();
        }

        public Task<AvatarDetectResult> DetectAsync(
            IProgress<AvatarDetectProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<AvatarSummary>> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AvatarSummary>>([]);

        public Task<IReadOnlyList<AvatarBaseSummary>> LoadBasesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AvatarBaseSummary>>([]);
    }

    /// <summary>
    /// 登録の列が札を持っている間は、登録の後の検出を始めず、札を返したら1回だけ始める（ユーザ判断 2026-10-06）。
    /// 始めると、検出が知らないアバターを問い合わせる1本ずつが、次の登録の問い合わせの合間に入る
    /// </summary>
    [Fact]
    public async Task HoldsTheDetectionWhileTheRegistrationQueueRunsAndStartsItOnceOnRelease()
    {
        var avatars = new HeldAvatarService();
        var handler = new CommandHandler(new FakeImportPipeline(), new FakeItemService(), avatars: avatars);

        var hold = handler.HoldAfterRegistration();
        Assert.IsType<CommandResult.ItemSaved>(await handler.ExecuteAsync(new UiCommand.AssignItemId("AAAA", "5813187")));
        Assert.IsType<CommandResult.ItemSaved>(await handler.ExecuteAsync(new UiCommand.AssignItemId("BBBB", "5813188")));
        await Task.Delay(50);
        Assert.False(avatars.Started.Task.IsCompleted);

        // 判定が終わったら画面へ知らせる（検索の写しの読み直しと、開いている商品ページの「表示する」）
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.AvatarsDetectedAfterRegistration += () => notified.TrySetResult();

        hold.Dispose();
        await avatars.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(notified.Task.IsCompleted);
        avatars.Release.SetResult();
        await avatars.Finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await notified.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ReportsFailureWhenAssignmentIsRejected()
    {
        var (handler, _, items) = Create();
        items.AssignSucceeds = false;

        var result = await handler.ExecuteAsync(new UiCommand.AssignItemId("AAAA", "5813187"));

        Assert.IsType<CommandResult.Failed>(result);
    }

    /// <summary>
    /// BOOTHが「無い」と答えたときだけ、見つからないIDのまま登録する道を出す（ユーザ判断 2026-09-29）。
    /// 一時的に届かないときに出すと、実はある商品を空のまま登録してしまう。
    /// </summary>
    [Fact]
    public async Task TellsThePreviewThatBoothSaidTheItemIsNotThere()
    {
        var (handler, _, items) = Create();
        items.PreviewNotOnBooth = true;

        var result = await handler.ExecuteAsync(new UiCommand.PreviewItem("5813187"));

        Assert.Equal("5813187", Assert.IsType<CommandResult.PreviewNotOnBooth>(result).ItemId);
    }

    [Fact]
    public async Task RoutesAssignUnpublishedItemIdAndReportsSavedItem()
    {
        var (handler, _, items) = Create();

        var result = await handler.ExecuteAsync(new UiCommand.AssignUnpublishedItemId("AAAA", "5813187", "季節の衣装"));

        Assert.Equal(("AAAA", "5813187", "季節の衣装"), items.AssignedUnpublished);
        Assert.Equal("5813187", Assert.IsType<CommandResult.ItemSaved>(result).ItemId);
    }

    [Fact]
    public async Task ReportsFailureWhenUnpublishedAssignmentIsRejected()
    {
        var (handler, _, items) = Create();
        items.AssignSucceeds = false;

        var result = await handler.ExecuteAsync(new UiCommand.AssignUnpublishedItemId("AAAA", "5813187", "季節の衣装"));

        Assert.IsType<CommandResult.Failed>(result);
    }

    [Fact]
    public async Task RoutesRefreshItem()
    {
        var (handler, _, items) = Create();

        var result = await handler.ExecuteAsync(new UiCommand.RefreshItem("5813187"));

        Assert.Equal("5813187", items.RefreshedItemId);
        Assert.IsType<CommandResult.ItemSaved>(result);
    }

    /// <summary>一時エラーは失敗として返すが、非公開判定とは区別された文言になる。</summary>
    [Theory]
    [InlineData(RefreshOutcome.NotFound)]
    [InlineData(RefreshOutcome.Delisted)]
    [InlineData(RefreshOutcome.TemporaryFailure)]
    [InlineData(RefreshOutcome.Unreachable)]
    [InlineData(RefreshOutcome.ServerError)]
    [InlineData(RefreshOutcome.Unreadable)]
    [InlineData(RefreshOutcome.Missing)]
    public async Task ReportsFailureForNonUpdatedRefreshOutcomes(RefreshOutcome outcome)
    {
        var (handler, _, items) = Create();
        items.Outcome = outcome;

        var result = await handler.ExecuteAsync(new UiCommand.RefreshItem("5813187"));

        Assert.IsType<CommandResult.Failed>(result);
    }

    /// <summary>
    /// つながっていないのと BOOTH の不調とは次の一手が違う（つなぐ／待つ）ので、文を言い分ける（点検 2026-09-28 の 9）。
    /// 押した人に「次回に再試行します」とは言わない。
    /// </summary>
    [Fact]
    public async Task TellsUnreachableApartFromBoothTrouble()
    {
        var (handler, _, items) = Create();

        items.Outcome = RefreshOutcome.Unreachable;
        var offline = Assert.IsType<CommandResult.Failed>(await handler.ExecuteAsync(new UiCommand.RefreshItem("5813187")));
        items.Outcome = RefreshOutcome.TemporaryFailure;
        var trouble = Assert.IsType<CommandResult.Failed>(await handler.ExecuteAsync(new UiCommand.RefreshItem("5813187")));

        Assert.Contains("ネットにつながっていない", offline.Message, StringComparison.Ordinal);
        Assert.Contains("BOOTHの不調", trouble.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("次回", trouble.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RoutesExcludeFilesInOneCall()
    {
        var (handler, _, items) = Create();

        var result = await handler.ExecuteAsync(
            new UiCommand.ExcludeFiles([File("AAAA", @"D:\storage\a.zip"), File("BBBB", @"D:\storage\b.zip")], "BOOTH商品ではない"));

        Assert.Equal(["AAAA", "BBBB"], items.ExcludedHashes);
        Assert.Equal(1, items.ExcludeCalls);
        Assert.Equal(["AAAA", "BBBB"], Assert.IsType<CommandResult.FilesExcluded>(result).AddedHashes);
    }

    private static UnresolvedFile File(string hash, string path) => new()
    {
        Hash = hash,
        Paths = [path],
        SizeBytes = 1,
        ModifiedAtUtc = DateTimeOffset.UnixEpoch,
        FirstSeenAt = DateTimeOffset.UnixEpoch,
    };

    // ---- まだ終わっていない登録の列（registration-queue.json。メモ60） ----

    private static (CommandHandler Handler, Storage.JsonFileStore<List<QueuedRegistration>> Store, string Root) CreateWithQueue()
    {
        var root = Path.Combine(Path.GetTempPath(), "chmonos-queue-file-" + Guid.NewGuid().ToString("N"));
        var paths = new Storage.AppPaths(root);
        paths.EnsureCreated();
        var store = new Storage.DataStore(paths).RegistrationQueue;
        return (new CommandHandler(new FakeImportPipeline(), new FakeItemService()) { RegistrationQueue = store }, store, root);
    }

    [Fact]
    public async Task 登録の列は_積んだ順に人が読める形で書き_済んだ物を外せる()
    {
        var (handler, store, root) = CreateWithQueue();
        try
        {
            var first = new QueuedRegistration { ItemId = "9900701", ItemName = "作り物の衣装", FileHashes = ["AAAA", "BBBB"], EstimatedRequests = 5 };
            var second = new QueuedRegistration { ItemId = "9900702", ItemName = "作り物の靴", FileHashes = ["CCCC"] };

            await handler.ExecuteAsync(new UiCommand.ChangeRegistrationQueue(list => [.. list, first]));
            await handler.ExecuteAsync(new UiCommand.ChangeRegistrationQueue(list => [.. list, second]));

            Assert.Equal(["9900701", "9900702"], store.Load().Select(record => record.ItemId));
            var text = System.IO.File.ReadAllText(store.Path);
            Assert.Contains("\"itemName\": \"作り物の衣装\"", text, StringComparison.Ordinal);
            Assert.Contains("\"estimatedRequests\": 5", text, StringComparison.Ordinal);

            // 見込みの無い行は欄ごと書かない（null を並べない）
            Assert.Equal(2, text.Split("estimatedRequests").Length);

            await handler.ExecuteAsync(new UiCommand.ChangeRegistrationQueue(list => [.. list.Where(record => record.ItemId != "9900701")]));
            Assert.Equal("9900702", Assert.Single(store.Load()).ItemId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 手で直した登録の列の欠けた配列は_空として読む()
    {
        var (_, store, root) = CreateWithQueue();
        try
        {
            System.IO.File.WriteAllText(store.Path, """[ { "itemId": "9900703", "itemName": "作り物", "fileHashes": null } ]""");

            Assert.Empty(Assert.Single(store.Load()).FileHashes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
