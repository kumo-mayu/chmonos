using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Commands;

/// <summary>
/// <see cref="UiCommand"/> を対応する処理へ振り分けるだけの層。
/// ここにロジックを書かないのは、UIとバックエンドの境界を1枚に保つため。
/// WPFに依存しないので、そのまま単体テストできる。
/// </summary>
public sealed class CommandHandler
{
    private readonly IImportPipeline _import;
    private readonly IItemService _items;

    public CommandHandler(IImportPipeline import, IItemService items)
    {
        _import = import;
        _items = items;
    }

    public async Task<CommandResult> ExecuteAsync(
        UiCommand command,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        switch (command)
        {
            case UiCommand.ScanFolders scan:
                return new CommandResult.Imported(
                    await _import.RunAsync(scan.Folders, progress, cancellationToken));

            case UiCommand.AssignItemId assign:
                return await _items.AssignItemIdAsync(assign.Hash, assign.ItemId, cancellationToken)
                    ? new CommandResult.ItemSaved(assign.ItemId)
                    : new CommandResult.Failed($"商品ID {assign.ItemId} を確定できませんでした。");

            case UiCommand.RefreshItem refresh:
                var outcome = await _items.RefreshAsync(refresh.ItemId, cancellationToken);
                return outcome switch
                {
                    RefreshOutcome.Updated => new CommandResult.ItemSaved(refresh.ItemId),
                    RefreshOutcome.NotFound => new CommandResult.Failed("BOOTHで見つかりませんでした。"),
                    RefreshOutcome.Delisted => new CommandResult.Failed("非公開または削除済みと判定しました。"),
                    RefreshOutcome.TemporaryFailure => new CommandResult.Failed("一時的に取得できませんでした。次回に再試行します。"),
                    RefreshOutcome.Missing => new CommandResult.Failed("対象のitemがローカルにありません。"),
                    _ => new CommandResult.Failed("不明な結果です。"),
                };

            case UiCommand.ExcludeFile exclude:
                await _items.ExcludeAsync(exclude.Hash, exclude.Paths, exclude.Reason, cancellationToken);
                return new CommandResult.Done();

            default:
                return new CommandResult.Failed($"未対応のコマンドです: {command.GetType().Name}");
        }
    }
}
