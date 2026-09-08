using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;
using BoothIdResolver;
using BoothZipInspector;
using BoothZipInspector.Models;

namespace BoothAssetManager.Core.Scanning;

public enum ImportPhase
{
    Scanning,
    Resolving,
    Fetching,
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
}

public interface IImportPipeline
{
    Task<ImportSummary> RunAsync(
        IReadOnlyList<string> folders,
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

    public ImportPipeline(DataStore store, IBoothClient client, ImagePipeline images, AppSettings? settings = null)
    {
        _store = store;
        _client = client;
        _images = images;
        _settings = settings ?? new AppSettings();
    }

    public async Task<ImportSummary> RunAsync(
        IReadOnlyList<string> folders,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _store.Paths.EnsureCreated();

        var scanCache = new ScanCacheIndex(_store.ScanCache.Load());
        var exclusions = new ExclusionFilter(_store.Excluded.Load());

        // 既に商品へ紐付けたフォルダの中は見に行かない。
        // 「管理済み」なので未確定へ流す必要が無く、容量も別途数えている。
        var (registered, owned) = await LoadOwnedAsync(cancellationToken);

        var scan = ScanFolders(folders, exclusions, registered, progress, cancellationToken);
        var resolution = await ResolveAsync(scan.Files, scanCache, exclusions, owned, progress, cancellationToken);
        await _store.ScanCache.SaveAsync(scanCache.ToList(), cancellationToken);
        await _store.Unresolved.SaveAsync(resolution.Unresolved, cancellationToken);

        var fetchResult = await FetchAsync(resolution.FilesByItemId, progress, cancellationToken);

        return new ImportSummary
        {
            FilesScanned = scan.Files.Count,
            UnpackedFolders = scan.UnpackedFolders,
            FilesSkippedAsUnpacked = scan.SkippedInsideUnpackedFolders,
            FilesHashed = resolution.Hashed,
            FilesReusedFromCache = resolution.ReusedFromCache,
            FilesExcluded = resolution.Excluded,
            FilesAlreadyOwned = resolution.AlreadyOwned,
            UnresolvedFiles = resolution.Unresolved.Count,
            ItemsAdded = fetchResult.Added,
            ItemsAlreadyKnown = fetchResult.AlreadyKnown,
            NotFound = fetchResult.NotFound,
            TemporaryFailures = fetchResult.TemporaryFailures,
            ImagesDownloaded = fetchResult.ImagesDownloaded,
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
                await _store.Items.SaveAsync(
                    item with { Local = item.Local with { LocalFolders = refreshed } },
                    cancellationToken);
            }
        }

        return (new RegisteredFolderSet(paths), owned);
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
            var candidates = IdResolver.Resolve(zone, clues);

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
        var processed = 0;

        foreach (var (itemId, discovered) in filesByItemId)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new ImportProgress
            {
                Phase = ImportPhase.Fetching,
                Current = ++processed,
                Total = filesByItemId.Count,
                Detail = itemId,
            });

            var existing = await _store.Items.LoadAsync(itemId, cancellationToken);
            if (existing is not null)
            {
                // 取得済みのitemは触らない。中断して再実行した時に、ここが「続きから」を成立させる。
                var merged = LocalFileMerger.Merge(existing.Local.LocalFiles, discovered);
                await _store.Items.SaveAsync(
                    existing with { Local = existing.Local with { LocalFiles = merged } },
                    cancellationToken);
                alreadyKnown++;
                continue;
            }

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

            var htmlResult = await _client.GetItemHtmlAsync(itemId, cancellationToken);
            var extraction = htmlResult.IsSuccess && htmlResult.Value is not null
                ? H2SectionExtractor.Extract(htmlResult.Value)
                : new H2ExtractionResult();

            var item = new ItemRecord
            {
                Id = itemId,
                Booth = BoothItemMapper.Map(jsonResult.Value!, DateTimeOffset.Now, extraction.Sections),
                Local = new LocalBlock
                {
                    LocalFiles = LocalFileMerger.Merge([], discovered),
                    NotifyOnUpdate = _settings.NotifyOnUpdateByDefault,
                    LastFetchedAt = DateTimeOffset.Now,
                    NextFetchDueAt = NextFetchDue(itemId),
                },
            };

            await _store.Items.SaveAsync(item, cancellationToken);

            if (extraction.DescriptionHtml is not null)
            {
                await _store.Items.SaveDescriptionHtmlAsync(itemId, extraction.DescriptionHtml, cancellationToken);
            }

            var imageResult = await _images.SyncAsync(itemId, item.Booth.Images, cancellationToken);
            imagesDownloaded += imageResult.Downloaded;
            added++;
        }

        return new FetchResult
        {
            Added = added,
            AlreadyKnown = alreadyKnown,
            NotFound = notFound,
            TemporaryFailures = temporaryFailures,
            ImagesDownloaded = imagesDownloaded,
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
    }
}
