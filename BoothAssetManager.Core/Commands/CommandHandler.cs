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
    private readonly ISettingsService? _settings;
    private readonly IAvatarRegistryEditor? _avatarEditor;
    private readonly IShopService? _shops;
    private readonly Images.ImagePipeline? _images;
    private readonly Booth.IBoothClient? _client;
    private readonly Storage.JsonFileStore<List<Models.VideoTitleRecord>>? _videoTitles;
    private readonly Storage.JsonFileStore<List<Models.ShopNoteRecord>>? _shopNotes;

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
        IAvatarService? avatars = null,
        UnityPackageCatalog? unityPackages = null,
        ISettingsService? settings = null,
        IAvatarRegistryEditor? avatarEditor = null,
        IShopService? shops = null,
        Images.ImagePipeline? images = null,
        Booth.IBoothClient? client = null,
        Storage.JsonFileStore<List<Models.VideoTitleRecord>>? videoTitles = null,
        Storage.JsonFileStore<List<Models.ShopNoteRecord>>? shopNotes = null)
    {
        _videoTitles = videoTitles;
        _shopNotes = shopNotes;
        _settings = settings;
        _avatarEditor = avatarEditor;
        _shops = shops;
        _images = images;
        _client = client;
        _unityPackages = unityPackages;
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
        // 確かめ用の足跡（環境変数を付けたときだけ。Services.UiTrace）。何を押すと何が起きたかを1行で追えるようにする
        if (!Services.UiTrace.IsOn)
        {
            return await ExecuteCoreAsync(command, progress, cancellationToken);
        }

        var traced = await ExecuteCoreAsync(command, progress, cancellationToken);
        Services.UiTrace.Write("命令", $"{command.GetType().Name} → {traced.GetType().Name}"
            + (traced is CommandResult.Failed failure ? $"：{failure.Message}" : string.Empty));
        return traced;
    }

    private async Task<CommandResult> ExecuteCoreAsync(
        UiCommand command,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken)
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

            case UiCommand.ChangeUiState uiState:
                await (_settings ?? throw new InvalidOperationException("設定の保存先が渡されていません。"))
                    .UpdateUiStateAsync(uiState.Change, cancellationToken);
                return new CommandResult.Done();

            case UiCommand.ChangeSettings change:
                return new CommandResult.SettingsChanged(
                    await (_settings ?? throw new InvalidOperationException("設定の保存先が渡されていません。"))
                        .UpdateAsync(change.Change, cancellationToken));

            case UiCommand.SetAvatarName or UiCommand.SetAvatarMemo or UiCommand.SetAvatarOwned
                or UiCommand.SetAvatarOverride or UiCommand.SetAvatarBase or UiCommand.SetBaseInferClothing
                or UiCommand.SetBaseItemId or UiCommand.RenameBase or UiCommand.DeleteBase
                or UiCommand.AddAvatarAlias or UiCommand.RemoveAvatarAlias or UiCommand.RecheckAvatar:
                return _avatarEditor is null
                    ? new CommandResult.Failed("アバターの登録簿の編集手段が設定されていません。")
                    : await EditAvatarRegistryAsync(_avatarEditor, command, cancellationToken);

            case UiCommand.StartEditSession or UiCommand.AdvanceEditSession or UiCommand.NoteEditSaved
                or UiCommand.ReplaceEditSessionItemId or UiCommand.ClearEditSession:
                return _edit is null
                    ? new CommandResult.Failed("編集の保存手段が設定されていません。")
                    : await EditSessionAsync(_edit, command, cancellationToken);

            case UiCommand.UnhideItem unhide:
                await (_settings ?? throw new InvalidOperationException("設定の保存先が渡されていません。"))
                    .UnhideAsync(unhide.ItemId, cancellationToken);
                return new CommandResult.Done();

            case UiCommand.ForgetDetached forget:
                await (_settings ?? throw new InvalidOperationException("設定の保存先が渡されていません。"))
                    .ForgetDetachedAsync(forget.Hash, forget.ItemId, cancellationToken);
                return new CommandResult.Done();

            case UiCommand.RestoreExcluded restore:
                await (_settings ?? throw new InvalidOperationException("設定の保存先が渡されていません。"))
                    .RestoreExcludedAsync(restore.Hash, cancellationToken);
                return new CommandResult.Done();

            // ---- BOOTH への問い合わせ。入口の「人が押した」優先度のままだと、開いただけで取る物まで取り込みより先に出るので、
            //      人が押していない物は梯子の段に下げる（内側の指定が勝つ） ----

            case UiCommand.SyncShopIcons sync:
                if (_shops is null || _images is null)
                {
                    return new CommandResult.Failed("ショップの画像の取得手段が設定されていません。");
                }

                using (Booth.BoothClient.Prioritize(Booth.BoothPriority.ShopIcon))
                {
                    return new CommandResult.Counted(await _shops.SyncIconsAsync(sync.Shops, _images, sync.OnFetched, cancellationToken));
                }

            case UiCommand.EnsureShopBanner banner:
                if (_shops is null || _images is null)
                {
                    return new CommandResult.Failed("ショップの画像の取得手段が設定されていません。");
                }

                // 開いた画面に出す1枚なので、指名された画像と同じ段
                using (Booth.BoothClient.Prioritize(Booth.BoothPriority.PinnedImage))
                {
                    return new CommandResult.ShopBannerEnsured(await _shops.EnsureBannerAsync(banner.Subdomain, _images, cancellationToken));
                }

            case UiCommand.RefreshShopImages refresh:
                return _shops is null || _images is null
                    ? new CommandResult.Failed("ショップの画像の取得手段が設定されていません。")
                    : new CommandResult.ShopImagesRefreshed(await _shops.RefreshImagesAsync(refresh.Subdomain, _images, cancellationToken));

            case UiCommand.FetchBoothImage fetch:
                if (_client is null)
                {
                    return new CommandResult.Failed("BOOTHへの問い合わせ手段が設定されていません。");
                }

                using (Booth.BoothClient.Prioritize(Booth.BoothPriority.PinnedImage))
                {
                    var fetched = await _client.GetBinaryAsync(fetch.Url, cancellationToken);
                    return fetched.IsSuccess && fetched.Value is { } bytes
                        ? new CommandResult.ImageFetched(bytes)
                        : new CommandResult.Failed("BOOTHから画像を取れませんでした。");
                }

            case UiCommand.ChangeSearchHistory history:
                return new CommandResult.SearchHistoryChanged(
                    await (_settings ?? throw new InvalidOperationException("設定の保存先が渡されていません。"))
                        .ChangeSearchHistoryAsync(history.Change, cancellationToken));

            case UiCommand.ChangeShopNote shopNote:
            {
                var store = _shopNotes ?? throw new InvalidOperationException("ショップの星とメモの保存先が渡されていません。");
                var notes = await store.UpdateAsync(
                    records => Services.ShopNotes.Apply(
                        records, shopNote.Subdomain, shopNote.NameHint, shopNote.Uuid, shopNote.Change, DateTimeOffset.Now),
                    cancellationToken);
                return new CommandResult.ShopNotesChanged(notes);
            }

            case UiCommand.RememberVideoTitle remember:
                await (_videoTitles ?? throw new InvalidOperationException("動画のタイトルの控えの保存先が渡されていません。"))
                    .UpdateAsync(
                        records => VideoTitleBook.Remember(records, remember.VideoId, remember.Title, DateTimeOffset.Now),
                        cancellationToken);
                return new CommandResult.Done();

            case UiCommand.PruneVideoTitles:
            {
                var store = _videoTitles ?? throw new InvalidOperationException("動画のタイトルの控えの保存先が渡されていません。");
                var now = DateTimeOffset.Now;

                // 起動のたびに書き直さない（古い物が無ければ触らない。ファイルがまだ無ければ作らない）
                if (!VideoTitleBook.HasStale(store.Load(), now))
                {
                    return new CommandResult.Counted(0);
                }

                var removed = 0;
                await store.UpdateAsync(
                    records =>
                    {
                        var kept = VideoTitleBook.Prune(records, now);
                        removed = records.Count - kept.Count;
                        return kept;
                    },
                    cancellationToken);
                return new CommandResult.Counted(removed);
            }

            case UiCommand.ReconcileUnresolved:
                return new CommandResult.Counted(await _items.ReconcileUnresolvedAsync(cancellationToken));

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
                    FillUnityPackagesInBackground(assign.ItemId);

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
                                Diagnostics.AppLog.Error("手で紐付けた後の対応アバターの検出", exception);
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
                if (localId is not null)
                {
                    FillUnityPackagesInBackground(localId);
                }

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

            case UiCommand.UndoExclude undo:
                await _items.UndoExcludeAsync(undo.Files, cancellationToken);
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

            case UiCommand.SetModificationBlueprintId blueprint:
                return await RunModificationAsync(
                    () => _modifications!.SetBlueprintIdAsync(blueprint.Id, blueprint.BlueprintId, cancellationToken));

            case UiCommand.SetModificationProject project:
                return await RunModificationAsync(
                    () => _modifications!.SetProjectAsync(project.Id, project.Path, cancellationToken));

            case UiCommand.AddModificationMember addMember:
                return await RunModificationAsync(
                    () => _modifications!.AddMemberAsync(addMember.Id, addMember.Member, cancellationToken));

            case UiCommand.SetModificationMemberDetached detachMember:
                return await RunModificationAsync(
                    () => _modifications!.SetMemberDetachedAsync(
                        detachMember.Id, detachMember.Index, detachMember.Detached, cancellationToken));

            case UiCommand.RemoveModificationMember removeMember:
                return await RunModificationAsync(
                    () => _modifications!.RemoveMemberAsync(removeMember.Id, removeMember.Index, cancellationToken));

            case UiCommand.MoveModificationMember moveMember:
                return await RunModificationAsync(
                    () => _modifications!.MoveMemberAsync(
                        moveMember.Id, moveMember.Index, moveMember.Delta, cancellationToken));

            case UiCommand.RecordModificationMemberFiles recordFiles:
                return await RunModificationAsync(
                    () => _modifications!.ReplaceMemberAsync(
                        recordFiles.Id, recordFiles.Index, recordFiles.Members, cancellationToken),
                    "使ったファイルを記録できませんでした。選んでいる間に、改変の使ったものが変わったかもしれません。");

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

            case UiCommand.SwapFolderForArchive swap:
                return new CommandResult.ArchiveSwapped(
                    await _items.SwapFolderForArchiveAsync(swap.ItemId, swap.FolderPath, cancellationToken));

            case UiCommand.UnregisterFolder unregister:
                return await _items.UnregisterFolderAsync(unregister.ItemId, unregister.FolderPath, cancellationToken)
                    ? new CommandResult.ItemSaved(unregister.ItemId)
                    : new CommandResult.Failed("登録が見つかりませんでした。");

            case UiCommand.ExportBackup export:
                try
                {
                    var exported = await Task.Run(
                        () => Storage.BackupArchive.Export(export.Root, export.ZipPath, export.IncludeImages, cancellationToken),
                        cancellationToken);
                    return new CommandResult.BackupExported(exported);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return new CommandResult.Failed(
                        $"書き出せませんでした：{exception.Message}（書き出し先の空きが足りないか、書けない場所のことがあります）");
                }

            case UiCommand.RestoreBackup restore:
                try
                {
                    var restored = await Task.Run(
                        () => Storage.BackupArchive.Restore(restore.ZipPath, restore.DestinationRoot, cancellationToken),
                        cancellationToken);
                    return new CommandResult.BackupRestored(restored);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    return new CommandResult.Failed($"戻せませんでした：{exception.Message}");
                }

            case UiCommand.UnpackToTemporary unpack:
                try
                {
                    // 大きい zip は数秒かかるので画面の手を止めない
                    var folder = await Task.Run(
                        () => new Services.TemporaryUnpacker().Unpack(unpack.ZipPath, cancellationToken), cancellationToken);
                    return new CommandResult.Unpacked(folder);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException
                                                      or UnauthorizedAccessException or NotSupportedException)
                {
                    // 原因はこちらでは決め付けない。見当だけ添える
                    return new CommandResult.Failed(
                        $"展開できませんでした：{exception.Message}（zip が壊れているか、一時フォルダの空きが足りないことがあります）");
                }

            case UiCommand.SetFileVariations setVariations:
                return await _items.SetFileVariationsAsync(
                        setVariations.ItemId, setVariations.VariationByHash, cancellationToken)
                    ? new CommandResult.ItemSaved(setVariations.ItemId)
                    : new CommandResult.Failed("対象の商品がローカルにありません。");

            case UiCommand.DetachFile detach:
            {
                var detachOutcome = await _items.DetachFileAsync(
                    detach.ItemId, detach.Hash, detach.DeleteItemWhenEmpty, cancellationToken);
                return detachOutcome == Services.DetachOutcome.Missing
                    ? new CommandResult.Failed("そのファイルはこの商品に紐付いていませんでした。")
                    : new CommandResult.FileDetached(detach.ItemId, detachOutcome);
            }

            case UiCommand.ReattachFile reattach:
                var reattached = await _items.ReattachFileAsync(reattach.ItemId, reattach.Hash, cancellationToken);
                if (reattached == Services.ReattachOutcome.Reattached)
                {
                    FillUnityPackagesInBackground(reattach.ItemId);
                }

                return reattached switch
                {
                    Services.ReattachOutcome.Reattached => new CommandResult.ItemSaved(reattach.ItemId),
                    Services.ReattachOutcome.OwnedElsewhere => new CommandResult.Failed(
                        "このファイルは外した後で別の商品に紐付けてあるので、戻せません。先にそちらの商品から外してください。"),
                    _ => new CommandResult.Failed("そのファイルはこの商品から外したものではありませんでした。"),
                };

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

            case UiCommand.MarkNotificationsRead markSome:
                if (_notifications is null)
                {
                    return new CommandResult.Failed("要確認の保存手段が設定されていません。");
                }

                await _notifications.MarkReadAsync(markSome.Ids, cancellationToken);
                return new CommandResult.Done();

            case UiCommand.ResolveNotifications resolve:
                if (_notifications is null)
                {
                    return new CommandResult.Failed("要確認の保存手段が設定されていません。");
                }

                await _notifications.ResolveAsync(resolve.Ids, cancellationToken);
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
    private readonly UnityPackageCatalog? _unityPackages;

    /// <summary>
    /// 手でファイルを付けた後、その商品の unitypackage を裏で読む（取り込みの裏で読むのと同じ・ユーザ判断 2026-09-13）。
    /// **確定の画面は待たせない。**読めなくても、使うときに zip を解くか、次の取り込みで読む
    /// </summary>
    /// <summary>アバターの登録簿の編集。振り分けるだけ（ここに判断を書かない）。</summary>
    private static async Task<CommandResult> EditAvatarRegistryAsync(
        IAvatarRegistryEditor editor,
        UiCommand command,
        CancellationToken cancellationToken)
    {
        switch (command)
        {
            case UiCommand.SetAvatarName name:
                await editor.SetDisplayNameAsync(name.ItemId, name.Name, cancellationToken);
                break;
            case UiCommand.SetAvatarMemo memo:
                await editor.SetMemoAsync(memo.ItemId, memo.Memo, cancellationToken);
                break;
            case UiCommand.SetAvatarOwned owned:
                await editor.SetOwnedManuallyAsync(owned.ItemId, owned.Owned, cancellationToken);
                break;
            case UiCommand.SetAvatarOverride overrideValue:
                await editor.SetAvatarOverrideAsync(overrideValue.ItemId, overrideValue.Value, cancellationToken);
                break;
            case UiCommand.SetAvatarBase avatarBase:
                await editor.SetBaseAsync(avatarBase.ItemId, avatarBase.BaseName, cancellationToken);
                break;
            case UiCommand.SetBaseInferClothing infer:
                await editor.SetInferClothingAsync(infer.Name, infer.Infer, cancellationToken);
                break;
            case UiCommand.SetBaseItemId baseItem:
                await editor.SetBaseItemIdAsync(baseItem.Name, baseItem.ItemId, cancellationToken);
                break;
            case UiCommand.RenameBase rename:
                return new CommandResult.Counted(await editor.RenameBaseAsync(rename.OldName, rename.NewName, cancellationToken));
            case UiCommand.DeleteBase delete:
                return new CommandResult.Counted(await editor.DeleteBaseAsync(delete.Name, cancellationToken));
            case UiCommand.AddAvatarAlias add:
                await editor.AddAliasAsync(add.ItemId, add.Text, cancellationToken);
                break;
            case UiCommand.RemoveAvatarAlias remove:
                await editor.RemoveAliasAsync(remove.ItemId, remove.Text, cancellationToken);
                break;
            case UiCommand.RecheckAvatar recheck:
                // 原因は特定できないので、見当だけ並べて判断はユーザに残す
                return await editor.RecheckAsync(recheck.ItemId, cancellationToken)
                    ? new CommandResult.Done()
                    : new CommandResult.Failed("BOOTHに確認できませんでした。通信が失敗したか、取得の設定が入っていないことがあります。");
        }

        return new CommandResult.Done();
    }

    /// <summary>編集キューの位置の記録。振り分けるだけ。</summary>
    private static async Task<CommandResult> EditSessionAsync(
        IEditService edit,
        UiCommand command,
        CancellationToken cancellationToken)
    {
        switch (command)
        {
            case UiCommand.StartEditSession start:
                await edit.StartSessionAsync(start.ItemIds, cancellationToken);
                break;
            case UiCommand.AdvanceEditSession advance:
                await edit.AdvanceSessionAsync(advance.Index, cancellationToken);
                break;
            case UiCommand.NoteEditSaved saved:
                await edit.NoteSavedAsync(saved.ItemId, cancellationToken);
                break;
            case UiCommand.ReplaceEditSessionItemId replace:
                await edit.ReplaceItemIdAsync(replace.FromId, replace.ToId, cancellationToken);
                break;
            case UiCommand.ClearEditSession:
                await edit.ClearSessionAsync(cancellationToken);
                break;
        }

        return new CommandResult.Done();
    }

    private void FillUnityPackagesInBackground(string itemId)
    {
        if (_unityPackages is not { } catalog)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await catalog.FillItemAsync(itemId);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                  or System.Text.Json.JsonException)
            {
                Diagnostics.AppLog.Error("ファイルを付けた後の unitypackage の読み込み", exception);
            }
        });
    }

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
