using BoothAssetManager.Core.Commands;
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
        {
            ReceivedFolders = folders;
            return Task.FromResult(new ImportSummary { FilesScanned = 3, ItemsAdded = 2 });
        }
    }

    private sealed class FakeItemService : IItemService
    {
        public Task<bool> RegisterFolderAsync(string itemId, string folderPath, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<int> ReconcileUnresolvedAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<ItemPreview?> PreviewAsync(string itemId, CancellationToken cancellationToken = default)
            => Task.FromResult<ItemPreview?>(new ItemPreview { Id = itemId, Name = "テスト商品" });

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

        public Task<bool> AssignItemIdAsync(string hash, string itemId, CancellationToken cancellationToken = default)
        {
            AssignedHash = hash;
            return Task.FromResult(AssignSucceeds);
        }

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
