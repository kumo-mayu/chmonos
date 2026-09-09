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
    private readonly IUserTagService? _userTags;
    private readonly IAttributeService? _attributes;

    public CommandHandler(
        IImportPipeline import,
        IItemService items,
        IEditService? edit = null,
        UnpackedFolderRemover? unpackedRemover = null,
        Resolution.FallbackResolver? resolver = null,
        INotificationService? notifications = null,
        IUserTagService? userTags = null,
        IAttributeService? attributes = null)
    {
        _import = import;
        _items = items;
        _edit = edit;
        _unpackedRemover = unpackedRemover;
        _resolver = resolver;
        _notifications = notifications;
        _userTags = userTags;
        _attributes = attributes;
    }

    public async Task<CommandResult> ExecuteAsync(
        UiCommand command,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 人が押した操作は、取り込みより先に通す。押した人は画面の前で結果を待っている。
        // 取り込み自体（ScanFolders）は中で段ごとの優先度に切り替わるので、ここでは
        // まとめて上げてよい——内側の指定が勝つ。
        using var priority = Booth.BoothClient.Prioritize(Booth.BoothPriority.User);

        switch (command)
        {
            case UiCommand.ScanFolders scan:
                return new CommandResult.Imported(
                    await _import.RunAsync(scan.Work, progress, cancellationToken));

            case UiCommand.FetchItemImages fetchImages:
                return new CommandResult.ImagesFetched(
                    fetchImages.ItemId,
                    await _items.FetchImagesAsync(fetchImages.ItemId, cancellationToken));

            case UiCommand.AssignItemId assign:
                return await _items.AssignItemIdAsync(assign.Hash, assign.ItemId, cancellationToken)
                    ? new CommandResult.ItemSaved(assign.ItemId)
                    : new CommandResult.Failed($"商品ID {assign.ItemId} を確定できませんでした。");

            case UiCommand.RefreshItem refresh:
                var outcome = await _items.RefreshAsync(refresh.ItemId, cancellationToken);

                // 取り直しは画像を落とさない（梯子の規則を破らないため）。
                // ただし押したのは人で、その商品を見ているので、優先ボタンと
                // 同じ経路でそのまま取りに行く。規則が1本で済む
                if (outcome is RefreshOutcome.Updated)
                {
                    await _items.FetchImagesAsync(refresh.ItemId, cancellationToken);
                }

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

                return await _edit.SaveLocalAsync(save.ItemId, save.Local, save.Owns, cancellationToken)
                    ? new CommandResult.ItemSaved(save.ItemId)
                    : new CommandResult.Failed("対象のitemがローカルにありません。");

            case UiCommand.AddUserTag addTag:
                if (_edit is null)
                {
                    return new CommandResult.Failed("編集の保存手段が設定されていません。");
                }

                return new CommandResult.UserTagsChanged(
                    await _edit.AddUserTagAsync(addTag.Top, addTag.Sub, cancellationToken));

            case UiCommand.AddAttribute addAttribute:
                if (_edit is null)
                {
                    return new CommandResult.Failed("編集の保存手段が設定されていません。");
                }

                return new CommandResult.AttributesChanged(
                    await _edit.AddAttributeAsync(addAttribute.Name, cancellationToken));

            case UiCommand.RenameUserTag rename:
                if (_userTags is null)
                {
                    return new CommandResult.Failed("分類の編集手段が設定されていません。");
                }

                return new CommandResult.UserTagsRewritten(rename.Sub is null
                    ? await _userTags.RenameTopAsync(rename.Top, rename.NewName, cancellationToken)
                    : await _userTags.RenameSubAsync(rename.Top, rename.Sub, rename.NewName, cancellationToken));

            case UiCommand.DeleteUserTag delete:
                if (_userTags is null)
                {
                    return new CommandResult.Failed("分類の編集手段が設定されていません。");
                }

                return new CommandResult.UserTagsRewritten(delete.Sub is null
                    ? await _userTags.DeleteTopAsync(delete.Top, cancellationToken)
                    : await _userTags.DeleteSubAsync(delete.Top, delete.Sub, cancellationToken));

            case UiCommand.SetUserTagMemo memo:
                if (_userTags is null)
                {
                    return new CommandResult.Failed("分類の編集手段が設定されていません。");
                }

                return new CommandResult.UserTagsChanged(
                    await _userTags.SetMemoAsync(memo.Top, memo.Sub, memo.Memo, cancellationToken));

            case UiCommand.ReorderUserTags reorder:
                if (_userTags is null)
                {
                    return new CommandResult.Failed("分類の編集手段が設定されていません。");
                }

                return new CommandResult.UserTagsChanged(
                    await _userTags.ReorderAsync(reorder.Top, reorder.Names, cancellationToken));

            case UiCommand.MoveUserTagSub move:
                if (_userTags is null)
                {
                    return new CommandResult.Failed("分類の編集手段が設定されていません。");
                }

                return new CommandResult.UserTagsRewritten(await _userTags.MoveSubAsync(
                    move.FromTop, move.Sub, move.ToTop, move.DropEmptySourceTop, cancellationToken));

            case UiCommand.RenameAttribute renameAttribute:
                if (_attributes is null)
                {
                    return new CommandResult.Failed("属性の編集手段が設定されていません。");
                }

                return new CommandResult.AttributesRewritten(await _attributes.RenameAsync(
                    renameAttribute.OldName, renameAttribute.NewName, renameAttribute.Keep, cancellationToken));

            case UiCommand.DeleteAttribute deleteAttribute:
                if (_attributes is null)
                {
                    return new CommandResult.Failed("属性の編集手段が設定されていません。");
                }

                return new CommandResult.AttributesRewritten(
                    await _attributes.DeleteAsync(deleteAttribute.Name, cancellationToken));

            case UiCommand.SetAttributeMemo attributeMemo:
                if (_attributes is null)
                {
                    return new CommandResult.Failed("属性の編集手段が設定されていません。");
                }

                return new CommandResult.AttributesChanged(
                    await _attributes.SetMemoAsync(attributeMemo.Name, attributeMemo.Memo, cancellationToken));

            case UiCommand.ReorderAttributes reorderAttributes:
                if (_attributes is null)
                {
                    return new CommandResult.Failed("属性の編集手段が設定されていません。");
                }

                return new CommandResult.AttributesChanged(
                    await _attributes.ReorderAsync(reorderAttributes.Names, cancellationToken));

            case UiCommand.PreviewItem preview:
                var (loaded, error) = await _items.PreviewWithReasonAsync(preview.ItemId, cancellationToken);
                return loaded is null
                    ? new CommandResult.Failed(error ?? $"商品ID {preview.ItemId} を取得できませんでした。")
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
