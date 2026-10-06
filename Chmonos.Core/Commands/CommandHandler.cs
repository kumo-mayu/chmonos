using Chmonos.Core.Scanning;
using Chmonos.Core.Services;

namespace Chmonos.Core.Commands;

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

    /// <summary>見つからないファイルを中身で探して結び直す（G17）。</summary>
    private readonly MissingFileFinder? _missingFiles;

    /// <summary>ドライブ文字と通し番号の組（フォルダビュー）。</summary>
    private readonly VolumeTable? _volumes;

    /// <summary>まだ終わっていない登録の列（registration-queue.json。メモ60）。組み立ての引数を増やさないよう、作るときに入れる。</summary>
    public Storage.JsonFileStore<List<Models.QueuedRegistration>>? RegistrationQueue { private get; init; }

    /// <summary>
    /// やりかけの記録（pending-operations.json）。IDの変更・タグや属性の名前の変更を、この記録で囲んで走らせる。
    /// 無ければ記録せずに走らせる（保存先を持たない一部の試験の組み立て）。組み立ての引数を増やさないよう、作るときに入れる。
    /// </summary>
    public Storage.JsonFileStore<List<Models.PendingOperation>>? PendingOperations { private get; init; }

    private PendingOperationRunner? _pending;

    private PendingOperationRunner Pending
        => _pending ??= new PendingOperationRunner(PendingOperations, _items, _userTags, _attributes, _settings, _notifications);

    /// <summary>やりかけの記録を書けなかったので始めなかったときの文。</summary>
    private const string NotRecordedMessage =
        "保存先に書き込めなかったので、変更していません。保存先の空きと、ほかのアプリで開いていないかを確かめてください。";

    /// <summary>途中で止まった取り込みの記録（import-state.json）。</summary>
    private readonly Storage.JsonFileStore<ImportState>? _importState;

    /// <summary>読めない商品の記録を控えに戻す・BOOTH から作り直す（通知の行のボタン）。</summary>
    private readonly BrokenItemRepair? _brokenItems;

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
        Storage.JsonFileStore<List<Models.ShopNoteRecord>>? shopNotes = null,
        MissingFileFinder? missingFiles = null,
        VolumeTable? volumes = null,
        Storage.JsonFileStore<ImportState>? importState = null,
        BrokenItemRepair? brokenItems = null)
    {
        _brokenItems = brokenItems;
        _volumes = volumes;
        _importState = importState;
        _missingFiles = missingFiles;
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

    /// <summary>
    /// 組み立てのときに依存を渡し忘れたときだけ来る道（ユーザ判断 2026-09-20・E5）。
    ///
    /// **利用者の操作では起こらない。**本番の組み立ては `AppServiceContainer` の1か所だけで、
    /// そこは全部を渡している。渡し忘れは書き間違いなので、
    /// 意味の取れない文（「〜手段が設定されていません。」）を画面に出すのではなく、**不具合として落とす。**
    /// 同じファイルの「〜が渡されていません。」（設定の保存先など）と扱いを揃えた。
    /// </summary>
    /// <summary>
    /// 名前の変更・統合の命令の中で、保存した検索の条件の名前も同じ名前へ寄せる（錠の中で今の並びに当てる）。
    /// 設定の保存先が渡されていない組み立て（一部の試験）では何もしない
    /// </summary>
    private async Task FollowRenameInSavedSearchesAsync(
        Func<IReadOnlyList<Models.SearchHistoryEntry>, IReadOnlyList<Models.SearchHistoryEntry>> rewrite,
        CancellationToken cancellationToken)
    {
        if (_settings is null)
        {
            return;
        }

        await _settings.ChangeSavedSearchesAsync(
            list => new Services.SavedSearchList { Entries = rewrite(list.Entries) }, cancellationToken);
    }

    private static CommandResult MissingService(string what)
        => throw new InvalidOperationException($"{what}が組み立てのときに渡されていません（アプリの不具合）。");

    /// <summary>
    /// 読めない商品の記録を直した結果の文。行の近くに出るので、何が起きたかと次の一手を言う。
    /// 失敗の道はどれも記録をそのままにしている（よけた物は元へ戻している）ので、そう言う
    /// </summary>
    private static CommandResult RepairResult(string itemId, BrokenItemRepairResult result) => result switch
    {
        BrokenItemRepairResult.Repaired => new CommandResult.ItemSaved(itemId),
        BrokenItemRepairResult.NotBroken => new CommandResult.Failed("この記録はもう読めるようになっています。"),
        BrokenItemRepairResult.Missing => new CommandResult.Failed(
            "この記録はもうありません。管理対象から除外したか、ファイルを削除した可能性があります。"),
        BrokenItemRepairResult.NoCopy => new CommandResult.Failed("1つ前の版がありません。BOOTHから作り直してください。"),
        BrokenItemRepairResult.CopyUnreadable => new CommandResult.Failed(
            "1つ前の版も壊れていて戻せません。BOOTHから作り直してください。"),
        BrokenItemRepairResult.NotOnBooth => new CommandResult.Failed(
            "BOOTHに無い商品として登録したものなので、作り直せません。JSONを開いて直してください。"),
        BrokenItemRepairResult.NotFound => new CommandResult.Failed(
            $"商品 {itemId} はBOOTHに見つかりませんでした。記録はそのままなので、JSONを開いて直してください。"),
        _ => new CommandResult.Failed(
            "BOOTHから取れませんでした。記録はそのままです。通信を確かめて、少し待ってからもう一度お試しください。"),
    };

    /// <summary>
    /// 保存先を運び終えた。**門を閉じたまま返し、ログも止める**（画面はこの後すぐ新しい保存先で開き直す・ユーザ判断 2026-09-23）。
    ///
    /// 開いて返すと、待っていた書き込みが画面が閉じ直すまでの間に古い保存先へ流れていた。
    /// ログは門を通らずに書き足すので、止めないと引越しで消した元の場所に <c>logs/</c> を作り直す。
    /// </summary>
    private static void KeepClosedUntilRestart(Storage.StoreWriteGate.StoreHold hold)
    {
        hold.KeepClosedUntilRestart();
        Diagnostics.AppLog.Use(null);
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

        // 保存先を丸ごと運んでいる間（引越し・置き換え・戻す）は、書き込みを待たせる（E8）。
        // **読むだけの道はここを通らない**ので、その間も画面は見られる（ユーザ判断 2026-09-20）
        await Storage.StoreWriteGate.WaitAsync(cancellationToken);

        switch (command)
        {
            case UiCommand.ScanFolders scan:
                return new CommandResult.Imported(
                    await _import.RunAsync(scan.Work, progress, cancellationToken));

            case UiCommand.ChangeUiState uiState:
                await (_settings ?? throw new InvalidOperationException("設定の保存先が渡されていません。"))
                    .UpdateUiStateAsync(uiState.Change, cancellationToken);
                return new CommandResult.Done();

            case UiCommand.ChangeRegistrationQueue queue:
                await (RegistrationQueue ?? throw new InvalidOperationException("登録の列の保存先が渡されていません。"))
                    .UpdateAsync(queue.Change, cancellationToken);
                return new CommandResult.Done();

            case UiCommand.ChangeSettings change:
                return new CommandResult.SettingsChanged(
                    await (_settings ?? throw new InvalidOperationException("設定の保存先が渡されていません。"))
                        .UpdateAsync(change.Change, cancellationToken));

            case UiCommand.SetAvatarName or UiCommand.SetAvatarMemo or UiCommand.SetAvatarOwned
                or UiCommand.SetAvatarOverride or UiCommand.SetAvatarBase or UiCommand.RemoveAvatarFromBase or UiCommand.SetBaseInferClothing
                or UiCommand.SetBaseItemId or UiCommand.RenameBase or UiCommand.DeleteBase or UiCommand.AddBase
                or UiCommand.AddAvatarAlias or UiCommand.RemoveAvatarAlias or UiCommand.RecheckAvatar:
            {
                if (_avatarEditor is null)
                {
                    return MissingService("アバターの登録簿の編集");
                }

                var edited = await EditAvatarRegistryAsync(_avatarEditor, command, cancellationToken);

                // 共通素体の名前を変える・統合すると、保存した検索の「base:名前」の条件も付いていかせる
                if (command is UiCommand.RenameBase renameBase && renameBase.NewName.Trim().Length > 0 && edited is not CommandResult.Failed)
                {
                    await FollowRenameInSavedSearchesAsync(
                        entries => Services.SavedSearches.RenameBase(entries, renameBase.OldName, renameBase.NewName), cancellationToken);
                }

                return edited;
            }

            case UiCommand.StartEditSession or UiCommand.AdvanceEditSession or UiCommand.NoteEditSaved
                or UiCommand.ReplaceEditSessionItemId or UiCommand.ClearEditSession:
                return _edit is null
                    ? MissingService("編集の保存")
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
                return new CommandResult.ExclusionLifted(
                    await (_settings ?? throw new InvalidOperationException("設定の保存先が渡されていません。"))
                        .RestoreExcludedAsync(restore.Hash, cancellationToken));

            // ---- BOOTH への問い合わせ。入口の「人が押した」優先度のままだと、開いただけで取る物まで取り込みより先に出るので、
            //      人が押していない物は梯子の段に下げる（内側の指定が勝つ） ----

            case UiCommand.SyncShopIcons sync:
                if (_shops is null || _images is null)
                {
                    return MissingService("ショップの画像の取得");
                }

                using (Booth.BoothClient.Prioritize(Booth.BoothPriority.ShopIcon))
                {
                    return new CommandResult.Counted(await _shops.SyncIconsAsync(sync.Shops, _images, sync.OnFetched, cancellationToken));
                }

            case UiCommand.EnsureShopBanner banner:
                if (_shops is null || _images is null)
                {
                    return MissingService("ショップの画像の取得");
                }

                // 開いた画面に出す1枚なので、指名された画像と同じ段
                using (Booth.BoothClient.Prioritize(Booth.BoothPriority.PinnedImage))
                {
                    return new CommandResult.ShopBannerEnsured(await _shops.EnsureBannerAsync(banner.Subdomain, _images, cancellationToken));
                }

            case UiCommand.RefreshShopImages refresh:
                return _shops is null || _images is null
                    ? MissingService("ショップの画像の取得")
                    : new CommandResult.ShopImagesRefreshed(await _shops.RefreshImagesAsync(refresh.Subdomain, _images, cancellationToken));

            case UiCommand.FetchBoothImage fetch:
                if (_client is null)
                {
                    return MissingService("BOOTHへの問い合わせ");
                }

                using (Booth.BoothClient.Prioritize(Booth.BoothPriority.PinnedImage))
                {
                    var fetched = await _client.GetBinaryAsync(fetch.Url, cancellationToken);
                    if (fetched.IsSuccess && fetched.Value is { } bytes)
                    {
                        return new CommandResult.ImageFetched(bytes);
                    }

                    // 届かなかったのと、BOOTH にもう無いのとで次の一手が違う（E3）
                    return new CommandResult.Failed(fetched.Status == Booth.BoothFetchStatus.NotFound
                        ? "この画像はBOOTHにありませんでした。商品ページから削除された可能性があります。"
                        : "BOOTHから画像を取れませんでした。通信を確かめて、少し待ってからもう一度お試しください。");
                }

            case UiCommand.ChangeSearchHistory history:
                return new CommandResult.SearchHistoryChanged(
                    await (_settings ?? throw new InvalidOperationException("設定の保存先が渡されていません。"))
                        .ChangeSearchHistoryAsync(history.Change, cancellationToken));

            case UiCommand.ChangeSavedSearches saved:
                return new CommandResult.SavedSearchesChanged(
                    await (_settings ?? throw new InvalidOperationException("設定の保存先が渡されていません。"))
                        .ChangeSavedSearchesAsync(saved.Change, cancellationToken));

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
                // 失敗の種類で文を分ける（E3）。待てば直るのか、待っても無いのかで次の一手が違う
                return await _items.RegisterItemAsync(register.ItemId, cancellationToken) switch
                {
                    Booth.BoothFetchStatus.Success => new CommandResult.ItemSaved(register.ItemId),
                    Booth.BoothFetchStatus.NotFound => new CommandResult.Failed(
                        $"商品 {register.ItemId} はBOOTHに見つかりませんでした。IDが違うか、販売が終わって非公開になっています。"),
                    _ => new CommandResult.Failed(
                        $"商品 {register.ItemId} をBOOTHから取れませんでした。通信を確かめて、少し待ってからもう一度お試しください。"),
                };

            case UiCommand.AssignItemId assign:
                if (await _items.AssignItemIdAsync(assign.Hash, assign.ItemId, cancellationToken, assign.RequestsLeft))
                {
                    FillUnityPackagesInBackground(assign.ItemId);

                    // 手で紐付けた商品は、説明文・タグ・種類名が揃っているのに、次の検出まで
                    // 対応アバターが空だった（「手で紐付けると上手く行かない」と見えていた）。
                    // **裏で走らせ、確定の画面は待たせない。**確定すると次の1件の自動検索が走るので、
                    // ここで待たせると #27 で直した待ちが戻る。検出は1本ずつなので重ならない
                    lock (_registrationHoldGate)
                    {
                        if (_registrationHolds > 0)
                        {
                            // 登録の列が動いている間は始めない（HoldAfterRegistration）。検出は知らないアバターを問い合わせるので、
                            // 始めると次の登録の問い合わせの合間に入り、2件目からの登録が遅くなる（ユーザ判断 2026-10-06）
                            _detectAfterRelease = true;
                            return new CommandResult.ItemSaved(assign.ItemId);
                        }
                    }

                    StartDetectionAfterRegistration();
                    return new CommandResult.ItemSaved(assign.ItemId);
                }

                // **考えられる理由を書く。**失敗する道は「未確定の一覧に
                // そのファイルが無い」か「BOOTHから商品を作れない」の2つだけ。
                // 前者は同じ中身のファイルが複数あるときに起きる——1つ確定すると
                // 一覧から消えるので、残った行を押すと空振りになる。
                // そのときは既に済んでいるので、実は失敗ではない
                return new CommandResult.Failed(
                        $"商品ID {assign.ItemId} には確定できませんでした。"
                        + "同じ中身のファイルが確定済みなら、商品ページのファイル一覧に表示されています。"
                        + "表示されていなければ、この商品IDがBOOTHで見つからなかった可能性があります。");

            case UiCommand.RegisterLocalItem local:
                var localId = await _items.RegisterLocalItemAsync(
                    local.Hashes, local.DisplayName, cancellationToken);
                if (localId is not null)
                {
                    FillUnityPackagesInBackground(localId);
                }

                return localId is not null
                    ? new CommandResult.ItemSaved(localId)
                    : new CommandResult.Failed("対象のファイルが未確定に見つかりませんでした。");

            case UiCommand.AssignUnpublishedItemId unpublished:
                if (await _items.AssignUnpublishedItemIdAsync(
                        unpublished.Hash, unpublished.ItemId, unpublished.DisplayName, cancellationToken))
                {
                    // BOOTHの情報は無いので対応アバターの検出はしない（説明文もタグも無い）。unitypackage の控えだけ埋める
                    FillUnityPackagesInBackground(unpublished.ItemId);
                    return new CommandResult.ItemSaved(unpublished.ItemId);
                }

                return new CommandResult.Failed("対象のファイルが未確定に見つかりませんでした。");

            case UiCommand.PlanItemIdChange plan:
                var planned = await _items.PlanItemIdChangeAsync(plan.FromId, plan.ToId, cancellationToken);
                return planned is not null
                    ? new CommandResult.ItemIdChangePlanned(planned)
                    : new CommandResult.Failed("移せません。同じIDか、元の商品が見つかりません。");

            case UiCommand.ChangeItemId change:
                var changed = await Pending.ChangeItemIdAsync(
                    change.FromId, change.ToId, change.SkippedPurchases, cancellationToken);
                return changed switch
                {
                    ItemIdChangeOutcome.Moved => new CommandResult.ItemSaved(change.ToId),
                    ItemIdChangeOutcome.SameId => new CommandResult.Failed("同じIDです。"),
                    ItemIdChangeOutcome.SourceMissing => new CommandResult.Failed("元の商品が見つかりませんでした。"),
                    ItemIdChangeOutcome.ImagesNotMoved => new CommandResult.Failed(
                        "自分で追加した画像を移せなかったので、商品IDは変えていません。ほかのアプリで画像を開いていないか確かめて、もう一度押してください。"),
                    ItemIdChangeOutcome.NotRecorded => new CommandResult.Failed(NotRecordedMessage),
                    _ => new CommandResult.Failed("移動先を用意できませんでした。"),
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
                    // 押した人には「次回に再試行します」ではなく次の一手を言う。待てば直る不調と、
                    // つながっていないのとは一手が違う（待つ／つなぐ）ので言い分ける（点検 2026-09-28 の 9）。
                    // 5xx（ServerError）と 429 などは、押した人の一手はどちらも「待つ」なので同じ文にする
                    RefreshOutcome.ServerError or RefreshOutcome.TemporaryFailure => new CommandResult.Failed(
                        "BOOTHの不調で取得できませんでした。少し待ってからもう一度押してください。"),
                    RefreshOutcome.Unreachable => new CommandResult.Failed(
                        "取得できませんでした。ネットにつながっていないようです。つながってからもう一度押してください。"),
                    RefreshOutcome.Unreadable => new CommandResult.Failed(
                        "BOOTHから届いた商品情報を読めませんでした。BOOTHのメンテナンス中か、ページの形が変わったことがあります。"
                        + "時間をおいてもう一度押してください。"),
                    RefreshOutcome.Missing => new CommandResult.Failed("対象の商品データが手元にありません。"),
                    RefreshOutcome.NotOnBooth =>
                        new CommandResult.Failed("BOOTHに無い商品として登録したものなので、取り直せません。"),
                    _ => new CommandResult.Failed("不明な結果です。"),
                };

            case UiCommand.ExcludeFiles exclude:
                return new CommandResult.FilesExcluded(
                    await _items.ExcludeAsync(exclude.Files, exclude.Reason, cancellationToken));

            case UiCommand.UndoExclude undo:
                await _items.UndoExcludeAsync(undo.Files, undo.ExcludedHashes, cancellationToken);
                return new CommandResult.Done();

            case UiCommand.RemoveUnpackedFolders remove:
                if (_unpackedRemover is null)
                {
                    return MissingService("削除の実行");
                }

                return new CommandResult.UnpackedFoldersRemoved(
                    await _unpackedRemover.RemoveAsync(remove.Folders, cancellationToken));

            case UiCommand.SaveItemLocal save:
                if (_edit is null)
                {
                    return MissingService("編集の保存");
                }

                return await _edit.SaveLocalAsync(save.ItemId, save.Local, save.Owns, cancellationToken)
                    ? new CommandResult.ItemSaved(save.ItemId)
                    : new CommandResult.Failed("対象の商品データが手元にありません。");

            case UiCommand.ConfirmAvatars confirm:
                if (_edit is null)
                {
                    return MissingService("対応アバターの確認");
                }

                return await _edit.ConfirmAvatarsAsync(confirm.ItemId, confirm.AvatarItemIds, cancellationToken)
                    ? new CommandResult.ItemSaved(confirm.ItemId)
                    : new CommandResult.Failed("対象の商品データが手元にありません。");

            case UiCommand.AddUserTag addTag:
                if (_edit is null)
                {
                    return MissingService("編集の保存");
                }

                return new CommandResult.UserTagsChanged(
                    await _edit.AddUserTagAsync(addTag.Top, addTag.Sub, cancellationToken));

            case UiCommand.AddAttribute addAttribute:
                if (_edit is null)
                {
                    return MissingService("編集の保存");
                }

                return new CommandResult.AttributesChanged(
                    await _edit.AddAttributeAsync(addAttribute.Name, cancellationToken));

            case UiCommand.RenameUserTag rename:
                if (_userTags is null)
                {
                    return MissingService("ユーザータグの編集");
                }

                // 一覧・商品・保存した検索の条件を順に書くので、やりかけの記録で囲む（落ちたら次の起動で続ける）
                return await Pending.RenameUserTagAsync(rename.Top, rename.Sub, rename.NewName, cancellationToken) is { } renamedTag
                    ? new CommandResult.UserTagsRewritten(renamedTag)
                    : new CommandResult.Failed(NotRecordedMessage);

            case UiCommand.DeleteUserTag delete:
                if (_userTags is null)
                {
                    return MissingService("ユーザータグの編集");
                }

                return new CommandResult.UserTagsRewritten(delete.Sub is null
                    ? await _userTags.DeleteTopAsync(delete.Top, cancellationToken)
                    : await _userTags.DeleteSubAsync(delete.Top, delete.Sub, cancellationToken));

            case UiCommand.SetUserTagMemo memo:
                if (_userTags is null)
                {
                    return MissingService("ユーザータグの編集");
                }

                return new CommandResult.UserTagsChanged(
                    await _userTags.SetMemoAsync(memo.Top, memo.Sub, memo.Memo, cancellationToken));

            case UiCommand.ReorderUserTags reorder:
                if (_userTags is null)
                {
                    return MissingService("ユーザータグの編集");
                }

                return new CommandResult.UserTagsChanged(
                    await _userTags.ReorderAsync(reorder.Top, reorder.Names, cancellationToken));

            case UiCommand.MoveUserTagSub move:
                if (_userTags is null)
                {
                    return MissingService("ユーザータグの編集");
                }

                var movedResult = await _userTags.MoveSubAsync(
                    move.FromTop, move.Sub, move.ToTop, move.DropEmptySourceTop, cancellationToken);

                // 保存した検索の条件も付いていかせる（名前の変更と同じ。残る側の綴りはマスタの名前）。移せなかったときは触らない
                if (movedResult.WasMoved)
                {
                    var toTopMaster = movedResult.Master.Tops.FirstOrDefault(
                        top => string.Equals(top.Name, move.ToTop, StringComparison.CurrentCultureIgnoreCase));
                    var movedSubName = toTopMaster?.Subs.FirstOrDefault(
                        sub => string.Equals(sub.Name, move.Sub, StringComparison.CurrentCultureIgnoreCase))?.Name ?? move.Sub;
                    await FollowRenameInSavedSearchesAsync(
                        entries => Services.SavedSearches.MoveUserTagSub(
                            entries, move.FromTop, move.Sub, toTopMaster?.Name ?? move.ToTop, movedSubName),
                        cancellationToken);
                }

                return new CommandResult.UserTagsRewritten(movedResult);

            case UiCommand.NestUserTagTop nest:
                if (_userTags is null)
                {
                    return MissingService("ユーザータグの編集");
                }

                var nested = await _userTags.NestTopAsync(nest.Top, nest.IntoTop, cancellationToken);
                if (nested.WasRefused)
                {
                    return new CommandResult.Failed($"「{nest.Top}」には小分類があるため、小分類にできませんでした。");
                }

                // 保存した検索の条件も付いていかせる。統合のときは入れ先に既にある小分類の綴りへ寄せる
                var intoTopMaster = nested.Master.Tops.FirstOrDefault(
                    top => string.Equals(top.Name, nest.IntoTop, StringComparison.CurrentCultureIgnoreCase));
                var nestedSubName = intoTopMaster?.Subs.FirstOrDefault(
                    sub => string.Equals(sub.Name, nest.Top, StringComparison.CurrentCultureIgnoreCase))?.Name ?? nest.Top;
                await FollowRenameInSavedSearchesAsync(
                    entries => Services.SavedSearches.NestUserTagTop(
                        entries, nest.Top, intoTopMaster?.Name ?? nest.IntoTop, nestedSubName),
                    cancellationToken);
                return new CommandResult.UserTagsRewritten(nested);

            case UiCommand.RenameAttribute renameAttribute:
                if (_attributes is null)
                {
                    return MissingService("属性の編集");
                }

                // 一覧・商品・設定のカードの属性・保存した検索の条件を順に書くので、やりかけの記録で囲む（落ちたら次の起動で続ける）
                return await Pending.RenameAttributeAsync(
                        renameAttribute.OldName, renameAttribute.NewName, renameAttribute.Keep, cancellationToken) is { } renamed
                    ? new CommandResult.AttributesRewritten(renamed)
                    : new CommandResult.Failed(NotRecordedMessage);

            case UiCommand.DeleteAttribute deleteAttribute:
                if (_attributes is null)
                {
                    return MissingService("属性の編集");
                }

                var deleted = await _attributes.DeleteAsync(deleteAttribute.Name, cancellationToken);
                if (_settings is not null)
                {
                    await _settings.UpdateAsync(current => current.WithCardAttributeRemoved(deleteAttribute.Name), cancellationToken);
                }

                return new CommandResult.AttributesRewritten(deleted);

            case UiCommand.SetAttributeMemo attributeMemo:
                if (_attributes is null)
                {
                    return MissingService("属性の編集");
                }

                return new CommandResult.AttributesChanged(
                    await _attributes.SetMemoAsync(attributeMemo.Name, attributeMemo.Memo, cancellationToken));

            case UiCommand.CreateModification create:
                if (_modifications is null)
                {
                    return MissingService("改変の編集");
                }

                var created = await _modifications.CreateAsync(
                    create.AvatarItemId, create.Name, cancellationToken);
                return created is not null
                    ? new CommandResult.ModificationCreated(created)
                    : new CommandResult.Failed("名前を入れてください。");

            case UiCommand.DuplicateModification duplicateMod:
                if (_modifications is null)
                {
                    return MissingService("改変の編集");
                }

                return await _modifications.DuplicateAsync(duplicateMod.Id, cancellationToken) is { } duplicated
                    ? new CommandResult.ModificationCreated(duplicated)
                    : new CommandResult.Failed("対象の改変が見つかりませんでした。");

            case UiCommand.DeleteModification deleteMod:
                if (_modifications is null)
                {
                    return MissingService("改変の編集");
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
                        detachMember.Id, detachMember.Member, detachMember.Detached, cancellationToken));

            case UiCommand.RemoveModificationMember removeMember:
                return await RunModificationAsync(
                    () => _modifications!.RemoveMemberAsync(removeMember.Id, removeMember.Member, cancellationToken));

            case UiCommand.MoveModificationMember moveMember:
                return await RunModificationAsync(
                    () => _modifications!.MoveMemberAsync(
                        moveMember.Id, moveMember.Member, moveMember.Delta, cancellationToken));

            case UiCommand.RecordModificationMemberFiles recordFiles:
                return await RunModificationAsync(
                    () => _modifications!.ReplaceMemberAsync(
                        recordFiles.Id, recordFiles.Member, recordFiles.Members, cancellationToken),
                    "使ったファイルを記録できませんでした。選んでいる間に、改変の使ったものが変わった可能性があります。");

            case UiCommand.AddModificationImage addImage2:
                if (_modifications is null)
                {
                    return MissingService("改変の編集");
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
                    return MissingService("属性の編集");
                }

                return new CommandResult.AttributesChanged(
                    await _attributes.SetDefaultAsync(
                        attributeDefault.Name,
                        attributeDefault.IsDefault,
                        cancellationToken));

            case UiCommand.ReorderAttributes reorderAttributes:
                if (_attributes is null)
                {
                    return MissingService("属性の編集");
                }

                return new CommandResult.AttributesChanged(
                    await _attributes.ReorderAsync(reorderAttributes.Names, cancellationToken));

            case UiCommand.PreviewItem preview:
                var (loaded, error, notOnBooth) = await _items.PreviewWithReasonAsync(preview.ItemId, cancellationToken);
                if (loaded is not null)
                {
                    return new CommandResult.PreviewLoaded(loaded);
                }

                var previewFailure = error ?? $"商品ID {preview.ItemId} を取得できませんでした。";
                return notOnBooth
                    ? new CommandResult.PreviewNotOnBooth(preview.ItemId, previewFailure)
                    : new CommandResult.Failed(previewFailure);

            case UiCommand.RegisterFolder register:
                return await _items.RegisterFolderAsync(register.ItemId, register.FolderPath, cancellationToken, register.RequestsLeft) switch
                {
                    FolderRegistration.Registered => new CommandResult.ItemSaved(register.ItemId),
                    FolderRegistration.FolderMissing => new CommandResult.Failed("選んだフォルダが見つかりませんでした。"),
                    FolderRegistration.ItemUnavailable => new CommandResult.Failed($"商品ID {register.ItemId} を取得できませんでした。商品IDを確かめてください。"),
                    FolderRegistration.Unreadable => new CommandResult.Failed("選んだフォルダの中を読めませんでした。"),
                    _ => new CommandResult.Failed("商品が削除されていたので、紐付けませんでした。"),
                };

            case UiCommand.RelocateFolder relocate:
                return await _items.RelocateFolderAsync(relocate.ItemId, relocate.FromPath, relocate.ToPath, cancellationToken) switch
                {
                    FolderRelocation.Moved => new CommandResult.ItemSaved(relocate.ItemId),
                    FolderRelocation.TargetMissing => new CommandResult.Failed("選んだフォルダが見つかりません。もう一度探してください。"),
                    FolderRelocation.RegisteredElsewhere => new CommandResult.Failed("このフォルダはほかの商品に登録されています。"),
                    FolderRelocation.Unreadable => new CommandResult.Failed("選んだフォルダの中を読めませんでした。"),
                    _ => new CommandResult.Failed("この登録は外されています。"),
                };

            case UiCommand.SwapFolderForArchive swap:
                return new CommandResult.ArchiveSwapped(
                    await _items.SwapFolderForArchiveAsync(
                        swap.ItemId, swap.FolderPath, swap.LiftExclusion, swap.TakeFromOtherItems, cancellationToken));

            case UiCommand.AttachFile attach:
                var attached = await _items.AttachFileAsync(
                    attach.ItemId, attach.Path, attach.LiftExclusion, attach.TakeFromOtherItems, cancellationToken);
                if (attached.Result == Services.FileAttachResult.Attached)
                {
                    // 登録の後と同じに、中の unitypackage を裏で読む（Unity へ送る候補。商品ページは後から届いた分を出す）
                    FillUnityPackagesInBackground(attach.ItemId);
                }

                return new CommandResult.FileAttached(attached);

            case UiCommand.UnregisterFolder unregister:
                return await _items.UnregisterFolderAsync(unregister.ItemId, unregister.FolderPath, cancellationToken)
                    ? new CommandResult.ItemSaved(unregister.ItemId)
                    : new CommandResult.Failed("登録が見つかりませんでした。");

            // 門を持つ3つは、**取るのも放すのも Task.Run の中で行う。**
            // 画面のスレッドの文脈で await すると、放す（開ける）のは画面のスレッドへ戻ってからになる。
            // そのとき画面のスレッドが同期で門を待っていると、互いに待ち合って固まる（StoreWriteGate.Enter の注）
            case UiCommand.ExportBackup export:
                try
                {
                    return await Task.Run(
                        async () =>
                        {
                            // 書き出している間は、束として食い違わないように書き込みを止める（E8）
                            using var holdForExport = await Storage.StoreWriteGate.HoldAsync(cancellationToken);
                            return (CommandResult)new CommandResult.BackupExported(Storage.BackupArchive.Export(
                                export.Root, export.ZipPath, export.IncludeImages, export.Progress, cancellationToken));
                        },
                        cancellationToken);
                }
                catch (Storage.StoreLinkException linked)
                {
                    // 始める前に断った。zip には手を付けていない
                    return new CommandResult.Failed(linked.Message);
                }
                catch (Storage.BackupReadException readFailed)
                {
                    // 途中で読めなくなった：作りかけの zip は消してある。前の zip が在ればそのまま（上書きしていない）
                    Diagnostics.AppLog.Error("バックアップの書き出し", readFailed);
                    return new CommandResult.Failed(
                        $"バックアップを書き出せませんでした。「{readFailed.RelativePath}」が途中で読めなくなりました。"
                        + (readFailed.PreviousKept ? "前のzipはそのまま残っています。" : "zipは作っていません。"));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Diagnostics.AppLog.Error("バックアップの書き出し", exception);
                    return new CommandResult.Failed($"バックアップを書き出せませんでした。{Services.FailureText.Cause(exception)}");
                }

            case UiCommand.RestoreBackup restore:
                try
                {
                    return await Task.Run(
                        async () =>
                        {
                            using var holdForRestore = await Storage.StoreWriteGate.HoldAsync(cancellationToken);
                            // 次の起動で戻した場所を開くよう、展開の中で場所を書き換える（書けなければ展開した物ごと取り消す）
                            var restored = Storage.BackupArchive.Restore(
                                restore.ZipPath, restore.DestinationRoot, restore.Progress, cancellationToken,
                                commit: () => Storage.StoreLocation.Save(restore.DestinationRoot));
                            KeepClosedUntilRestart(holdForRestore);
                            return (CommandResult)new CommandResult.BackupRestored(restored);
                        },
                        cancellationToken);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
                                                      or InvalidOperationException)
                {
                    Diagnostics.AppLog.Error("バックアップから戻す", exception);
                    return new CommandResult.Failed($"バックアップから戻せませんでした。{Services.FailureText.Cause(exception)}");
                }

            case UiCommand.MoveStore move:
                return await Task.Run(
                    async () =>
                    {
                        // **運んでいる間は書き込みを止める**（E8）。通してしまうと、コピー済みへ書いた分は
                        // 元を消すときに消え、列挙の後に生まれたファイルは運ばれず、増えた1件で突き合わせが落ちる
                        using var holdForMove = await Storage.StoreWriteGate.HoldAsync(cancellationToken);
                        // 次の起動で運んだ先を開くよう、元を消す前に場所を書き換える（書けなければ運んだ物を消して元のまま）
                        void Commit() => Storage.StoreLocation.Save(move.Destination);
                        var moved = move.Replace
                            ? Storage.StoreMover.Replace(move.Source, move.Destination, move.Progress, cancellationToken, Commit)
                            : Storage.StoreMover.Move(move.Source, move.Destination, move.Progress, cancellationToken, Commit);
                        if (moved.Succeeded)
                        {
                            KeepClosedUntilRestart(holdForMove);
                        }

                        return (CommandResult)new CommandResult.StoreMoved(moved);
                    },
                    cancellationToken);

            case UiCommand.UnpackToTemporary unpack:
                try
                {
                    // 大きい zip は数秒かかるので画面の手を止めない。
                    // 中止（OperationCanceledException）はここで受けずに上へ通す——失敗の文を出す話ではないので、
                    // 呼んだ側が黙って帯を畳む。書きかけは Unpack が投げる前に消している
                    var folder = await Task.Run(
                        () => new Services.TemporaryUnpacker().Unpack(unpack.ZipPath, unpack.Progress, cancellationToken),
                        cancellationToken);
                    return new CommandResult.Unpacked(folder);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException
                                                      or UnauthorizedAccessException or NotSupportedException)
                {
                    // 原因はこちらでは決め付けない。見当だけ添え、詳しい中身はログへ
                    Diagnostics.AppLog.Error("一時展開", exception);
                    return new CommandResult.Failed($"展開できませんでした。{Services.FailureText.Cause(exception)}");
                }

            case UiCommand.NoteFilePresence notePresence:
                return await _items.NoteFilePresenceAsync(notePresence.ItemId, notePresence.Sightings, cancellationToken)
                    ? new CommandResult.ItemSaved(notePresence.ItemId)
                    : new CommandResult.Done();

            case UiCommand.SetFileVariations setVariations:
                return await _items.SetFileVariationsAsync(
                        setVariations.ItemId, setVariations.VariationByHash, cancellationToken)
                    ? new CommandResult.ItemSaved(setVariations.ItemId)
                    : new CommandResult.Failed("対象の商品データが手元にありません。");

            case UiCommand.DetachFile detach:
            {
                var detachOutcome = await _items.DetachFileAsync(
                    detach.ItemId, detach.Hash, detach.DeleteItemWhenEmpty, cancellationToken);
                return detachOutcome == Services.DetachOutcome.Missing
                    ? new CommandResult.Failed("そのファイルはこの商品に紐付いていませんでした。")
                    : new CommandResult.FileDetached(detach.ItemId, detachOutcome);
            }

            case UiCommand.ForgetOldVersion forgetOld:
                return await _items.ForgetOldVersionAsync(forgetOld.ItemId, forgetOld.Hash, cancellationToken)
                    ? new CommandResult.ItemSaved(forgetOld.ItemId)
                    : new CommandResult.Failed("この古い版の記録は、もう片付いています。");

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
                    _ => new CommandResult.Failed("このファイルは、この商品から外したものではありません。"),
                };

            case UiCommand.SetNotificationRead setRead:
                if (_notifications is null)
                {
                    return MissingService("通知の保存");
                }

                await _notifications.SetReadAsync(setRead.Id, setRead.IsRead, cancellationToken);
                return new CommandResult.Done();

            case UiCommand.AddNotification add:
                if (_notifications is null)
                {
                    return MissingService("通知の保存");
                }

                await _notifications.AddAsync(add.Record, cancellationToken);
                return new CommandResult.Done();

            case UiCommand.FindMissingFiles find:
                if (_missingFiles is null || _settings is null)
                {
                    return MissingService("見つからないファイルの探索");
                }

                return new CommandResult.MissingFilesSearched(
                    await _missingFiles.FindAsync(
                        find.Folders ?? _settings.Current.WatchedFolders, find.Progress, cancellationToken));

            case UiCommand.DetectUnreadableItems:
                if (_notifications is null)
                {
                    return MissingService("通知の保存");
                }

                return new CommandResult.Counted(await _notifications.DetectUnreadableItemsAsync(cancellationToken));

            case UiCommand.RestoreItemCopy restoreCopy:
                if (_brokenItems is null)
                {
                    return MissingService("読めない商品の記録の直し");
                }

                return RepairResult(restoreCopy.ItemId, await _brokenItems.RestoreCopyAsync(restoreCopy.ItemId, cancellationToken));

            case UiCommand.RebuildItemFromBooth rebuild:
                if (_brokenItems is null)
                {
                    return MissingService("読めない商品の記録の直し");
                }

                return RepairResult(rebuild.ItemId, await _brokenItems.RebuildFromBoothAsync(rebuild.ItemId, cancellationToken));

            case UiCommand.ResumePendingOperations:
                return new CommandResult.Counted(await Pending.ResumeAsync(cancellationToken));

            case UiCommand.DetectOrphanReferences:
                if (_notifications is null)
                {
                    return MissingService("通知の保存");
                }

                return new CommandResult.Counted(await _notifications.DetectOrphanReferencesAsync(cancellationToken));

            case UiCommand.ObserveVolumes observe:
                if (_volumes is null)
                {
                    return MissingService("ドライブ文字の記録");
                }

                return new CommandResult.VolumesObserved(
                    await _volumes.ObserveAsync(observe.RecordedPaths, cancellationToken));

            case UiCommand.DiscardInterruptedImport:
                if (_importState is null)
                {
                    return MissingService("取り込みの続きの記録");
                }

                await _importState.SaveAsync(new ImportState(), cancellationToken);
                return new CommandResult.Done();

            case UiCommand.MarkAllNotificationsRead:
                if (_notifications is null)
                {
                    return MissingService("通知の保存");
                }

                await _notifications.MarkAllReadAsync(cancellationToken);
                return new CommandResult.Done();

            case UiCommand.MarkNotificationsRead markSome:
                if (_notifications is null)
                {
                    return MissingService("通知の保存");
                }

                await _notifications.MarkReadAsync(markSome.Ids, cancellationToken);
                return new CommandResult.Done();

            case UiCommand.ResolveNotifications resolve:
                if (_notifications is null)
                {
                    return MissingService("通知の保存");
                }

                await _notifications.ResolveAsync(resolve.Ids, cancellationToken);
                return new CommandResult.Done();

            case UiCommand.DetectAvatars detect:
                if (_avatars is null)
                {
                    return MissingService("対応アバターの検出");
                }

                return new CommandResult.AvatarsDetected(
                    await _avatars.DetectAsync(detect.Progress, cancellationToken));

            case UiCommand.ProposeCandidates propose:
                if (_resolver is null)
                {
                    return MissingService("候補の検索");
                }

                // **画面が今まさに待っている対象。**確定を押すと次の1件へ自動で移るので、
                // 前の件の後始末（新しい商品を作る取得）と同じ User だと、その後ろに付く。
                // 1件ずつ間隔を空けるので待ちがそのまま目に見える
                using (Booth.BoothClient.Prioritize(Booth.BoothPriority.Foreground))
                {
                    var proposal = await _resolver.ProposeAsync(propose.FilePath, cancellationToken, propose.Progress, propose.Listed);
                    return new CommandResult.CandidatesProposed(proposal.Candidates, proposal.BoothUnreachable);
                }

            case UiCommand.FindReplacementItem find:
                if (_resolver is null)
                {
                    return MissingService("候補の検索");
                }

                // 優先度は入口の「人が押した操作」のまま。窓は結果を待っているが、Foreground は
                // 未確定で次の1件へ移ったときの目の前の検索のための段で、ここで使うと段の意味が薄れる
                return new CommandResult.ReplacementsFound(await _resolver.FindReplacementWithImagesAsync(
                    find.FromId, find.PreviousName, find.ShopSubdomain, find.Paths, cancellationToken, find.Progress));

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
            case UiCommand.RemoveAvatarFromBase removeFromBase:
                await editor.RemoveFromBaseAsync(removeFromBase.ItemId, removeFromBase.BaseName, cancellationToken);
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
            case UiCommand.AddBase add:
                return new CommandResult.BaseAdded(await editor.AddBaseAsync(add.Name, cancellationToken));
            case UiCommand.AddAvatarAlias add:
                await editor.AddAliasAsync(add.ItemId, add.Text, cancellationToken);
                break;
            case UiCommand.RemoveAvatarAlias remove:
                await editor.RemoveAliasAsync(remove.ItemId, remove.Text, cancellationToken);
                break;
            case UiCommand.RecheckAvatar recheck:
                // 届かなかったのか、BOOTH に無かったのかで言い分ける（E3）。404 は「非公開になっていた」として書き込むので成功扱い
                return await editor.RecheckAsync(recheck.ItemId, cancellationToken) switch
                {
                    Booth.BoothFetchStatus.TemporaryFailure => new CommandResult.Failed(
                        "BOOTHに問い合わせできませんでした。通信を確かめて、少し待ってからもう一度押してください。"),
                    _ => new CommandResult.Done(),
                };
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
                await edit.StartSessionAsync(start.ItemIds, start.Index, cancellationToken);
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

    private readonly object _registrationHoldGate = new();
    private int _registrationHolds;
    private bool _detectAfterRelease;

    /// <summary>
    /// 登録の列が動いている間、登録の後に続く問い合わせ（残りの画像・対応アバターの検出）を待たせる（ユーザ判断 2026-10-06・メモ60 案B の続き）。
    /// 門は空いた時点で待っている物から選ぶので、続けて始めると次の登録の問い合わせの合間に1本ずつ入り、2件目からの登録が遅くなる。
    /// 返した物を Dispose すると（列が空になったら）、残りの画像はまとめて⑤の段で、検出は1回にまとめて③の段で始める。
    /// 閉じて途中で止まっても、列は次の起動で続きから流れ、終わったときに同じく始まる
    /// </summary>
    public IDisposable HoldAfterRegistration()
    {
        lock (_registrationHoldGate)
        {
            _registrationHolds++;
        }

        var images = _items.HoldRemainingImages();
        return new RegistrationHold(() =>
        {
            images.Dispose();
            bool detect;
            lock (_registrationHoldGate)
            {
                detect = --_registrationHolds == 0 && _detectAfterRelease;
                if (detect)
                {
                    _detectAfterRelease = false;
                }
            }

            if (detect)
            {
                StartDetectionAfterRegistration();
            }
        });
    }

    private sealed class RegistrationHold(Action release) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                release();
            }
        }
    }

    /// <summary>手で紐付けた後の対応アバターの検出を裏で始める（③の段。まとめて確定したときは1回にまとめる・N4）。</summary>
    private void StartDetectionAfterRegistration()
    {
        if (_avatars is not { } avatars)
        {
            return;
        }

        // 落ちても、次の取り込みかアバター画面のボタンで拾われる（失敗はログに残る）
        Diagnostics.BackgroundWork.Run("手で紐付けた後の対応アバターの検出", async () =>
        {
            using var priority = Booth.BoothClient.Prioritize(Booth.BoothPriority.Detection);
            await avatars.RequestDetectAsync();

            // 画面へ知らせる。前は何も知らせず、検索のカードや対応アバターの条件が次に読み直すまで古いまま、
            // 開いている商品ページも組み直されなかった（2026-10-06 の確認で見つけた穴）
            AvatarsDetectedAfterRegistration?.Invoke();
        });
    }

    /// <summary>登録の後の対応アバターの判定が終わった（裏のスレッドで呼ばれる）。画面は検索の写しを読み直し、開いている商品ページへ知らせる。</summary>
    public event Action? AvatarsDetectedAfterRegistration;

    private void FillUnityPackagesInBackground(string itemId)
    {
        if (_unityPackages is not { } catalog)
        {
            return;
        }

        Diagnostics.BackgroundWork.Run("ファイルを付けた後の unitypackage の読み込み", () => catalog.FillItemAsync(itemId));
    }

    private async Task<CommandResult> RunModificationAsync(
        Func<Task<bool>> run,
        string failure = "対象の改変が見つかりませんでした。")
    {
        if (_modifications is null)
        {
            return MissingService("改変の編集");
        }

        return await run()
            ? new CommandResult.ModificationsChanged()
            : new CommandResult.Failed(failure);
    }
}
