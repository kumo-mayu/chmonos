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
    private readonly IEditService? _edit;
    private readonly UnpackedFolderRemover? _unpackedRemover;
    private readonly Resolution.FallbackResolver? _resolver;
    private readonly INotificationService? _notifications;
    private readonly IAppTagService? _appTags;

    public CommandHandler(
        IImportPipeline import,
        IItemService items,
        IEditService? edit = null,
        UnpackedFolderRemover? unpackedRemover = null,
        Resolution.FallbackResolver? resolver = null,
        INotificationService? notifications = null,
        IAppTagService? appTags = null)
    {
        _import = import;
        _items = items;
        _edit = edit;
        _unpackedRemover = unpackedRemover;
        _resolver = resolver;
        _notifications = notifications;
        _appTags = appTags;
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

            case UiCommand.RemoveUnpackedFolders remove:
                if (_unpackedRemover is null)
                {
                    return new CommandResult.Failed("削除の実行手段が設定されていません。");
                }

                return new CommandResult.UnpackedFoldersRemoved(
                    await _unpackedRemover.RemoveAsync(remove.Folders, cancellationToken));

            case UiCommand.SaveItemLocal save:
                if (_edit is null)
                {
                    return new CommandResult.Failed("編集の保存手段が設定されていません。");
                }

                return await _edit.SaveLocalAsync(save.ItemId, save.Local, cancellationToken)
                    ? new CommandResult.ItemSaved(save.ItemId)
                    : new CommandResult.Failed("対象のitemがローカルにありません。");

            case UiCommand.AddAppTag addTag:
                if (_edit is null)
                {
                    return new CommandResult.Failed("編集の保存手段が設定されていません。");
                }

                return new CommandResult.AppTagsChanged(
                    await _edit.AddAppTagAsync(addTag.Top, addTag.Sub, cancellationToken));

            case UiCommand.AddAttribute addAttribute:
                if (_edit is null)
                {
                    return new CommandResult.Failed("編集の保存手段が設定されていません。");
                }

                return new CommandResult.AttributesChanged(
                    await _edit.AddAttributeAsync(addAttribute.Name, cancellationToken));

            case UiCommand.RenameAppTag rename:
                if (_appTags is null)
                {
                    return new CommandResult.Failed("分類の編集手段が設定されていません。");
                }

                return new CommandResult.AppTagsRewritten(rename.Sub is null
                    ? await _appTags.RenameTopAsync(rename.Top, rename.NewName, cancellationToken)
                    : await _appTags.RenameSubAsync(rename.Top, rename.Sub, rename.NewName, cancellationToken));

            case UiCommand.DeleteAppTag delete:
                if (_appTags is null)
                {
                    return new CommandResult.Failed("分類の編集手段が設定されていません。");
                }

                return new CommandResult.AppTagsRewritten(delete.Sub is null
                    ? await _appTags.DeleteTopAsync(delete.Top, cancellationToken)
                    : await _appTags.DeleteSubAsync(delete.Top, delete.Sub, cancellationToken));

            case UiCommand.SetAppTagMemo memo:
                if (_appTags is null)
                {
                    return new CommandResult.Failed("分類の編集手段が設定されていません。");
                }

                return new CommandResult.AppTagsChanged(
                    await _appTags.SetMemoAsync(memo.Top, memo.Sub, memo.Memo, cancellationToken));

            case UiCommand.PreviewItem preview:
                var loaded = await _items.PreviewAsync(preview.ItemId, cancellationToken);
                return loaded is null
                    ? new CommandResult.Failed($"商品ID {preview.ItemId} を取得できませんでした。")
                    : new CommandResult.PreviewLoaded(loaded);

            case UiCommand.RegisterFolder register:
                return await _items.RegisterFolderAsync(register.ItemId, register.FolderPath, cancellationToken)
                    ? new CommandResult.ItemSaved(register.ItemId)
                    : new CommandResult.Failed("フォルダを紐付けられませんでした。フォルダが存在するか、商品IDが正しいかを確認してください。");

            case UiCommand.UnregisterFolder unregister:
                return await _items.UnregisterFolderAsync(unregister.ItemId, unregister.FolderPath, cancellationToken)
                    ? new CommandResult.ItemSaved(unregister.ItemId)
                    : new CommandResult.Failed("登録が見つかりませんでした。");

            case UiCommand.SetNotificationRead setRead:
                if (_notifications is null)
                {
                    return new CommandResult.Failed("要確認の保存手段が設定されていません。");
                }

                await _notifications.SetReadAsync(setRead.Id, setRead.IsRead, cancellationToken);
                return new CommandResult.Done();

            case UiCommand.MarkAllNotificationsRead:
                if (_notifications is null)
                {
                    return new CommandResult.Failed("要確認の保存手段が設定されていません。");
                }

                await _notifications.MarkAllReadAsync(cancellationToken);
                return new CommandResult.Done();

            case UiCommand.ProposeCandidates propose:
                if (_resolver is null)
                {
                    return new CommandResult.Failed("候補の検索手段が設定されていません。");
                }

                return new CommandResult.CandidatesProposed(
                    await _resolver.ProposeAsync(propose.FilePath, cancellationToken, propose.Progress));

            default:
                return new CommandResult.Failed($"未対応のコマンドです: {command.GetType().Name}");
        }
    }
}
