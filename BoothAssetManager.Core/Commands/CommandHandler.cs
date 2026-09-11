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
    private readonly IModificationService? _modifications;
    private readonly IAvatarService? _avatars;

    public CommandHandler(
        IImportPipeline import,
        IItemService items,
        IEditService? edit = null,
        UnpackedFolderRemover? unpackedRemover = null,
        Resolution.FallbackResolver? resolver = null,
        INotificationService? notifications = null,
        IUserTagService? userTags = null,
        IAttributeService? attributes = null,
        IModificationService? modifications = null,
        IAvatarService? avatars = null)
    {
        _import = import;
        _items = items;
        _edit = edit;
        _unpackedRemover = unpackedRemover;
        _resolver = resolver;
        _notifications = notifications;
        _userTags = userTags;
        _attributes = attributes;
        _modifications = modifications;
        _avatars = avatars;
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

            case UiCommand.RegisterItem register:
                return await _items.RegisterItemAsync(register.ItemId, cancellationToken)
                    ? new CommandResult.ItemSaved(register.ItemId)
                    : new CommandResult.Failed($"商品 {register.ItemId} をBOOTHから取得できませんでした。");

            case UiCommand.AssignItemId assign:
                if (await _items.AssignItemIdAsync(assign.Hash, assign.ItemId, cancellationToken))
                {
                    // 手で紐付けた商品は、説明文・タグ・種類名が揃っているのに、次の検出まで
                    // 対応アバターが空だった（「手で紐付けると上手く行かない」と見えていた）。
                    // **裏で走らせ、確定の画面は待たせない。**確定すると次の1件の自動検索が走るので、
                    // ここで待たせると #27 で直した待ちが戻る。検出は1本ずつなので重ならない
                    if (_avatars is { } avatars)
                    {
                        _ = Task.Run(async () =>
                        {
                            using var priority = Booth.BoothClient.Prioritize(Booth.BoothPriority.Detection);
                            try
                            {
                                await avatars.DetectAsync();
                            }
                            catch (Exception exception) when (exception is not OperationCanceledException)
                            {
                                // 拾えなくても、次の取り込みかアバター画面のボタンで拾われる
                            }
                        });
                    }

                    return new CommandResult.ItemSaved(assign.ItemId);
                }

                // **考えられる理由を書く。**失敗する道は「未確定の一覧に
                // そのファイルが無い」か「BOOTHから商品を作れない」の2つだけ。
                // 前者は同じ中身のファイルが複数あるときに起きる——1つ確定すると
                // 一覧から消えるので、残った行を押すと空振りになる。
                // そのときは既に済んでいるので、実は失敗ではない
                return new CommandResult.Failed(
                        $"商品ID {assign.ItemId} には確定できませんでした。"
                        + "同じ中身のファイルが他にもあって、そちらで既に確定済みかもしれません"
                        + "（その場合は商品ページのファイル一覧に出ています）。"
                        + $"出ていなければ、商品ID {assign.ItemId} がBOOTHで見つからなかった可能性があります。");

            case UiCommand.RegisterLocalItem local:
                var localId = await _items.RegisterLocalItemAsync(
                    local.Hash, local.DisplayName, cancellationToken);
                return localId is not null
                    ? new CommandResult.ItemSaved(localId)
                    : new CommandResult.Failed("対象のファイルが未確定に見つかりませんでした。");

            case UiCommand.PlanItemIdChange plan:
                var planned = await _items.PlanItemIdChangeAsync(plan.FromId, plan.ToId, cancellationToken);
                return planned is not null
                    ? new CommandResult.ItemIdChangePlanned(planned)
                    : new CommandResult.Failed("移せません。同じIDか、元の商品が見つかりません。");

            case UiCommand.ChangeItemId change:
                var changed = await _items.ChangeItemIdAsync(
                    change.FromId, change.ToId, change.SkippedPurchases, cancellationToken);
                return changed switch
                {
                    ItemIdChangeOutcome.Moved => new CommandResult.ItemSaved(change.ToId),
                    ItemIdChangeOutcome.SameId => new CommandResult.Failed("同じIDです。"),
                    ItemIdChangeOutcome.SourceMissing => new CommandResult.Failed("元の商品が見つかりませんでした。"),
                    _ => new CommandResult.Failed("移した先を用意できませんでした。"),
                };

            case UiCommand.AddUserImage addImage:
                var addedName = await _items.AddUserImageAsync(
                    addImage.ItemId, addImage.Bytes, addImage.Caption, cancellationToken);
                return addedName is not null
                    ? new CommandResult.UserImageAdded(addImage.ItemId, addedName)
                    : new CommandResult.Failed("画像として読めませんでした。");

            case UiCommand.RemoveUserImage removeImage:
                return await _items.RemoveUserImageAsync(
                    removeImage.ItemId, removeImage.FileName, cancellationToken)
                    ? new CommandResult.ItemSaved(removeImage.ItemId)
                    : new CommandResult.Failed("対象の商品が見つかりませんでした。");

            case UiCommand.MoveUserImage moveImage:
                return await _items.MoveUserImageAsync(
                    moveImage.ItemId, moveImage.FileName, moveImage.Delta, cancellationToken)
                    ? new CommandResult.ItemSaved(moveImage.ItemId)
                    : new CommandResult.Failed("これ以上は動かせません。");

            case UiCommand.PinThumbnail pin:
                return await _items.PinThumbnailAsync(pin.ItemId, pin.FileName, cancellationToken)
                    ? new CommandResult.ItemSaved(pin.ItemId)
                    : new CommandResult.Failed("対象の商品が見つかりませんでした。");

            case UiCommand.SetImageRole imageRole:
                return await _items.SetImageRoleAsync(
                    imageRole.ItemId,
                    imageRole.FileName,
                    imageRole.Role,
                    imageRole.IsUserAdded,
                    cancellationToken)
                    ? new CommandResult.ItemSaved(imageRole.ItemId)
                    : new CommandResult.Failed("対象の商品が見つかりませんでした。");

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
                    RefreshOutcome.NotOnBooth =>
                        new CommandResult.Failed("BOOTHに無い商品として登録したものなので、取り直せません。"),
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

            case UiCommand.CreateModification create:
                if (_modifications is null)
                {
                    return new CommandResult.Failed("改変の編集手段が設定されていません。");
                }

                var created = await _modifications.CreateAsync(
                    create.AvatarItemId, create.Name, cancellationToken);
                return created is not null
                    ? new CommandResult.ModificationCreated(created)
                    : new CommandResult.Failed("名前を入れてください。");

            case UiCommand.DeleteModification deleteMod:
                if (_modifications is null)
                {
                    return new CommandResult.Failed("改変の編集手段が設定されていません。");
                }

                return await _modifications.DeleteAsync(deleteMod.Id, cancellationToken)
                    ? new CommandResult.ModificationsChanged()
                    : new CommandResult.Failed("対象の改変が見つかりませんでした。");

            case UiCommand.RenameModification rename:
                return await RunModificationAsync(
                    () => _modifications!.RenameAsync(rename.Id, rename.Name, cancellationToken),
                    "名前を入れてください。");

            case UiCommand.SetModificationMemo memo:
                return await RunModificationAsync(
                    () => _modifications!.SetMemoAsync(memo.Id, memo.Memo, cancellationToken));

            case UiCommand.SetModificationProject project:
                return await RunModificationAsync(
                    () => _modifications!.SetProjectAsync(project.Id, project.Path, cancellationToken));

            case UiCommand.AddModificationMember addMember:
                return await RunModificationAsync(
                    () => _modifications!.AddMemberAsync(addMember.Id, addMember.Member, cancellationToken));

            case UiCommand.RemoveModificationMember removeMember:
                return await RunModificationAsync(
                    () => _modifications!.RemoveMemberAsync(removeMember.Id, removeMember.Index, cancellationToken));

            case UiCommand.MoveModificationMember moveMember:
                return await RunModificationAsync(
                    () => _modifications!.MoveMemberAsync(
                        moveMember.Id, moveMember.Index, moveMember.Delta, cancellationToken));

            case UiCommand.AddModificationImage addImage2:
                if (_modifications is null)
                {
                    return new CommandResult.Failed("改変の編集手段が設定されていません。");
                }

                return await _modifications.AddImageAsync(addImage2.Id, addImage2.Bytes, cancellationToken) is not null
                    ? new CommandResult.ModificationsChanged()
                    : new CommandResult.Failed("画像として読めませんでした。");

            case UiCommand.RemoveModificationImage removeImage2:
                return await RunModificationAsync(
                    () => _modifications!.RemoveImageAsync(removeImage2.Id, removeImage2.FileName, cancellationToken));

            case UiCommand.MoveModificationImage moveImage2:
                return await RunModificationAsync(
                    () => _modifications!.MoveImageAsync(
                        moveImage2.Id, moveImage2.FileName, moveImage2.Delta, cancellationToken));

            case UiCommand.SetAttributeDefault attributeDefault:
                if (_attributes is null)
                {
                    return new CommandResult.Failed("属性の編集手段が設定されていません。");
                }

                return new CommandResult.AttributesChanged(
                    await _attributes.SetDefaultAsync(
                        attributeDefault.Name,
                        attributeDefault.IsDefault,
                        cancellationToken));

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

            case UiCommand.DetachFile detach:
            {
                var detachOutcome = await _items.DetachFileAsync(
                    detach.ItemId, detach.Hash, detach.DeleteItemWhenEmpty, cancellationToken);
                return detachOutcome == Services.DetachOutcome.Missing
                    ? new CommandResult.Failed("そのファイルはこの商品に紐付いていませんでした。")
                    : new CommandResult.FileDetached(detach.ItemId, detachOutcome);
            }

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

            case UiCommand.DetectAvatars detect:
                if (_avatars is null)
                {
                    return new CommandResult.Failed("対応アバターの検出手段が設定されていません。");
                }

                return new CommandResult.AvatarsDetected(
                    await _avatars.DetectAsync(detect.Progress, cancellationToken));

            case UiCommand.ProposeCandidates propose:
                if (_resolver is null)
                {
                    return new CommandResult.Failed("候補の検索手段が設定されていません。");
                }

                // **画面が今まさに待っている対象。**確定を押すと次の1件へ自動で移るので、
                // 前の件の後始末（新しい商品を作る取得）と同じ User だと、その後ろに付く。
                // 1件ずつ間隔を空けるので待ちがそのまま目に見える
                using (Booth.BoothClient.Prioritize(Booth.BoothPriority.Foreground))
                {
                    return new CommandResult.CandidatesProposed(
                        await _resolver.ProposeAsync(propose.FilePath, cancellationToken, propose.Progress));
                }

            default:
                return new CommandResult.Failed($"未対応のコマンドです: {command.GetType().Name}");
        }
    }

    /// <summary>
    /// 改変の書き換えを1本にまとめる。
    ///
    /// 手段が無い／対象が無いの分岐が10箇所に並ぶと、どれかで文言がずれる。
    /// </summary>
    private async Task<CommandResult> RunModificationAsync(
        Func<Task<bool>> run,
        string failure = "対象の改変が見つかりませんでした。")
    {
        if (_modifications is null)
        {
            return new CommandResult.Failed("改変の編集手段が設定されていません。");
        }

        return await run()
            ? new CommandResult.ModificationsChanged()
            : new CommandResult.Failed(failure);
    }
}
