using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

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

        public Task<ArchiveSwapOutcome> SwapFolderForArchiveAsync(string itemId, string folderPath, CancellationToken cancellationToken = default)
            => Task.FromResult(new ArchiveSwapOutcome(ArchiveSwapResult.Registered, "x.zip"));

        public Task<DetachOutcome> DetachFileAsync(string itemId, string hash, bool deleteItemWhenEmpty, CancellationToken cancellationToken = default) => Task.FromResult(DetachOutcome.Detached);

        public Task<ReattachOutcome> ReattachFileAsync(string itemId, string hash, CancellationToken cancellationToken = default) => Task.FromResult(ReattachOutcome.Reattached);

        public Task<bool> SetFileVariationsAsync(string itemId, IReadOnlyDictionary<string, long?> variationByHash, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> RegisterFolderAsync(string itemId, string folderPath, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<int> FetchImagesAsync(string itemId, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<BoothAssetManager.Core.Booth.BoothFetchStatus> RegisterItemAsync(string itemId, CancellationToken cancellationToken = default)
            => Task.FromResult(BoothAssetManager.Core.Booth.BoothFetchStatus.Success);

        public Task<int> ReconcileUnresolvedAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<ItemPreview?> PreviewAsync(string itemId, CancellationToken cancellationToken = default)
            => Task.FromResult<ItemPreview?>(new ItemPreview { Id = itemId, Name = "テスト商品" });

        public Task<(ItemPreview? Preview, string? Error)> PreviewWithReasonAsync(
            string itemId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<(ItemPreview?, string?)>(
                (new ItemPreview { Id = itemId, Name = "テスト商品" }, null));

        public RefreshOutcome Outcome { get; set; } = RefreshOutcome.Updated;

        public bool AssignSucceeds { get; set; } = true;

        public string? RefreshedItemId { get; private set; }

        public string? AssignedHash { get; private set; }

        public string? ExcludedHash { get; private set; }

        public Task<RefreshOutcome> RefreshAsync(string itemId, CancellationToken cancellationToken = default)
        {
            RefreshedItemId = itemId;
            return Task.FromResult(Outcome);
        }

        public Task<string?> RegisterLocalItemAsync(
            string hash,
            string displayName,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(LocalItemId.For(hash));

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

        public Task<bool> AssignItemIdAsync(string hash, string itemId, CancellationToken cancellationToken = default)
        {
            AssignedHash = hash;
            return Task.FromResult(AssignSucceeds);
        }

        public Task UndoExcludeAsync(IReadOnlyList<UnresolvedFile> files, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ExcludeAsync(
            string hash,
            IReadOnlyList<string> paths,
            string? reason,
            CancellationToken cancellationToken = default)
        {
            ExcludedHash = hash;
            return Task.CompletedTask;
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

        public Task<BoothAssetManager.Core.Booth.BoothFetchStatus> RecheckAsync(string itemId, CancellationToken cancellationToken = default)
            => Task.FromResult(RecheckSucceeds
                ? BoothAssetManager.Core.Booth.BoothFetchStatus.Success
                : BoothAssetManager.Core.Booth.BoothFetchStatus.TemporaryFailure);

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
        var renamed = Assert.IsType<CommandResult.Counted>(await handler.ExecuteAsync(new UiCommand.RenameBase("素体A", "素体B")));

        Assert.Equal(7, renamed.Count);
        Assert.Equal(["base 111 素体A", "rename 素体A 素体B"], editor.Calls);
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

    [Fact]
    public async Task ReportsFailureWhenAssignmentIsRejected()
    {
        var (handler, _, items) = Create();
        items.AssignSucceeds = false;

        var result = await handler.ExecuteAsync(new UiCommand.AssignItemId("AAAA", "5813187"));

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
    [InlineData(RefreshOutcome.Unreadable)]
    [InlineData(RefreshOutcome.Missing)]
    public async Task ReportsFailureForNonUpdatedRefreshOutcomes(RefreshOutcome outcome)
    {
        var (handler, _, items) = Create();
        items.Outcome = outcome;

        var result = await handler.ExecuteAsync(new UiCommand.RefreshItem("5813187"));

        Assert.IsType<CommandResult.Failed>(result);
    }

    [Fact]
    public async Task RoutesExcludeFile()
    {
        var (handler, _, items) = Create();

        var result = await handler.ExecuteAsync(
            new UiCommand.ExcludeFile("AAAA", [@"D:\storage\a.zip"], "BOOTH商品ではない"));

        Assert.Equal("AAAA", items.ExcludedHash);
        Assert.IsType<CommandResult.Done>(result);
    }
}
