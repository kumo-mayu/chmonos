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
    private readonly AppSettings _settings;
    private readonly FolderScanner _scanner = new();

    /// <summary>③ 対応アバターの検出。渡されなければその段を飛ばす。</summary>
    private readonly Services.IAvatarService? _avatars;

    public ImportPipeline(
        DataStore store,
        IBoothClient client,
        ImagePipeline images,
        AppSettings? settings = null,
        Services.IAvatarService? avatars = null)
    {
        _store = store;
        _client = client;
        _images = images;
        _settings = settings ?? new AppSettings();
        _avatars = avatars;
    }

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
        var detached = DetachedIndex.From(_store.Detached.Load());

        var totals = new ImportTotals();

        while (work.TakePending() is { Count: > 0 } folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 既に商品へ紐付けたフォルダの中は見に行かない。
            // 「管理済み」なので未確定へ流す必要が無く、容量も別途数えている。
            // 周回ごとに読み直すのは、前の周回で増えた商品を次の周回が知っている必要があるため。
            var (registered, owned) = await LoadOwnedAsync(cancellationToken);

            var scan = ScanFolders(folders, exclusions, registered, progress, cancellationToken);
            var resolution = await ResolveAsync(
                scan.Files, scanCache, exclusions, detached, owned, progress, cancellationToken);
            await _store.ScanCache.SaveAsync(scanCache.ToList(), cancellationToken);

            // 未確定は積み上げる。前の周回で残ったものを消してはいけない
            totals.Unresolved.AddRange(resolution.Unresolved);
            await _store.Unresolved.SaveAsync(totals.Unresolved, cancellationToken);

            var fetchResult = await FetchAsync(resolution.FilesByItemId, progress, cancellationToken);

            totals.Add(scan, resolution, fetchResult);
        }

        // 最後まで来たので記録は要らない。残すと次の起動で「中断した」と嘘をつく
        await _store.ImportState.SaveAsync(new ImportState(), cancellationToken);

        return totals.ToSummary();
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
                Detail = value.Current ?? value.Phase,
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

        public void Add(ScanOutcome scan, ResolutionResult resolution, FetchResult fetch)
        {
            _scanned += scan.Files.Count;
            _unpacked.AddRange(scan.UnpackedFolders);
            _skippedUnpacked += scan.SkippedInsideUnpackedFolders;

            _hashed += resolution.Hashed;
            _reused += resolution.ReusedFromCache;
            _excluded += resolution.Excluded;
            _alreadyOwned += resolution.AlreadyOwned;

            _added += fetch.Added;
            _alreadyKnown += fetch.AlreadyKnown;
            _notFound += fetch.NotFound;
            _temporaryFailures += fetch.TemporaryFailures;
            _imagesDownloaded += fetch.ImagesDownloaded;

            // 検出は周回ごとに走るので足し合わせる。理由は最後のものを残す
            _avatarItemsUpdated += fetch.AvatarItemsUpdated;
            _avatarsFound += fetch.AvatarsFound;
            _avatarDetectError = fetch.AvatarDetectError ?? _avatarDetectError;
        }

        public ImportSummary ToSummary() => new()
        {
            FilesScanned = _scanned,
            UnpackedFolders = _unpacked,
            FilesSkippedAsUnpacked = _skippedUnpacked,
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
        };
    }

    /// <summary>
    /// 既に管理下にあるものを集める。登録済みフォルダと、itemが持っているファイルのハッシュ。
    /// ついでに登録済みフォルダの中身を数え直して保存する
    /// （数えるのは列挙だけでハッシュは計算しないので速い）。
    /// </summary>
    private async Task<(RegisteredFolderSet Registered, IReadOnlySet<string> OwnedHashes)> LoadOwnedAsync(
        CancellationToken cancellationToken)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var paths = new List<string>();
        var notifications = _store.Notifications.Load();
        var notificationCountBefore = notifications.Count;

        var owned = loaded.Items
            .SelectMany(item => item.Local.LocalFiles)
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

        return (new RegisteredFolderSet(paths), owned);
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
            Title = $"{item.Booth.Name ?? item.Id}：zipが手元に入りました",
            Detail = $"フォルダ登録は不要になりました。{Path.GetFileName(archivePath)} を取り込めば、"
                + $"展開先（{Path.GetFileName(folderPath)}）は自動で対象から外れます。"
                + "商品ページからフォルダの登録を解除してください（このままだと容量が二重に数えられます）。",
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

        foreach (var folder in folders)
        {
            var result = _scanner.Scan(folder, cancellationToken);
            unpacked.AddRange(result.UnpackedFolders);
            skippedUnpacked += result.SkippedInsideUnpackedFolders;

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
        };
    }

    private sealed class ScanOutcome
    {
        public required List<ScannedFile> Files { get; init; }

        public required List<UnpackedFolder> UnpackedFolders { get; init; }

        public int SkippedInsideUnpackedFolders { get; init; }
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
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var added = 0;
        var alreadyKnown = 0;
        var notFound = 0;
        var temporaryFailures = 0;
        var imagesDownloaded = 0;
        var avatarItemsUpdated = 0;
        var avatarsFound = 0;
        string? avatarDetectError = null;

        // 手元にある商品は通信が要らない。ファイルを足すだけなので、段に入る前に片付ける
        var pending = new List<(string ItemId, List<LocalFileRecord> Files)>();

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
        }

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

            if (jsonResult.Status == BoothFetchStatus.NotFound)
            {
                notFound++;
                continue;
            }

            if (!jsonResult.IsSuccess || jsonResult.Value is null)
            {
                temporaryFailures++;
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
            fetched.Add(item);
            added++;

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
        done = 0;

        for (var index = 0; index < fetched.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = fetched[index];
            Report(progress, ImportPhase.FetchingHtml, ++done, fetched.Count, item.Id);

            var htmlResult = await _client.GetItemHtmlAsync(item.Id, cancellationToken);
            if (!htmlResult.IsSuccess || htmlResult.Value is null)
            {
                // 節が取れなくても商品自体は使える。次の段へ進む
                continue;
            }

            var extraction = H2SectionExtractor.Extract(htmlResult.Value);

            fetched[index] = item = item with { Booth = item.Booth with { H2Sections = extraction.Sections } };
            await _store.Items.SaveLocalAsync(
                item.Id,
                item.Local,
                [],
                item.Booth,
                cancellationToken);

            if (extraction.DescriptionHtml is not null)
            {
                await _store.Items.SaveDescriptionHtmlAsync(item.Id, extraction.DescriptionHtml, cancellationToken);
            }
        }

        // ── ③ 対応アバターの検出 ──
        //
        // 画像より先に置く。対応アバターを選ぶのは人の作業で、その候補が出揃っている
        // ことの方が、絵が見えていることより先に要る。
        // 通信が要るのは「対応表明で名前が出たが、手元に持っていないアバター」だけなので、
        // ここを④の前に置いても待ちはほとんど伸びない。
        if (_avatars is not null && fetched.Count > 0)
        {
            using var detectPriority = BoothClient.Prioritize(BoothPriority.Detection);

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

        // ── ④ 1枚目の画像（全商品）──
        //
        // 一覧のカードは1枚目しか使っていない（静止時に1枚だけ読み、
        // マウスを乗せて初めてギャラリーを組む）。だから1枚あれば一覧は完成する。
        var withImages = fetched.Where(item => item.Booth.Images.Count > 0).ToList();
        done = 0;

        using var thumbnailPriority = BoothClient.Prioritize(BoothPriority.Thumbnail);

        foreach (var item in withImages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, ImportPhase.FetchingThumbnails, ++done, withImages.Count, item.Booth.Name);

            if (await _images.SyncOneAsync(item.Id, item.Booth.Images[0], cancellationToken))
            {
                imagesDownloaded++;
            }

            // 取れなくても商品は画面に出す。出さないとその商品は永久に見えない
        }

        // ── ⑤ 残りの画像（商品ごと）──
        //
        // 商品ごとにまとめて取るのは、その商品を開いたときに揃っている確率を上げるため。
        done = 0;

        using var galleryPriority = BoothClient.Prioritize(BoothPriority.Gallery);

        foreach (var item in withImages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, ImportPhase.FetchingGallery, ++done, withImages.Count, item.Booth.Name);

            var result = await _images.SyncAsync(item.Id, item.Booth.Images, cancellationToken);
            imagesDownloaded += result.Downloaded;
        }

        // ── ⑥ ショップのアイコン ──
        //
        // 使うのはショップ画面と商品ページの作者名の横だけで、無くても名前で用は足りる。
        done = 0;

        using var iconPriority = BoothClient.Prioritize(BoothPriority.ShopIcon);

        foreach (var (subdomain, thumbnailUrl) in shopIcons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, ImportPhase.FetchingShopIcons, ++done, shopIcons.Count, subdomain);

            await _images.SyncShopIconAsync(subdomain, thumbnailUrl, cancellationToken);
        }

        return new FetchResult
        {
            Added = added,
            AlreadyKnown = alreadyKnown,
            NotFound = notFound,
            TemporaryFailures = temporaryFailures,
            ImagesDownloaded = imagesDownloaded,
            AvatarItemsUpdated = avatarItemsUpdated,
            AvatarsFound = avatarsFound,
            AvatarDetectError = avatarDetectError,
        };
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

    private sealed class FetchResult
    {
        public int Added { get; init; }

        public int AlreadyKnown { get; init; }

        public int NotFound { get; init; }

        public int TemporaryFailures { get; init; }

        public int ImagesDownloaded { get; init; }

        public int AvatarItemsUpdated { get; init; }

        public int AvatarsFound { get; init; }

        public string? AvatarDetectError { get; init; }
    }
}
