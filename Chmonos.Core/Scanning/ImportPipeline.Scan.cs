using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using BoothIdResolver;
using BoothZipInspector;
using BoothZipInspector.Models;

namespace Chmonos.Core.Scanning;

// <summary>/n/// 取り込みの走査と ID の特定：フォルダを並べてハッシュを取り（<see cref="ScanFolders"/>）、手掛かりから商品 ID を決める（<see cref="ResolveAsync"/>）。/n////n/// ImportPipeline（約2,200行）を段ごとのファイルに分けた（点検24・ユーザ判断 2026-10-08）。中身は変えていない/n/// </summary>
public sealed partial class ImportPipeline
{
    private ScanOutcome ScanFolders(
        IReadOnlyList<string> folders,
        ExclusionFilter exclusions,
        ScanCacheIndex scanCache,
        RegisteredFolderSet registered,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var scanned = new List<ScannedFile>();
        var unpacked = new List<UnpackedFolder>();
        var skippedUnpacked = 0;
        var unreadable = 0;
        var unreadableFolders = 0;
        var onlineOnly = 0;
        var unseen = new List<string>();
        var links = 0;

        foreach (var folder in folders)
        {
            var result = _scanner.Scan(folder, cancellationToken);
            unseen.AddRange(result.UnreadableFolders);
            unseen.AddRange(result.NotRead);

            // たどらなかったリンクは数を結果に出し、どれかはログに残す（見つからない・移動の点検 15）。
            // リンクの先を勝手にたどらないのは、ループと二重読みを避けるため。入れたい人はリンク先を取り込み元に足す
            links += result.Links.Count;
            foreach (var link in result.Links)
            {
                Diagnostics.AppLog.Warn("取り込みの走査", $"{link}：リンクなので中を読みませんでした。リンク先を取り込み元に足すと取り込めます");
            }
            unpacked.AddRange(result.UnpackedFolders);
            skippedUnpacked += result.SkippedInsideUnpackedFolders;
            unreadable += result.Unreadable;

            // 中を並べられなかったフォルダは数を結果に出し、どれかはログに残す（ハッシュを取れなかったファイルと同じ・大容量の確かめ #5）
            unreadableFolders += result.UnreadableFolders.Count;
            foreach (var denied in result.UnreadableFolders)
            {
                Diagnostics.AppLog.Warn("取り込みの走査", $"{denied}：フォルダの中を読めませんでした");
            }

            // 中身が手元に無いクラウドのファイルは、読むとダウンロードが始まるので飛ばした。
            // 数は結果に出す（ユーザ判断 2026-09-23）。結果は全体の数だけなので、どのフォルダで何件かはログに残す
            onlineOnly += result.OnlineOnly;
            if (result.OnlineOnly > 0)
            {
                Diagnostics.AppLog.Warn(
                    "取り込みの走査",
                    $"{folder}：中身が手元に無いクラウドのファイル {result.OnlineOnly} 件は読みませんでした（開くとダウンロードが始まるため）");
            }

            foreach (var file in result.Files)
            {
                // 外したパスで、控えから中身も同じと分かる物はここで弾く。ハッシュ計算にすら進ませない。
                // 同じパスでも中身が変わっていれば（落とし直した更新版）ここを通し、解決でハッシュを取って決める
                if (exclusions.IsExcludedWithoutHashing(file, scanCache))
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
            UnreadableFolders = unreadableFolders,
            OnlineOnly = onlineOnly,
            Unseen = unseen,
            Links = links,
        };
    }

    private sealed class ScanOutcome
    {
        public required List<ScannedFile> Files { get; init; }

        public required List<UnpackedFolder> UnpackedFolders { get; init; }

        public int SkippedInsideUnpackedFolders { get; init; }

        public int Unreadable { get; init; }

        public int UnreadableFolders { get; init; }

        public int OnlineOnly { get; init; }

        /// <summary>在るのに今回見ていない場所（読めなかったフォルダ・オンラインのみ・読めなかった1ファイル）。</summary>
        public required List<string> Unseen { get; init; }

        /// <summary>たどらなかったジャンクション・シンボリックリンクの数。</summary>
        public int Links { get; init; }
    }

    private async Task<ResolutionResult> ResolveAsync(
        List<ScannedFile> scanned,
        ScanCacheIndex scanCache,
        ExclusionFilter exclusions,
        DetachedIndex detached,
        IReadOnlyDictionary<string, IReadOnlyList<string>> owned,
        IReadOnlyDictionary<string, List<FileOwner>> owners,
        IReadOnlyDictionary<string, List<RecordedFile>> recordedAt,
        IReadOnlyList<UnresolvedFile> previousUnresolved,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var filesByItemId = new Dictionary<string, List<LocalFileRecord>>(StringComparer.Ordinal);
        var relinked = new Dictionary<string, List<LocalFileRecord>>(StringComparer.Ordinal);
        var unresolved = new List<UnresolvedFile>();
        var hashed = 0;
        var reused = 0;
        var excluded = 0;
        var alreadyOwned = 0;
        var unreadable = 0;
        var unhashed = new List<string>();
        var brokenArchives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var brokenOwned = new List<(string ItemId, string Hash)>();
        var replaced = new List<(string ItemId, string Hash, string Path)>();

        // 前に書いた未確定の「同じ場所にあった商品」（中身のハッシュ → 商品ID）。一覧は取り込みのたびに作り直すので、
        // 引き継がないと次の取り込みで消える（古い記録はもうその場所を指していないので、見つけ直せない）
        var carriedSamePath = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var previous in previousUnresolved.Where(previous => previous.SamePathItemIds.Count > 0))
        {
            carriedSamePath.TryAdd(previous.Hash, previous.SamePathItemIds);
        }

        var processed = 0;
        var lastCacheSave = Environment.TickCount64;

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
                    // 書き込み中のダウンロードや、ほかのアプリが開いているファイルがここに来る。
                    // 黙って飛ばしていたので、取り込んだつもりの物が入っていないことに気付けなかった。
                    // 走査で読めなかった物（E4）と同じ数に入れて結果に出し、どれかはログに残す
                    Diagnostics.AppLog.Warn("取り込みでファイルを読む", $"{file.Path}：{exception.Message}");
                    unreadable++;
                    unhashed.Add(file.Path);
                    continue;
                }

                scanCache.Set(file.Path, file.SizeBytes, file.ModifiedAtUtc, hash);
                hashed++;

                // 途中でも控えを書く（閉じても計算した分は残す）。**間隔は時間で決める**：本数で決めていた（50本ごと）ので、
                // 小さいファイルが続くと1秒に何度も控え全体（1万件なら約2MB）を書き直していた
                if (Environment.TickCount64 - lastCacheSave >= ScanCacheSaveIntervalMs)
                {
                    await SaveScanCacheAsync(scanCache, cancellationToken);
                    lastCacheSave = Environment.TickCount64;
                }
            }

            // 記録の場所に、別の中身が在る（同じ名前で上書きした・壊れた zip を落とし直した）。古い中身の記録からこの場所を外す
            // （書くのは DropReplacedPathsAsync）。管理から外した中身が来ていても、古い方がそこに無いことは同じ。
            // **ここまで来た物だけを判じる**：ハッシュを取れた（か控えから分かった）場所だけ。走査していない場所・
            // 読めなかったファイル・つながっていないドライブの上の場所は、中身が変わったとは言えないので触らない
            IReadOnlyList<string> formerHolders = [];
            if (recordedAt.TryGetValue(file.Path, out var recordedHere))
            {
                var stale = recordedHere
                    .Where(record => !string.Equals(record.Hash, hash, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                replaced.AddRange(stale.Select(record => (record.ItemId, record.Hash, file.Path)));

                // 外した印の行の商品は候補にしない（前の中身を「この商品のものではない」と人が外している）
                formerHolders = [.. stale.Where(record => record.Owned)
                    .Select(record => record.ItemId)
                    .Distinct(StringComparer.Ordinal)];
            }

            // 移動・改名された除外ファイルと、外したパスで控えと合わなかった物は、ハッシュで最終判定する。
            // 外したパスでも中身が違えばここを抜け、新しい物として取り込まれる
            if (exclusions.IsExcludedByHash(hash))
            {
                excluded++;
                continue;
            }

            // **持っている zip は開かない**（2026-09-24）。取り込み直すたびに持っているファイル全部の zip を開き直していた。
            // 中身の一覧は商品が持ち、ID の手掛かりは走査の控えがハッシュと一緒に持つ（どちらも中身だけで決まる）。
            // 商品の一覧が空の物（手で付けた等）は、前と同じく開いて一覧を取る
            IReadOnlyList<BoothClue> clues;
            IReadOnlyList<string> contents;
            var broken = false;
            if (file.IsArchive
                && owned.TryGetValue(hash, out var knownContents) && knownContents.Count > 0
                && scanCache.TryGetClueItemIds(file, hash, out var knownClues))
            {
                clues = [.. knownClues.Select(ClueOf)];
                contents = knownContents;
            }
            else
            {
                // zip の中身読みは同期なので、画面のスレッドへ戻ってから走っていた（C19）。
                // ハッシュ計算だけが本当に非同期で、その直後にここで引っかかる
                var inspected = await Task.Run(() => InspectFile(file), cancellationToken);
                clues = inspected.Clues;
                contents = inspected.Contents;
                broken = inspected.Broken;
                if (broken && brokenArchives.Add(hash))
                {
                    // 前は握りつぶしていて、途中で切れたダウンロードが普通の未確定と見分けられなかった（大容量の確かめ 問題4）。
                    // 印は未確定の行にも商品のファイルの行にも出るが、結果の文が名前を言うのは商品1つだけなので、どれかはここにも残す
                    Diagnostics.AppLog.Warn("取り込みで zip を開く", $"{file.Path}：zip として開けなかった（壊れているか、zip ではない）");
                }

                // 前から商品が持っている壊れた zip（手で結んだ物・前の取り込みで結び付いた物）。結果の数に入れる
                if (broken && owners.TryGetValue(hash, out var brokenHolders))
                {
                    brokenOwned.AddRange(brokenHolders.Select(holder => (holder.ItemId, hash)));
                }

                if (file.IsArchive && inspected.Read)
                {
                    // 開けなかった zip（ほかのアプリが開いている等）は控えない。次の取り込みでまた開く
                    scanCache.SetClueItemIds(file, hash, ClueItemIdsOf(clues));
                }
            }

            var zone = ZoneIdentifierReader.Read(file.Path);

            // このファイルの場所を、記録にまだ持っていない持ち主へ足す予定に積む（書くのは RelinkMovedFilesAsync）。
            // **開けなかった印が今の答えと違う持ち主にも積む**（ユーザ判断 2026-09-30）：手で結んだ壊れた zip は場所が変わらないので、
            // 積まないと印を書く機会が無い。逆（印があるのに今回は開けた）は、開けて中身の一覧が取れたときだけ——
            // ほかのアプリが開いていて読めなかった回に、壊れていないことにしない
            void Relink(IEnumerable<FileOwner> holders)
            {
                // 場所は綴りまで同じかで見る。大文字小文字だけ違う（名前の大文字小文字だけを変えた）物も積み、
                // 足すときに記録の綴りを今の名前に合わせる（LocalFileMerger。2026-10-05・点検の14）
                foreach (var holder in holders.Where(holder =>
                             !holder.Paths.Contains(file.Path, StringComparer.Ordinal)
                             || (holder.ArchiveBroken != broken && (broken || contents.Count > 0))))
                {
                    if (!relinked.TryGetValue(holder.ItemId, out var list))
                    {
                        list = [];
                        relinked[holder.ItemId] = list;
                    }

                    list.Add(new LocalFileRecord
                    {
                        Hash = hash,
                        Paths = [file.Path],
                        SizeBytes = file.SizeBytes,
                        Contents = contents,
                        ArchiveBroken = broken,
                    });
                }
            }

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

                    // ダウンロード元の記録から商品が決まると未確定を通らない。開けなかった事実は商品の記録に持っていく
                    ArchiveBroken = broken,
                };

                if (!filesByItemId.TryGetValue(candidates[0].ItemId, out var list))
                {
                    list = [];
                    filesByItemId[candidates[0].ItemId] = list;
                }

                list.Add(record);

                // **手掛かりで決まっても、同じ中身を持つほかの商品へ新しい場所を足す**（ユーザ判断 2026-09-30）。
                // 同じ zip を2つの商品が持つ（片方は手掛かり、もう片方は人が手で結んだ）とき、前は決まった商品にだけ足し、
                // 手で結んだ方は古い場所のまま「見つかりません」になっていた。手掛かりで決まらないとき（下）は両方へ足すので、
                // 手掛かりの有無で結果が分かれていた。決まった商品そのものは「手元にある商品」の道（FetchAsync）が足す
                if (owners.TryGetValue(hash, out var others))
                {
                    Relink(others.Where(holder => !string.Equals(holder.ItemId, candidates[0].ItemId, StringComparison.Ordinal)));
                }
            }
            else if (owners.TryGetValue(hash, out var holders))
            {
                // 未確定画面で手作業で紐付けたファイル。手掛かりからは決まらないので、
                // 毎回ここへ落ちてくる。既にitemが持っていると分かっているものを
                // 作業として出し直すのは嘘なので、未確定には出さない。
                //
                // ただし**記録に無い場所で見つかったら、その商品に足す**（大容量の確かめ A・2026-09-30）。
                // 同一性はハッシュなので、別の取り込み元へ移した・写しを置いただけなら同じ物と言える。
                // 前は黙って飛ばしていたので、移した後は商品の記録が古い場所のまま「見つからない」になっていた。
                // 同じ中身を2つの商品が持つなら両方へ足す（どちらの物かは人が決めたことで、場所は中身の場所）
                alreadyOwned++;
                Relink(holders);
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

                    // 同じ場所に前にあった別の中身の持ち主を、候補として残す（結ぶのは人が選んだときだけ）。
                    // 上書きを見つけた回にしか分からないので、前の回に書いた分も同じ中身の記録から引き継ぐ。
                    // この中身をその商品から外してあれば（外した印）載せない——違うと人が決めている
                    SamePathItemIds = [.. formerHolders
                        .Concat(carriedSamePath.GetValueOrDefault(hash) ?? [])
                        .Distinct(StringComparer.Ordinal)
                        .Where(itemId => !detached.IsDetached(hash, itemId))],
                    ArchiveBroken = broken,
                });
            }
        }

        return new ResolutionResult
        {
            FilesByItemId = filesByItemId,
            Relinked = relinked,
            Unresolved = unresolved,
            Hashed = hashed,
            ReusedFromCache = reused,
            Excluded = excluded,
            AlreadyOwned = alreadyOwned,
            Unreadable = unreadable,
            Unhashed = unhashed,
            BrokenOwned = brokenOwned,
            Replaced = replaced,
        };
    }

    /// <summary>ZIPだけ中身を読む。それ以外の形式は Zone.Identifier だけが手掛かりになる。</summary>
    /// <returns>
    /// 手掛かりと中身の一覧、読めたか（読めなかった zip と zip 以外は false）、zip として開けなかったか。
    /// </returns>
    private static (IReadOnlyList<BoothClue> Clues, IReadOnlyList<string> Contents, bool Read, bool Broken) InspectFile(ScannedFile file)
    {
        if (!file.IsArchive)
        {
            return ([], [], false, false);
        }

        try
        {
            var inspection = ZipInspector.Inspect(file.Path);
            return (inspection.Clues, inspection.Summary.Files.Select(entry => entry.RelativePath).ToList(), true, false);
        }
        catch (InvalidDataException)
        {
            // 形式が合わない：目録（末尾の一覧）が無い・崩れている。途中で切れたダウンロードはここに来る。
            // 何度開いても同じなので「壊れている」と言える。目録が無事で中のデータだけが化けた zip は、
            // 全部を解かないと分からないのでここでは見つからない（一時展開で分かる）
            return ([], [], false, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // ほかのアプリが開いている・権限が無い。次の取り込みでは開けるかもしれないので、壊れているとは言わない
            return ([], [], false, false);
        }
    }

    /// <summary>
    /// 控えに書く手掛かり。ID を決めるのに使うのは「商品のURL」の手掛かりの商品IDだけ（<see cref="IdResolver.Resolve"/>）なので、
    /// それだけを出てきた順に残す。
    /// </summary>
    private static IReadOnlyList<string> ClueItemIdsOf(IReadOnlyList<BoothClue> clues)
        => [.. clues
            .Where(clue => clue.Kind == BoothClueKind.ItemUrl && clue.ItemId is not null)
            .Select(clue => clue.ItemId!)
            .Distinct(StringComparer.Ordinal)];

    /// <summary>控えた商品IDを、読んだときと同じ働きの手掛かりに戻す。</summary>
    private static BoothClue ClueOf(string itemId) => new()
    {
        Kind = BoothClueKind.ItemUrl,
        Url = IdResolver.ToItemUrl(itemId),
        ItemId = itemId,
        SourcePath = "（走査の控え）",
    };

    private sealed class ResolutionResult
    {
        public required Dictionary<string, List<LocalFileRecord>> FilesByItemId { get; init; }

        /// <summary>手掛かりからは決まらず、同じ中身を持つ商品へ場所を足す物（商品ID → ファイル）。</summary>
        public required Dictionary<string, List<LocalFileRecord>> Relinked { get; init; }

        public required List<UnresolvedFile> Unresolved { get; init; }

        public int Hashed { get; init; }

        public int ReusedFromCache { get; init; }

        public int Excluded { get; init; }

        /// <summary>既にitemが持っていたので未確定へ流さなかった件数。</summary>
        public int AlreadyOwned { get; init; }

        /// <summary>中身を読めず（ハッシュを計算できず）飛ばした件数。</summary>
        public int Unreadable { get; init; }

        /// <summary>ハッシュを計算できなかったファイルの場所（今回見ていない場所として、前の未確定を残す）。</summary>
        public IReadOnlyList<string> Unhashed { get; init; } = [];

        /// <summary>zip として開けなかった物のうち、前から商品が持っている物（商品ID とハッシュ）。結果の数に入れる。</summary>
        public required List<(string ItemId, string Hash)> BrokenOwned { get; init; }

        /// <summary>記録の場所に、別の中身が来ていた物（その記録からこの場所を外す）。</summary>
        public required List<(string ItemId, string Hash, string Path)> Replaced { get; init; }
    }
}
