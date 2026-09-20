using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;
using BoothIdResolver;
using BoothZipInspector;
using BoothZipInspector.Models;

namespace BoothAssetManager.Core.Scanning;

/// <summary>
/// 取り込みの段。**通信する段は、急ぐ順に並んでいる。**
///
/// 商品JSONだけで検索・絞り込み・統計に要るものは全部揃う
/// （名前・ショップ・タグ・カテゴリ・価格・variation・スキ数・R-18・販売終了・公開日）。
/// 画像は見た目のためだけで、探すのにも数えるのにも要らない。
/// 実データでは**画像が全リクエストの76%**を占めるので、後ろに回すと
/// 「使えるようになるまで」が劇的に縮む。
/// </summary>
public enum ImportPhase
{
    Scanning,
    Resolving,

    /// <summary>① 商品JSON（全商品）。ここが終われば検索も統計も成立する。</summary>
    FetchingJson,

    /// <summary>② 商品ページHTML（全商品）。対応アバターの節と説明文。</summary>
    FetchingHtml,

    /// <summary>
    /// ③ 対応アバターの検出。
    ///
    /// ①②の後に置けるのは、判定の材料（category）が商品JSONに入っているから。
    /// 取り込んだ直後は説明文もタグも手元にあるので、ほとんどが通信なしで済む。
    /// </summary>
    Detecting,

    /// <summary>④ 1枚目の画像（全商品）。1枚あれば一覧のカードは完成する。</summary>
    FetchingThumbnails,

    /// <summary>⑤ 残りの画像（商品ごと）。ホバーのギャラリーと商品ページで要る。</summary>
    FetchingGallery,

    /// <summary>⑥ ショップのアイコン。無くても名前で用は足りるので最後。</summary>
    FetchingShopIcons,
}

public sealed class ImportProgress
{
    public required ImportPhase Phase { get; init; }

    public int Current { get; init; }

    public int Total { get; init; }

    /// <summary>
    /// 段の中の小さな段（検出の「見つかった商品を確かめています」など）。件数の前に出す。
    /// 件数だけでは何を数えているかが読めず、検出の「346 / 599」がアバターの数に見えた。
    /// </summary>
    public string? Step { get; init; }

    public string? Detail { get; init; }
}

public sealed class ImportSummary
{
    public int FilesScanned { get; init; }

    /// <summary>実際にSHA-256を計算した件数。キャッシュの効き具合がここに出る。</summary>
    public int FilesHashed { get; init; }

    public int FilesReusedFromCache { get; init; }

    public int FilesExcluded { get; init; }

    /// <summary>既にitemが持っていたので未確定へ流さなかった件数。手作業で紐付けたファイルがここに入る。</summary>
    public int FilesAlreadyOwned { get; init; }

    /// <summary>アーカイブの展開先とみなして取り込まなかったファイル数。</summary>
    public int FilesSkippedAsUnpacked { get; init; }

    /// <summary>
    /// 権限などで読めず、取り込めなかったファイル数（E4）。**0 でないときだけ画面に出す。**
    /// 黙って飛ばしていたので、取り込んだつもりの物が入っていないことに気付けなかった。
    /// </summary>
    public int FilesUnreadable { get; init; }

    /// <summary>見つかった展開先フォルダ。削除機能に渡す候補になる。</summary>
    public IReadOnlyList<UnpackedFolder> UnpackedFolders { get; init; } = [];

    public int ItemsAdded { get; init; }

    public int ItemsAlreadyKnown { get; init; }

    public int UnresolvedFiles { get; init; }

    public int NotFound { get; init; }

    public int TemporaryFailures { get; init; }

    public int ImagesDownloaded { get; init; }

    /// <summary>③ 検出が対応アバターを書き込んだ商品数。</summary>
    public int AvatarItemsUpdated { get; init; }

    /// <summary>③ 検出で分かったアバターの数。</summary>
    public int AvatarsFound { get; init; }

    /// <summary>③ 検出が途中で止まった理由。止まっても取り込み自体は成立している。</summary>
    public string? AvatarDetectError { get; init; }

    /// <summary>
    /// ③ 検出を走らせたか。**BOOTHから1件も取らなかった取り込みでは走らない**
    /// （既にある商品にファイルを足しただけなど）。検出が読むのはBOOTHから取った説明文・タグ・種類名なので、
    /// 新しく読むものが無い。走らなかったのに「見つかりませんでした」と言わないために持つ。
    /// </summary>
    public bool AvatarDetectRan { get; init; }
}

public interface IImportPipeline
{
    Task<ImportSummary> RunAsync(
        IReadOnlyList<string> folders,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>走らせている最中にも対象を足せる版。呼ぶ側が作業集合を握る。</summary>
    Task<ImportSummary> RunAsync(
        ImportWorkSet work,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 取り込みの3フェーズ（走査 → BoothID解決 → BOOTH取得）。
///
/// 中断への備えは2段構え。ハッシュ計算の結果は途中でも定期的に保存し、
/// 取得済みのitemは1件ごとに保存するので、閉じて再実行すれば続きから進む。
/// </summary>
public sealed class ImportPipeline : IImportPipeline
{
    private const int ScanCacheSaveInterval = 50;

    private readonly DataStore _store;
    private readonly IBoothClient _client;
    private readonly ImagePipeline _images;
    private readonly Func<AppSettings> _currentSettings;
    private readonly FolderScanner _scanner = new();

    /// <summary>③ 対応アバターの検出。渡されなければその段を飛ばす。</summary>
    private readonly Services.IAvatarService? _avatars;

    /// <summary>unitypackage の中身を裏で読む。渡されなければ読まない（使うときに zip を解く）。</summary>
    private readonly Services.UnityPackageCatalog? _unityPackages;

    /// <summary>ドライブ文字と通し番号の組を控える。渡されなければ控えない。</summary>
    private readonly Services.VolumeTable? _volumes;

    public ImportPipeline(
        DataStore store,
        IBoothClient client,
        ImagePipeline images,
        AppSettings? settings = null,
        Services.IAvatarService? avatars = null,
        Services.UnityPackageCatalog? unityPackages = null)
        : this(store, client, images, SettingsSource.Fixed(settings), avatars, unityPackages)
    {
    }

    /// <param name="currentSettings">使うたびに今の設定を返すもの（<see cref="SettingsSource"/>）。</param>
    public ImportPipeline(
        DataStore store,
        IBoothClient client,
        ImagePipeline images,
        Func<AppSettings> currentSettings,
        Services.IAvatarService? avatars = null,
        Services.UnityPackageCatalog? unityPackages = null,
        Services.VolumeTable? volumes = null)
    {
        _store = store;
        _client = client;
        _images = images;
        _currentSettings = currentSettings;
        _avatars = avatars;
        _unityPackages = unityPackages;
        _volumes = volumes;
    }

    /// <summary>今の設定。**抱えずに毎回読む。**</summary>
    private AppSettings _settings => _currentSettings();

    public Task<ImportSummary> RunAsync(
        IReadOnlyList<string> folders,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => RunAsync(new ImportWorkSet(folders), progress, cancellationToken);

    /// <summary>
    /// 対象がまだ残っている限り回り続ける。
    ///
    /// 走っている最中に <see cref="ImportWorkSet.Add"/> されたフォルダは、
    /// 今の周回が終わったところで次の周回として拾う。**押し直す必要がない。**
    /// パイプラインが1本のままなので、取得の順序も進捗の出どころも1つに保てる。
    ///
    /// まとめの件数は周回をまたいで足し合わせる。ユーザにとっては
    /// 「1回の取り込み」なので、途中で足したぶんも同じ数字に入っていてほしい。
    /// </summary>
    public async Task<ImportSummary> RunAsync(
        ImportWorkSet work,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _store.Paths.EnsureCreated();

        var scanCache = new ScanCacheIndex(_store.ScanCache.Load());
        var exclusions = new ExclusionFilter(_store.Excluded.Load());

        var totals = new ImportTotals();

        // 未確定の一覧は取り込みの最中に人も書く。書くたびに、前に書いた物と今の物を比べて人の変更を残す（UnresolvedMerge）。
        // 外付けを外している取り込み元の下の物は、見られなかっただけなので引き継ぐ
        var unresolvedBase = _store.Unresolved.Load();
        var offlineTargets = new List<string>();

        // unitypackage の中身を裏で読む（2026-09-13 ユーザ判断）。問い合わせは1本ずつ1.5秒空けるので、その間 CPU とディスクは空いている。
        // 前の取り込みで読み残した物（中断など）も、最初の周回で一緒に拾う
        var unityPending = _unityPackages is null ? null : await _unityPackages.FindPendingAsync(cancellationToken);
        var unityWork = new List<Task>();

        // 周回の外で取る画像の列（④1枚目 ⑤残り ⑥ショップのアイコン）。周回をまたいで持ち越す
        var images = new ImageQueue();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (work.TakePending() is not { Count: > 0 } folders)
            {
                if (images.IsEmpty)
                {
                    break;
                }

                // 画像は1件ずつ取り、そのたびに積まれたフォルダが無いかを見る。
                // 積まれていれば、その場で次の周回（走査〜①②③）へ移る（U5）
                totals.AddImages(await DrainImagesAsync(images, work, progress, cancellationToken));
                continue;
            }

            // 既に商品へ紐付けたフォルダの中は見に行かない。
            // 「管理済み」なので未確定へ流す必要が無く、容量も別途数えている。
            // 周回ごとに読み直すのは、前の周回で増えた商品を次の周回が知っている必要があるため。
            // 外した印も商品のJSONの中にあるので、同じ読み込みから引く
            var (registered, owned, detached) = await LoadOwnedAsync(cancellationToken);

            // この周回で記録するパスは今のドライブ文字で書かれるので、文字と通し番号の組はここで確か（ユーザ判断 2026-09-14）
            await RecordVolumesAsync(folders, cancellationToken);
            offlineTargets.AddRange(folders.Where(UnresolvedMerge.IsOnMissingVolume));

            var scan = ScanFolders(folders, exclusions, registered, progress, cancellationToken);
            var resolution = await ResolveAsync(
                scan.Files, scanCache, exclusions, detached, owned, progress, cancellationToken);
            await _store.ScanCache.SaveAsync(scanCache.ToList(), cancellationToken);

            // 読むのは item に触らないので、①と同時に進めてよい。①に着くのを遅らせないよう、ここでは待たない
            Task? unityReading = null;
            var unityItemIds = new List<string>();
            if (_unityPackages is { } catalog)
            {
                var unityFiles = resolution.FilesByItemId.Values.SelectMany(list => list).ToList();
                unityItemIds.AddRange(resolution.FilesByItemId.Keys);
                if (unityPending is { } pending)
                {
                    unityFiles.AddRange(pending.Files);
                    unityItemIds.AddRange(pending.ItemIds);
                    unityPending = null;
                }

                unityReading = catalog.ReadAsync(unityFiles, cancellationToken);
            }

            // 未確定は積み上げる。前の周回で残ったものを消してはいけない
            totals.Unresolved.AddRange(resolution.Unresolved);
            unresolvedBase = await SaveUnresolvedAsync(totals.Unresolved, unresolvedBase, offlineTargets, cancellationToken);

            var fetchResult = await FetchAsync(resolution.FilesByItemId, work, progress, cancellationToken);

            // 入り先を item に写すのは①の後。item の手元のファイルは①も書き、錠が無いので、重なると片方の書き込みが消える。
            // 読み終わっていなければ、読み終わったところで写す（画像の段は待たせない）
            if (unityReading is not null)
            {
                unityWork.Add(ApplyUnityPackagesAsync(unityReading, unityItemIds, cancellationToken));
            }

            // BOOTHに無かったものも未確定へ。ここで落とすと手元から消える
            if (fetchResult.NotFoundFiles.Count > 0)
            {
                totals.Unresolved.AddRange(fetchResult.NotFoundFiles);
                unresolvedBase = await SaveUnresolvedAsync(totals.Unresolved, unresolvedBase, offlineTargets, cancellationToken);
            }

            totals.Add(scan, resolution, fetchResult);

            // この周回の1枚目は、今の列に残っている画像より先に取る。
            // 積んだ物が早く一覧に出ることの方が、前の周回の2枚目より先に要る
            images.Enqueue(fetchResult.WithImages, fetchResult.ShopIcons);

            // 画像の問い合わせの見込み（U1）。手元にある絵は取りに行かないので、多めに出ることがある
            work.PlanRequests(images: fetchResult.WithImages.Sum(item => item.Booth.Images.Count) + fetchResult.ShopIcons.Count);
        }

        // 取り込みが終わったと言うのは、裏で読んでいた unitypackage も書き終えてから
        await Task.WhenAll(unityWork);

        // 最後まで来たので記録は要らない。残すと次の起動で「中断した」と嘘をつく
        await _store.ImportState.SaveAsync(new ImportState(), cancellationToken);

        return totals.ToSummary();
    }

    /// <summary>未確定の一覧を、錠の中で人の変更と合わせて書く（技術的負債 1-2・1-3）。書いた物を次の比べる元にする。</summary>
    private Task<List<UnresolvedFile>> SaveUnresolvedAsync(
        IReadOnlyList<UnresolvedFile> found,
        IReadOnlyList<UnresolvedFile> lastWritten,
        IReadOnlyList<string> offlineTargets,
        CancellationToken cancellationToken)
        => _store.Unresolved.UpdateAsync(
            current => UnresolvedMerge.ForImport(current, lastWritten, found, new RegisteredFolderSet(offlineTargets)),
            cancellationToken);

    /// <summary>控えられなくても取り込みは止めない（次に開いたフォルダビューで控え直す）。</summary>
    private async Task RecordVolumesAsync(IReadOnlyList<string> folders, CancellationToken cancellationToken)
    {
        if (_volumes is null)
        {
            return;
        }

        try
        {
            await _volumes.RecordAsync(folders, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or System.Text.Json.JsonException)
        {
            Diagnostics.AppLog.Error("取り込みの裏の作業", exception);
        }
    }

    /// <summary>読み終わるのを待って、入り先を item に写す。読めなくても取り込みは止めない（次の取り込みで読み直す）。</summary>
    private async Task ApplyUnityPackagesAsync(Task reading, IReadOnlyList<string> itemIds, CancellationToken cancellationToken)
    {
        try
        {
            await reading;
            await _unityPackages!.ApplyAsync(itemIds, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or System.Text.Json.JsonException)
        {
            Diagnostics.AppLog.Error("取り込みの裏の作業", exception);
        }
    }

    /// <summary>
    /// 検出の進み具合を、取り込みの進み具合として流し直す。
    ///
    /// 検出は取り込みの1段なので、画面には1本の進捗として見えていてほしい。
    /// 画面が2種類の進捗を混ぜて出すより、ここで形を揃える方が食い違いようがない。
    /// </summary>
    private sealed class DetectProgressAdapter(IProgress<ImportProgress>? inner)
        : IProgress<Services.AvatarDetectProgress>
    {
        public void Report(Services.AvatarDetectProgress value)
            => inner?.Report(new ImportProgress
            {
                Phase = ImportPhase.Detecting,
                Current = value.Done,
                Total = value.Total,
                // 小さな段と、今見ている商品の名前を分けて渡す。名前が無いときに段の名前を
                // 代わりに出すと、件数の前の欄と同じ文が2回並ぶ
                Step = value.Phase,
                Detail = value.Current,
            });
    }

    private static void Report(
        IProgress<ImportProgress>? progress,
        ImportPhase phase,
        int current,
        int total,
        string? detail)
        => progress?.Report(new ImportProgress
        {
            Phase = phase,
            Current = current,
            Total = total,
            Detail = detail,
        });

    /// <summary>周回をまたいだ合計。1回の取り込みとして1つのまとめに畳む。</summary>
    private sealed class ImportTotals
    {
        public List<UnresolvedFile> Unresolved { get; } = [];

        private readonly List<UnpackedFolder> _unpacked = [];
        private int _scanned;
        private int _skippedUnpacked;
        private int _unreadable;
        private int _hashed;
        private int _reused;
        private int _excluded;
        private int _alreadyOwned;
        private int _added;
        private int _alreadyKnown;
        private int _notFound;
        private int _temporaryFailures;
        private int _imagesDownloaded;
        private int _avatarItemsUpdated;
        private int _avatarsFound;
        private string? _avatarDetectError;
        private bool _avatarDetectRan;

        public void Add(ScanOutcome scan, ResolutionResult resolution, FetchResult fetch)
        {
            _scanned += scan.Files.Count;
            _unpacked.AddRange(scan.UnpackedFolders);
            _skippedUnpacked += scan.SkippedInsideUnpackedFolders;
            _unreadable += scan.Unreadable;

            _hashed += resolution.Hashed;
            _reused += resolution.ReusedFromCache;
            _excluded += resolution.Excluded;
            _alreadyOwned += resolution.AlreadyOwned;

            _added += fetch.Added;
            _alreadyKnown += fetch.AlreadyKnown;
            _notFound += fetch.NotFound;
            _temporaryFailures += fetch.TemporaryFailures;

            // 検出は周回ごとに走るので足し合わせる。理由は最後のものを残す
            _avatarItemsUpdated += fetch.AvatarItemsUpdated;
            _avatarsFound += fetch.AvatarsFound;
            _avatarDetectError = fetch.AvatarDetectError ?? _avatarDetectError;
            _avatarDetectRan |= fetch.AvatarDetectRan;
        }

        /// <summary>周回の外で取った画像の数（U5）。</summary>
        public void AddImages(int downloaded) => _imagesDownloaded += downloaded;

        public ImportSummary ToSummary() => new()
        {
            FilesScanned = _scanned,
            UnpackedFolders = _unpacked,
            FilesSkippedAsUnpacked = _skippedUnpacked,
            FilesUnreadable = _unreadable,
            FilesHashed = _hashed,
            FilesReusedFromCache = _reused,
            FilesExcluded = _excluded,
            FilesAlreadyOwned = _alreadyOwned,
            UnresolvedFiles = Unresolved.Count,
            ItemsAdded = _added,
            ItemsAlreadyKnown = _alreadyKnown,
            NotFound = _notFound,
            TemporaryFailures = _temporaryFailures,
            ImagesDownloaded = _imagesDownloaded,
            AvatarItemsUpdated = _avatarItemsUpdated,
            AvatarsFound = _avatarsFound,
            AvatarDetectError = _avatarDetectError,
            AvatarDetectRan = _avatarDetectRan,
        };
    }

    /// <summary>
    /// 既に管理下にあるものを集める。登録済みフォルダと、itemが持っているファイルのハッシュ。
    /// ついでに登録済みフォルダの中身を数え直して保存する
    /// （数えるのは列挙だけでハッシュは計算しないので速い）。
    /// </summary>
    private async Task<(RegisteredFolderSet Registered, IReadOnlySet<string> OwnedHashes, DetachedIndex Detached)> LoadOwnedAsync(
        CancellationToken cancellationToken)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var paths = new List<string>();
        var notifications = _store.Notifications.Load();
        var notificationCountBefore = notifications.Count;

        var owned = loaded.Items
            .SelectMany(item => item.Local.OwnedFiles)
            .Select(file => file.Hash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in loaded.Items.Where(item => item.Local.LocalFolders.Count > 0))
        {
            var refreshed = new List<LocalFolderRecord>();
            var changed = false;

            foreach (var folder in item.Local.LocalFolders)
            {
                if (!Directory.Exists(folder.Path))
                {
                    // 見つからないものは登録として残すが、スキャンの除外には使わない
                    refreshed.Add(folder);
                    continue;
                }

                paths.Add(folder.Path);

                // zipが手に入っていれば、フォルダ登録は役目を終えている。
                // 黙っていると容量が二重に乗ったままなので知らせる。
                if (RegisteredFolderSet.FindArchiveFor(folder.Path) is { } archive)
                {
                    NoteArchiveFound(notifications, item, folder.Path, archive);
                }

                var (count, bytes) = RegisteredFolderSet.Measure(folder.Path);
                if (count != folder.FileCount || bytes != folder.TotalBytes || folder.LastSeenAt is null)
                {
                    changed = true;
                }

                refreshed.Add(folder with
                {
                    FileCount = count,
                    TotalBytes = bytes,
                    LastSeenAt = DateTimeOffset.Now,
                });
            }

            if (changed)
            {
                // 全件を先に読んでから、フォルダを1つずつ測って回る。測るのに時間がかかるので、
                // 書く頃には写しが古い。取り込みが持つ項目だけを名指しする
                await _store.Items.SaveLocalAsync(
                    item.Id,
                    item.Local with { LocalFolders = refreshed },
                    LocalOwners.Import,
                    cancellationToken: cancellationToken);
            }
        }

        if (notifications.Count != notificationCountBefore)
        {
            await _store.Notifications.SaveAsync(notifications, cancellationToken);
        }

        return (new RegisteredFolderSet(paths), owned, DetachedIndex.From(loaded.Items));
    }

    /// <summary>
    /// 「登録したフォルダのzipが手に入った」を要確認へ書く。
    /// 同じフォルダで何度も出さないよう、未読の同種があれば足さない。
    /// </summary>
    private static void NoteArchiveFound(
        List<NotificationRecord> notifications,
        ItemRecord item,
        string folderPath,
        string archivePath)
    {
        var id = $"archive-found:{folderPath}";
        if (notifications.Any(entry => entry.Id == id && !entry.IsRead))
        {
            return;
        }

        notifications.Add(new NotificationRecord
        {
            Id = id,
            Kind = NotificationKind.ArchiveFoundForFolder,
            ItemId = item.Id,
            Title = item.DisplayName,
            Detail = $"展開先「{Path.GetFileName(folderPath)}」と {Path.GetFileName(archivePath)}",
            CreatedAt = DateTimeOffset.Now,
            IsStrong = true,
        });
    }

    private ScanOutcome ScanFolders(
        IReadOnlyList<string> folders,
        ExclusionFilter exclusions,
        RegisteredFolderSet registered,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var scanned = new List<ScannedFile>();
        var unpacked = new List<UnpackedFolder>();
        var skippedUnpacked = 0;
        var unreadable = 0;

        foreach (var folder in folders)
        {
            var result = _scanner.Scan(folder, cancellationToken);
            unpacked.AddRange(result.UnpackedFolders);
            skippedUnpacked += result.SkippedInsideUnpackedFolders;
            unreadable += result.Unreadable;

            foreach (var file in result.Files)
            {
                // 除外済みのパスはここで弾く。ハッシュ計算にすら進ませない。
                if (exclusions.IsExcludedByPath(file.Path))
                {
                    continue;
                }

                // 既に商品へ紐付けたフォルダの中身も同じく飛ばす
                if (registered.Contains(file.Path))
                {
                    continue;
                }

                scanned.Add(file);
                progress?.Report(new ImportProgress
                {
                    Phase = ImportPhase.Scanning,
                    Current = scanned.Count,
                    Detail = file.Path,
                });
            }
        }

        return new ScanOutcome
        {
            Files = scanned,
            UnpackedFolders = unpacked,
            SkippedInsideUnpackedFolders = skippedUnpacked,
            Unreadable = unreadable,
        };
    }

    private sealed class ScanOutcome
    {
        public required List<ScannedFile> Files { get; init; }

        public required List<UnpackedFolder> UnpackedFolders { get; init; }

        public int SkippedInsideUnpackedFolders { get; init; }

        public int Unreadable { get; init; }
    }

    private async Task<ResolutionResult> ResolveAsync(
        List<ScannedFile> scanned,
        ScanCacheIndex scanCache,
        ExclusionFilter exclusions,
        DetachedIndex detached,
        IReadOnlySet<string> owned,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var filesByItemId = new Dictionary<string, List<LocalFileRecord>>(StringComparer.Ordinal);
        var unresolved = new List<UnresolvedFile>();
        var hashed = 0;
        var reused = 0;
        var excluded = 0;
        var alreadyOwned = 0;
        var processed = 0;

        foreach (var file in scanned)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new ImportProgress
            {
                Phase = ImportPhase.Resolving,
                Current = ++processed,
                Total = scanned.Count,
                Detail = Path.GetFileName(file.Path),
            });

            string hash;
            if (scanCache.TryGetHash(file.Path, file.SizeBytes, file.ModifiedAtUtc, out var cachedHash))
            {
                hash = cachedHash;
                reused++;
            }
            else
            {
                try
                {
                    hash = await FileHasher.ComputeSha256Async(file.Path, cancellationToken);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                scanCache.Set(file.Path, file.SizeBytes, file.ModifiedAtUtc, hash);
                hashed++;

                if (hashed % ScanCacheSaveInterval == 0)
                {
                    await _store.ScanCache.SaveAsync(scanCache.ToList(), cancellationToken);
                }
            }

            // 移動・改名された除外ファイルはパスでは弾けないので、ハッシュで最終判定する。
            if (exclusions.IsExcludedByHash(hash))
            {
                excluded++;
                continue;
            }

            var clues = InspectFile(file, out var contents);
            var zone = ZoneIdentifierReader.Read(file.Path);

            // 商品ページで外したものは候補から落とす。
            // 外す操作が要るのは手掛かりが間違っている場合なので、
            // ここで落とさないと次の取り込みで同じ商品へ戻ってしまう。
            var candidates = IdResolver.Resolve(zone, clues)
                .Where(candidate => !detached.IsDetached(hash, candidate.ItemId))
                .ToList();

            if (candidates.Count == 1)
            {
                var record = new LocalFileRecord
                {
                    Hash = hash,
                    Paths = [file.Path],
                    SizeBytes = file.SizeBytes,
                    Contents = contents,
                };

                if (!filesByItemId.TryGetValue(candidates[0].ItemId, out var list))
                {
                    list = [];
                    filesByItemId[candidates[0].ItemId] = list;
                }

                list.Add(record);
            }
            else if (owned.Contains(hash))
            {
                // 未確定画面で手作業で紐付けたファイル。手掛かりからは決まらないので、
                // 毎回ここへ落ちてくる。既にitemが持っていると分かっているものを
                // 作業として出し直すのは嘘なので、黙って飛ばす。
                alreadyOwned++;
            }
            else
            {
                unresolved.Add(new UnresolvedFile
                {
                    Hash = hash,
                    Paths = [file.Path],
                    SizeBytes = file.SizeBytes,
                    ModifiedAtUtc = file.ModifiedAtUtc,
                    FirstSeenAt = DateTimeOffset.Now,
                    Contents = contents,
                    ZoneHostUrl = zone.HostUrl,
                    ZoneReferrerUrl = zone.ReferrerUrl,
                    CandidateItemIds = candidates.Select(candidate => candidate.ItemId).ToList(),
                });
            }
        }

        return new ResolutionResult
        {
            FilesByItemId = filesByItemId,
            Unresolved = unresolved,
            Hashed = hashed,
            ReusedFromCache = reused,
            Excluded = excluded,
            AlreadyOwned = alreadyOwned,
        };
    }

    /// <summary>ZIPだけ中身を読む。それ以外の形式は Zone.Identifier だけが手掛かりになる。</summary>
    private static IReadOnlyList<BoothClue> InspectFile(ScannedFile file, out IReadOnlyList<string> contents)
    {
        contents = [];
        if (!file.IsArchive)
        {
            return [];
        }

        try
        {
            var inspection = ZipInspector.Inspect(file.Path);
            contents = inspection.Summary.Files.Select(entry => entry.RelativePath).ToList();
            return inspection.Clues;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// 梯子を段ごとに降りる。**商品ごとに全部取るのではなく、段ごとに全商品を回る。**
    ///
    /// 理由は2つ。
    ///
    /// ひとつは**待ち時間を作業時間に変える**こと。実データでは1商品あたり
    /// JSON 1本・HTML 1本・画像 9.5本で、画像が全体の76%を占める。
    /// 商品ごとに取ると100商品で31分かかり、その間ずっと何も見えない。
    /// ①②だけなら5分で、そこには検索・絞り込み・統計に要るものが全部揃っている。
    ///
    /// もうひとつは**部分的な知識で作業を始めさせない**こと。対応アバターを選ぶとき、
    /// 候補の材料にはvariationの名前が入る。100商品のうち1商品しか知らない状態で
    /// 選ばせると候補が出揃わず、後から選び直すことになる。
    /// </summary>
    private async Task<FetchResult> FetchAsync(
        Dictionary<string, List<LocalFileRecord>> filesByItemId,
        ImportWorkSet work,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var added = 0;
        var alreadyKnown = 0;
        var notFound = 0;
        var notFoundFiles = new List<UnresolvedFile>();
        var temporaryFailures = 0;
        var avatarItemsUpdated = 0;
        var avatarsFound = 0;
        string? avatarDetectError = null;
        var avatarDetectRan = false;

        // 手元にある商品は通信が要らない。ファイルを足すだけなので、段に入る前に片付ける
        var pending = new List<(string ItemId, List<LocalFileRecord> Files)>();

        // 取得済みなのに説明が無い商品。①の後・②の前で閉じた取り込みの続き（U9）
        var withoutPage = new List<ItemRecord>();

        foreach (var (itemId, discovered) in filesByItemId)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var existing = await _store.Items.LoadAsync(itemId, cancellationToken);
            if (existing is null)
            {
                pending.Add((itemId, discovered));
                continue;
            }

            // 取得済みのitemは触らない。中断して再実行した時に、ここが「続きから」を成立させる。
            await _store.Items.SaveLocalAsync(
                itemId,
                existing.Local with { LocalFiles = LocalFileMerger.Merge(existing.Local.LocalFiles, discovered) },
                LocalOwners.Import,
                cancellationToken: cancellationToken);

            alreadyKnown++;

            // 販売終了の商品はページも無いので戻さない（取りに行っても毎回失敗するだけ）
            if (!existing.Local.IsDelisted && !File.Exists(_store.Paths.ItemHtmlFile(itemId)))
            {
                withoutPage.Add(existing);
            }
        }

        // 残り時間の見込み（U1）。①は新しい商品の数、②はそれに説明の無い取得済みを足した数
        work.PlanRequests(json: pending.Count, pages: pending.Count + withoutPage.Count);

        // ── ① 商品JSON（全商品）。ここが終われば検索も統計も成立する ──
        //
        // 段ごとに優先度を切り替える。人が押した操作はこれより上なので、
        // 取り込みの最中でも「このIDで確認」は待たされない
        using var metadataPriority = BoothClient.Prioritize(BoothPriority.Metadata);

        var fetched = new List<ItemRecord>();
        var shopIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var done = 0;

        foreach (var (itemId, discovered) in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, ImportPhase.FetchingJson, ++done, pending.Count, itemId);

            var jsonResult = await _client.GetItemJsonAsync(itemId, cancellationToken);
            work.PlanRequests(json: -1);

            if (jsonResult.Status == BoothFetchStatus.NotFound)
            {
                // BOOTHに無い＝ファイルが無かったことにはならない。
                // 買っていて手元にあるものなので、未確定へ戻して人に決めてもらう。
                // 別のIDで再公開されていることもあり、そのときは候補検索が拾える。
                //
                // 1回の404で流すのは、**こちらが「非公開だ」と確定する必要がないから。**
                // 一時的な障害だったなら次の取り込みで普通に確定するだけで、何も失われない
                notFound++;
                work.PlanRequests(pages: -1); // ②へは進まない
                notFoundFiles.AddRange(discovered.Select(file => ToUnresolved(file, itemId)));
                continue;
            }

            if (!jsonResult.IsSuccess || jsonResult.Value is null)
            {
                temporaryFailures++;
                work.PlanRequests(pages: -1); // ②へは進まない
                continue;
            }

            var item = new ItemRecord
            {
                Id = itemId,
                Booth = BoothItemMapper.Map(jsonResult.Value, DateTimeOffset.Now),
                Local = new LocalBlock
                {
                    LocalFiles = LocalFileMerger.Merge([], discovered),
                    NotifyOnUpdate = _settings.NotifyOnUpdateByDefault,
                    LastFetchedAt = DateTimeOffset.Now,
                    NextFetchDueAt = NextFetchDue(itemId),
                },
            };

            // 1件ずつ保存する。ここで中断しても、取れたぶんはそのまま残る
            await _store.Items.SaveAsync(item, cancellationToken);

            // 「追加」の足跡。**itemのJSONには書かない**（足跡で埋めないため）。
            // 既にある商品には打てないので、そちらは「不明」のまま残る——
            // 後から作った時刻を騙るより、無いと言う方がよい
            await StampAddedAsync(itemId, cancellationToken);

            fetched.Add(item);
            added++;

            // 検索と件数にはもう出してよい。編集は③が済むまで待たせる（U8・U10）
            work.NoteAdded(itemId);

            // どこまで進んだかを残す。閉じた時に何件残っていたかをユーザは覚えていない。
            // ①の途中で閉じると「IDは分かったがまだ取得していない商品」の一覧は消えるので、
            // 件数だけでも残しておかないと、中断したこと自体が黙って起きる
            await _store.ImportState.SaveAsync(
                new ImportState { Done = fetched.Count, Total = pending.Count, StoppedAt = DateTimeOffset.Now },
                cancellationToken);

            // アイコンのURLは商品JSONにしか入っていないので、ここで控えて⑥で取りに行く
            if (item.Booth.Shop is { ThumbnailUrl.Length: > 0 } shop)
            {
                shopIcons[shop.Subdomain] = shop.ThumbnailUrl;
            }
        }

        // ── ② 商品ページHTML（全商品）。対応アバターの節と説明文 ──
        //
        // 取得済みでも説明が無い商品はここへ戻す。②③の途中で閉じてから押し直すと、
        // ①が済んだ商品は「取得済み」で飛ばされ、説明が⑦の取り直しの日まで埋まらなかった（U9）
        var pages = fetched.Concat(withoutPage).ToList();
        done = 0;

        for (var index = 0; index < pages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = pages[index];
            Report(progress, ImportPhase.FetchingHtml, ++done, pages.Count, item.Id);

            var htmlResult = await _client.GetItemHtmlAsync(item.Id, cancellationToken);

            // 見込みは問い合わせが済んでから減らす（①と揃える）。取っている最中の1件は残りに数える
            work.PlanRequests(pages: -1);
            if (!htmlResult.IsSuccess || htmlResult.Value is null)
            {
                // 節が取れなくても商品自体は使える。次の段へ進む
                continue;
            }

            var extraction = H2SectionExtractor.Extract(htmlResult.Value);

            pages[index] = item = item with { Booth = item.Booth with { H2Sections = extraction.Sections } };
            await _store.Items.SaveLocalAsync(
                item.Id,
                item.Local,
                [],
                item.Booth,
                cancellationToken);

            // 説明が無い商品でも空のファイルを置く。置かないと「まだ取っていない」と見分けが付かず、
            // 取り込むたびに取り直しに来る
            await _store.Items.SaveDescriptionHtmlAsync(item.Id, extraction.DescriptionHtml ?? string.Empty, cancellationToken);
        }

        // ── ③ 対応アバターの検出 ──
        //
        // 画像より先に置く。対応アバターを選ぶのは人の作業で、その候補が出揃っている
        // ことの方が、絵が見えていることより先に要る。
        // 通信が要るのは「対応表明で名前が出たが、手元に持っていないアバター」だけなので、
        // ここを④の前に置いても待ちはほとんど伸びない。
        if (_avatars is not null && pages.Count > 0)
        {
            using var detectPriority = BoothClient.Prioritize(BoothPriority.Detection);
            avatarDetectRan = true;

            try
            {
                var detected = await _avatars.DetectAsync(new DetectProgressAdapter(progress), cancellationToken);
                avatarItemsUpdated = detected.ItemsUpdated;
                avatarsFound = detected.AvatarsFound;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 取り込みは成立している。検出はアバター画面からやり直せるので、止めずに知らせるだけ
                avatarDetectError = exception.Message;
            }
        }

        // ③の段は終わった（失敗しても、検出を使わない設定でも）。この周回の商品を編集に出す
        work.NoteDetectionDone(fetched.Select(item => item.Id));

        // ④1枚目 ⑤残りの画像 ⑥ショップのアイコン は、周回の外の画像の列で取る（U5・DrainImagesAsync）。
        // 周回の中で取り切ると、その間に積まれたフォルダは⑥が終わるまで何も始まらない。
        // 画像を取らない設定なら何も積まない（梯子は①②③で終わり。検索・絞り込み・統計は JSON だけで成立する）
        var withImages = _images.SavesImages
            ? pages.Where(item => item.Booth.Images.Count > 0).ToList()
            : [];

        return new FetchResult
        {
            Added = added,
            AlreadyKnown = alreadyKnown,
            NotFound = notFound,
            NotFoundFiles = notFoundFiles,
            TemporaryFailures = temporaryFailures,
            WithImages = withImages,
            ShopIcons = _images.SavesImages ? shopIcons : new Dictionary<string, string>(),
            AvatarItemsUpdated = avatarItemsUpdated,
            AvatarsFound = avatarsFound,
            AvatarDetectError = avatarDetectError,
            AvatarDetectRan = avatarDetectRan,
        };
    }

    /// <summary>
    /// 「手元に入った」時刻を <c>recent.json</c> に打つ。
    ///
    /// **itemのJSONには書かない。**足跡（追加・使った・閲覧）は
    /// 人が入力したものと混ぜない方針（<see cref="Services.RecentActivity"/>）。
    ///
    /// 失敗しても取り込みは止めない。足跡が1つ欠けても商品の記録は無事。
    /// </summary>
    private async Task StampAddedAsync(string itemId, CancellationToken cancellationToken)
    {
        try
        {
            var log = _store.Recent.Load();
            var updated = Services.RecentActivity.Touch(
                log.Entries, itemId, Services.RecentKind.Added, DateTimeOffset.Now);

            await _store.Recent.SaveAsync(
                new Services.RecentLog { Entries = updated },
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// 次回の取得予定。全itemが同じ日に期限切れにならないよう、商品IDから決まるばらつきを足す。
    /// 乱数ではなくIDから決めているのは、同じitemなら何度計算しても同じ日になるようにするため。
    /// </summary>
    private DateTimeOffset NextFetchDue(string itemId)
    {
        var jitterDays = _settings.RefreshJitterDays;
        var offset = jitterDays <= 0
            ? 0
            : Math.Abs(itemId.GetHashCode(StringComparison.Ordinal)) % ((jitterDays * 2) + 1) - jitterDays;

        return DateTimeOffset.Now.AddDays(_settings.RefreshIntervalDays + offset);
    }

    private sealed class ResolutionResult
    {
        public required Dictionary<string, List<LocalFileRecord>> FilesByItemId { get; init; }

        public required List<UnresolvedFile> Unresolved { get; init; }

        public int Hashed { get; init; }

        public int ReusedFromCache { get; init; }

        public int Excluded { get; init; }

        /// <summary>既にitemが持っていたので未確定へ流さなかった件数。</summary>
        public int AlreadyOwned { get; init; }
    }

    /// <summary>
    /// 商品へ紐付けたファイルを、未確定のファイルへ戻す。
    ///
    /// 手掛かりから決まった商品IDは<b>候補として載せる</b>。
    /// 「このファイルは 1234567 を指しているが、BOOTHには無い」と読める形にするため。
    /// </summary>
    private static UnresolvedFile ToUnresolved(LocalFileRecord file, string itemId)
    {
        var modified = DateTimeOffset.Now;
        var path = file.Paths.FirstOrDefault();

        if (path is not null)
        {
            try
            {
                modified = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 日時が読めなくても未確定には出したいので、今の時刻で通す
            }
        }

        return new UnresolvedFile
        {
            Hash = file.Hash,
            Paths = file.Paths,
            SizeBytes = file.SizeBytes,
            ModifiedAtUtc = modified,
            FirstSeenAt = DateTimeOffset.Now,
            Contents = file.Contents,
            CandidateItemIds = [itemId],
        };
    }

    /// <summary>
    /// 画像の列を1件ずつ取る（U5）。④1枚目 → ⑤残り → ⑥ショップのアイコン の順。
    ///
    /// 1件ごとに <paramref name="work"/> を見て、積まれたフォルダがあればその場で戻る。
    /// 残りの問い合わせの見込み（U1）もここで減らす。
    /// 周回の中で取り切っていた頃は、積んだ分は⑥が終わるまで何も始まらなかった
    /// （設計では「積まれたら①②が最優先」と決めてあったのに、実装が合っていなかった）。
    /// 優先度は段ごとに切り替える。人が押した通信はどの段よりも上なので待たされない。
    /// </summary>
    /// <returns>落とせた画像の枚数。</returns>
    private async Task<int> DrainImagesAsync(
        ImageQueue queue,
        ImportWorkSet work,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var downloaded = 0;

        while (!queue.IsEmpty && !work.HasPending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ④ 1枚目。一覧のカードは1枚目しか使わない（マウスを乗せて初めてギャラリーを組む）ので、1枚あれば一覧は完成する
            if (queue.Thumbnails.First is { } next)
            {
                queue.Thumbnails.RemoveFirst();
                var item = next.Value;
                Report(progress, ImportPhase.FetchingThumbnails, ++queue.ThumbnailsDone, queue.ThumbnailsTotal, item.Booth.Name);

                using (BoothClient.Prioritize(BoothPriority.Thumbnail))
                {
                    // 取れなくても商品は画面に出す。出さないとその商品は永久に見えない
                    if (await _images.SyncOneAsync(item.Id, item.Booth.Images[0], cancellationToken))
                    {
                        downloaded++;
                    }
                }

                // 見込みは取り終えてから減らす（①②と揃える）
                work.PlanRequests(images: -1);

                queue.Galleries.Enqueue(item);
                queue.GalleriesTotal++;
                continue;
            }

            // ⑤ 残りの画像。商品ごとにまとめて取ると、その商品を開いたときに揃っている確率が上がる
            if (queue.Galleries.TryDequeue(out var gallery))
            {
                Report(progress, ImportPhase.FetchingGallery, ++queue.GalleriesDone, queue.GalleriesTotal, gallery.Booth.Name);

                using (BoothClient.Prioritize(BoothPriority.Gallery))
                {
                    downloaded += (await _images.SyncAsync(gallery.Id, gallery.Booth.Images, cancellationToken)).Downloaded;
                }

                work.PlanRequests(images: -(gallery.Booth.Images.Count - 1)); // 1枚目は④で数えた

                continue;
            }

            // ⑥ ショップのアイコン。使うのはショップ画面と作者名の横だけで、無くても名前で用は足りる
            var (subdomain, thumbnailUrl) = queue.ShopIcons.First();
            queue.ShopIcons.Remove(subdomain);
            Report(progress, ImportPhase.FetchingShopIcons, ++queue.IconsDone, queue.IconsTotal, subdomain);

            using (BoothClient.Prioritize(BoothPriority.ShopIcon))
            {
                await _images.SyncShopIconAsync(subdomain, thumbnailUrl, cancellationToken);
            }

            work.PlanRequests(images: -1);
        }

        return downloaded;
    }

    /// <summary>
    /// 周回の外で取る画像の列（U5）。
    ///
    /// 新しい周回の1枚目は**先頭**へ入れる。積んだ物が早く一覧に出ることの方が、
    /// 前の周回の2枚目より先に要る。件数は周回をまたいで数える（画面には1本の進み具合として出す）。
    /// </summary>
    private sealed class ImageQueue
    {
        public LinkedList<ItemRecord> Thumbnails { get; } = new();

        public Queue<ItemRecord> Galleries { get; } = new();

        public Dictionary<string, string> ShopIcons { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int ThumbnailsDone { get; set; }

        public int ThumbnailsTotal { get; set; }

        public int GalleriesDone { get; set; }

        public int GalleriesTotal { get; set; }

        public int IconsDone { get; set; }

        public int IconsTotal { get; set; }

        public bool IsEmpty => Thumbnails.Count == 0 && Galleries.Count == 0 && ShopIcons.Count == 0;

        public void Enqueue(IReadOnlyList<ItemRecord> withImages, IReadOnlyDictionary<string, string> shopIcons)
        {
            // 並びは保ったまま先頭へ
            for (var index = withImages.Count - 1; index >= 0; index--)
            {
                Thumbnails.AddFirst(withImages[index]);
            }

            ThumbnailsTotal += withImages.Count;

            foreach (var (subdomain, url) in shopIcons)
            {
                if (ShopIcons.TryAdd(subdomain, url))
                {
                    IconsTotal++;
                }
            }
        }
    }

    private sealed class FetchResult
    {
        public int Added { get; init; }

        public int AlreadyKnown { get; init; }

        public int NotFound { get; init; }

        /// <summary>BOOTHに無かったので未確定へ戻すファイル。ここで捨てると手元から消える。</summary>
        public IReadOnlyList<UnresolvedFile> NotFoundFiles { get; init; } = [];

        public int TemporaryFailures { get; init; }

        /// <summary>画像の列（④⑤）に積む商品。画像を取らない設定なら空。</summary>
        public IReadOnlyList<ItemRecord> WithImages { get; init; } = [];

        /// <summary>画像の列（⑥）に積むショップのアイコン。サブドメイン → アイコンのURL。</summary>
        public IReadOnlyDictionary<string, string> ShopIcons { get; init; } = new Dictionary<string, string>();

        public int AvatarItemsUpdated { get; init; }

        public int AvatarsFound { get; init; }

        public string? AvatarDetectError { get; init; }

        public bool AvatarDetectRan { get; init; }
    }
}
