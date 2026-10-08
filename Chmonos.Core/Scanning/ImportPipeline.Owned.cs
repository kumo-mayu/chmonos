using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Core.Scanning;

// 取り込みの1周の初め：ライブラリが既に持っている物を読み（OwnedSnapshot）、走査の前に記録の側を今のディスクに合わせる
// （上書きで置き換わった場所を外す・移したファイルを結び直す・見つからない印・登録したフォルダの zip が手に入った知らせ）。
//
// ImportPipeline（約2,200行）を段ごとのファイルに分けた（点検24・ユーザ判断 2026-10-08）。クラスの説明は ImportPipeline.cs にある
public sealed partial class ImportPipeline
{
    /// <summary>
    /// 既に管理下にあるものを集める。登録済みフォルダと、itemが持っているファイルのハッシュ。
    /// ついでに登録済みフォルダの中身を数え直して保存する
    /// （数えるのは列挙だけでハッシュは計算しないので速い）。
    /// </summary>
    /// <param name="remeasure">
    /// 登録したフォルダを測り直し、zip が手に入っていないかを見るか。**取り込み1回につき最初の周回だけ**（2026-09-24）。
    /// 周回は積むたびに増え、そのたびに登録したフォルダの中を全部並べ直していた。測った値は容量の表示に使うだけで、
    /// 同じ取り込みの中で何度測っても変わらない。
    /// </param>
    private async Task<OwnedSnapshot> LoadOwnedAsync(
        bool remeasure,
        CancellationToken cancellationToken)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var paths = new List<string>();

        // zipが手に入っていたフォルダ。知らせは測り終えてから、錠の中で今の一覧に足す
        var archivesFound = new List<(ItemRecord Item, string FolderPath, string ArchivePath)>();

        // 持っているハッシュと、その中身の一覧（持っている zip を開かずに済ませるため。ResolveAsync）
        var owned = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        // 持っているハッシュと、それを持つ商品・記録している場所（移したファイルを結び直すため。ResolveAsync）。
        // 外した印の行は入れない——外した商品へ戻すと、人が外した判断を取り込みが覆す。
        // 上書きで残った古い版は入れる（所持には数えないが、その中身をまた見つけたらこの商品へ結び直して印を下ろす。⑤-B）
        var owners = new Dictionary<string, List<FileOwner>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in loaded.Items)
        {
            foreach (var file in item.Local.AttachedFiles)
            {
                if (!owned.TryGetValue(file.Hash, out var known) || (known.Count == 0 && file.Contents.Count > 0))
                {
                    owned[file.Hash] = file.Contents;
                }

                if (!owners.TryGetValue(file.Hash, out var list))
                {
                    list = [];
                    owners[file.Hash] = list;
                }

                list.Add(new FileOwner(item.Id, file.Paths, file.ArchiveBroken));
            }
        }

        // 起動時の見回り（MissingMarksSweep）と番を合わせる。同じフォルダを同時に見て、同じ商品を二度書かないように（ユーザ判断 2026-10-05）
        using var sweeping = await _missingMarks.EnterAsync(cancellationToken);

        // 在るかは見回りと同じ部品で見る（ドライブごとに根を1回・3秒で打ち切る。spec background-and-network.md）。
        // 前は Directory.Exists と IsOnMissingVolume を打ち切り無しで呼んでいて、落ちた共有の上の登録フォルダで周回の頭が
        // 1つにつき数十秒止まり得た（根の確かめが1回21秒かかったことがある）。同じ周回のファイルの見回りにも渡し、根を二度待たない
        var probe = _missingMarks.NewProbe();

        foreach (var item in loaded.Items.Where(item => item.Local.LocalFolders.Count > 0))
        {
            var measured = new Dictionary<string, FolderSurvey>(StringComparer.OrdinalIgnoreCase);

            // 「無い」と見たフォルダと、また見つかったフォルダ（LocalFolderRecord.MissingSince・ユーザ判断 2026-10-04）
            var missingNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenAgain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 在ると見た、ディスクの通し番号がまだ無い登録 → 今そこに来ているディスク（点検の3。在ると見たときに書き足す）
            var volumes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var changed = false;

            foreach (var folder in item.Local.LocalFolders)
            {
                var presence = probe.OfFolder(folder.Path, folder.Volume);
                if (presence == FilePresence.Present && folder.Volume is null && probe.Volumes.SerialAt(folder.Path) is { } serial)
                {
                    volumes[folder.Path] = serial;
                    changed = true;
                }

                if (presence != FilePresence.Present)
                {
                    // 見つからないものは登録として残すが、スキャンの除外には使わない。
                    // 無いことは記録に残す。ただしドライブごと見えない（外付けを外している・根が答えない）ときは「無い」と書かない
                    // （ファイルの取り込みが外付けの上の場所を残すのと同じ。LocalFileMerger）
                    if (folder.MissingSince is null && presence == FilePresence.Missing)
                    {
                        missingNow.Add(folder.Path);
                        changed = true;
                    }

                    continue;
                }

                paths.Add(folder.Path);
                if (folder.MissingSince is not null)
                {
                    seenAgain.Add(folder.Path);
                    changed = true;
                }

                if (!remeasure)
                {
                    continue;
                }

                // zipが手に入っていれば、フォルダ登録は役目を終えている。
                // 黙っていると容量が二重に乗ったままなので知らせる。
                if (RegisteredFolderSet.FindArchiveFor(folder.Path) is { } archive)
                {
                    archivesFound.Add((item, folder.Path, archive));
                }

                // 中の unitypackage も同じ1回の列挙で拾う（右クリックの「Unityへ送る」を、開くたびにフォルダを並べずに決めるため。メモ65-③）
                // 読めなかったら前の値を残す（0件として書くと、正しい値が消える。外部の点検 2026-10-06）
                if (RegisteredFolderSet.Survey(folder.Path, cancellationToken) is not { } survey)
                {
                    continue;
                }

                measured[folder.Path] = survey;
                if (survey.FileCount != folder.FileCount || survey.TotalBytes != folder.TotalBytes
                    || !survey.SamePackages(folder.UnityPackages) || folder.LastSeenAt is null)
                {
                    changed = true;
                }
            }

            if (changed)
            {
                // 全件を先に読んでから、フォルダを1つずつ測って回る。測るのに時間がかかるので、
                // 書く頃には写しが古い。**測った値を今の一覧に当てる**（古い写しで丸ごと書き戻すと、
                // 測っている間に人がフォルダを外した・ファイルに種類を付けた操作が消える）。
                // 見つからなくなった日時も同じく今の値に当てる（無い間は最初に見た日時を残す。決まりは起動時の見回りと同じ FileMissingMarks.MarkedFolder）。
                // 今の値で変わる物が無ければ書かない（起動時の見回りが先に同じ答えを書いていれば、二度書かない）
                var now = DateTimeOffset.Now;
                LocalFolderRecord WithVolume(LocalFolderRecord folder)
                    => folder.Volume is null && volumes.TryGetValue(folder.Path, out var serial) ? folder with { Volume = serial } : folder;

                await _store.Items.ChangeLocalAsync(
                    item.Id,
                    current =>
                    {
                        var folders = current.LocalFolders.Select(folder => WithVolume(
                            measured.TryGetValue(folder.Path, out var size)
                                ? folder with
                                {
                                    FileCount = size.FileCount,
                                    TotalBytes = size.TotalBytes,
                                    UnityPackages = size.UnityPackages,
                                    LastSeenAt = now,
                                    MissingSince = null,
                                }
                                : seenAgain.Contains(folder.Path)
                                    ? FileMissingMarks.MarkedFolder(folder, FilePresence.Present, now)
                                    : missingNow.Contains(folder.Path)
                                        ? FileMissingMarks.MarkedFolder(folder, FilePresence.Missing, now)
                                        : folder)).ToList();

                        return folders.Where((folder, index) => !ReferenceEquals(folder, current.LocalFolders[index])).Any()
                            ? current with { LocalFolders = folders }
                            : null;
                    },
                    LocalOwners.Import,
                    cancellationToken);
            }
        }

        if (archivesFound.Count > 0)
        {
            await _store.Notifications.TryUpdateAsync(
                notifications =>
                {
                    var before = notifications.Count;
                    foreach (var (item, folderPath, archivePath) in archivesFound)
                    {
                        NoteArchiveFound(notifications, item, folderPath, archivePath);
                    }

                    return notifications.Count == before ? null : notifications;
                },
                cancellationToken);
        }

        // 商品の記録が指している場所（場所 → どの商品の、どの中身か）。走査でそこに別の中身が見つかったら、上書きされた物（ResolveAsync）。
        // 外した印の行も入れる（場所を外すのは同じ。候補にはしない）
        var recordedAt = new Dictionary<string, List<RecordedFile>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in loaded.Items)
        {
            foreach (var file in item.Local.LocalFiles)
            {
                foreach (var path in file.Paths)
                {
                    // 記録が別のディスクの上の場所なら、今その場所に在る物は上書きではない（2台の外付けが同じ文字を使う。点検の3）。
                    // 入れると、Bの上の同じ名前の別の中身を見て、Aの上の記録から場所を外していた
                    if (PlaceVolumes.Of(file, path) is { } recorded
                        && probe.Volumes.SerialAt(path) is { } now
                        && !string.Equals(recorded, now, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!recordedAt.TryGetValue(path, out var list))
                    {
                        list = [];
                        recordedAt[path] = list;
                    }

                    list.Add(new RecordedFile(item.Id, file.Hash, !file.Detached));
                }
            }
        }

        return new OwnedSnapshot(new RegisteredFolderSet(paths), owned, owners, recordedAt, DetachedIndex.From(loaded.Items), probe);
    }

    /// <summary>
    /// 記録しているファイルの場所を全部見て、見つからなくなった日時（<see cref="LocalFileRecord.MissingSince"/>）を付け外しする
    /// （ユーザ判断 2026-10-04。フォルダの <see cref="LocalFolderRecord.MissingSince"/> と同じく取り込みのたびに見る）。
    /// </summary>
    /// <remarks>
    /// 前は取り込みがその商品のファイルを扱ったときにしか「無い」が記録に残らず、手で zip を消してもカードの印・検索の条件・統計に出なかった。
    /// **場所は外さない**（覚えている場所を黙って消さない。外すのは今までどおり、その商品を扱った取り込み＝<see cref="LocalFileMerger"/> だけ）。
    /// 在るかはドライブごとにまとめて見る（<see cref="FilePresenceProbe"/>。つながっていないドライブの上は見に行かず、書かない）。
    /// 外したファイルも見る（記録は事実なので。印と条件は外したファイルを数えない）。
    /// 書くのは商品ごとの錠の中で今の値に当て、見ている間に場所が変わったファイルには当てない（<see cref="FileMissingMarks.Apply"/>）。
    /// 見回りそのものは起動時の見回りと同じ <see cref="MissingMarksSweep"/>（1本ずつ回るので、起動時の見回りと重ならない）。
    /// </remarks>
    private Task NoteMissingFilesAsync(FilePresenceProbe probe, CancellationToken cancellationToken)
        => _missingMarks.NoteFilesAsync(probe, cancellationToken);

    /// <summary>
    /// 1周の初めに読む、ライブラリが既に持っている物（<see cref="LoadOwnedAsync"/>）。**1周の間だけ使う**——周回ごとに読み直すのは、
    /// 前の周回で増えた商品を次の周回が知っている必要があるため（点検26：6つの値を組で返していたのを、生きる長さの分かる型にまとめた）
    /// </summary>
    /// <param name="Registered">登録したフォルダ（走査で中を見ない所）。</param>
    /// <param name="Owned">持っているハッシュと、その zip の中身の一覧（持っている zip を開かずに済ませる）。</param>
    /// <param name="Owners">持っているハッシュと、それを持つ商品・記録している場所（移したファイルを結び直す）。外した印の行は入れない。</param>
    /// <param name="RecordedAt">記録している場所と、そこに記録しているファイル（上書き・移動を見分ける）。</param>
    /// <param name="Detached">外した印（人が外した判断を取り込みが覆さない）。</param>
    /// <param name="Probe">ドライブごとの在る・無いの見方（つながっていないドライブの上は見に行かない）。</param>
    private sealed record OwnedSnapshot(
        RegisteredFolderSet Registered,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Owned,
        IReadOnlyDictionary<string, List<FileOwner>> Owners,
        IReadOnlyDictionary<string, List<RecordedFile>> RecordedAt,
        DetachedIndex Detached,
        FilePresenceProbe Probe);

    /// <summary>そのハッシュを持つ商品と、その商品が記録している場所・開けなかった印。</summary>
    private sealed record FileOwner(string ItemId, IReadOnlyList<string> Paths, bool ArchiveBroken);

    /// <summary>商品の記録が指している場所1つ分：どの商品の、どの中身の記録か。外した印の行は <paramref name="Owned"/> が false。</summary>
    private sealed record RecordedFile(string ItemId, string Hash, bool Owned);

    /// <summary>
    /// 記録が指す場所に、別の中身が来ていたら、その場所を記録から外す（ユーザ判断 2026-09-30「3A」）。
    ///
    /// 外さないと、古い中身の記録が「その場所に在る」ままになる（<see cref="LocalFileMerger"/> は場所に何かが在るかしか見ない）。
    /// 更新版を同じ名前で上書きすると商品ページに同じ名前の行が2つ並び、壊れた zip は落とし直しても「壊れたzip」が消えなかった。
    /// 未確定は取り込みのたびに一覧を作り直すので、同じことは起きない。
    ///
    /// **場所が1つも残らなくなった記録は残し、古い版の印（<see cref="LocalFileRecord.Replaced"/>）を付ける**（商品ページで「古い版」の行になる。
    /// 「見つかりません」には数えない・2026-10-05・点検の8）。種類の結び付きや、手で結んだ事実を失わないため。
    /// **壊れた zip の記録だけは記録ごと落とす**——同じ場所に落とし直したのだから、壊れた方はもうどこにも無く、残しても取り戻す物が無い。
    /// 外した印の行からも場所は外す（そこに在るのは別の中身で、「この商品に戻す」相手ではない）。
    ///
    /// 錠の中で今の値に当てる。壊れているかも今の値で見る（読んでからここまでの間に印が下りていれば、記録は残す）。
    /// </summary>
    private async Task DropReplacedPathsAsync(
        IReadOnlyList<(string ItemId, string Hash, string Path)> replaced,
        CancellationToken cancellationToken)
    {
        foreach (var group in replaced.GroupBy(entry => entry.ItemId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _store.Items.ChangeLocalAsync(
                group.Key,
                current =>
                {
                    var changed = false;
                    var files = new List<LocalFileRecord>();
                    foreach (var file in current.LocalFiles)
                    {
                        var gone = group.Where(entry => string.Equals(entry.Hash, file.Hash, StringComparison.OrdinalIgnoreCase))
                            .Select(entry => entry.Path)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var paths = file.Paths.Where(path => !gone.Contains(path)).ToList();
                        if (paths.Count == file.Paths.Count)
                        {
                            files.Add(file);
                            continue;
                        }

                        changed = true;
                        if (paths.Count > 0)
                        {
                            files.Add(file with { Paths = paths });
                        }
                        else if (!file.ArchiveBroken)
                        {
                            // 場所が残らなかった記録は「古い版」と印を付ける（2026-10-05・点検の8）。印が無いと「見つかりません」と
                            // 数えられ続け、探しても見つからない。どこで置き換わったかは行の名前に使う（場所はもう空なので）
                            files.Add(file with
                            {
                                Paths = paths,
                                Replaced = new ReplacedVersion(file.Paths.First(gone.Contains), DateTimeOffset.Now),
                                MissingSince = null,
                            });
                        }
                    }

                    return changed ? current with { LocalFiles = files } : null;
                },
                LocalOwners.Import,
                cancellationToken);
        }
    }

    /// <summary>
    /// 同じ中身を商品が持っているファイルの場所を、その商品に足す（大容量の確かめ A・2026-09-30）。
    /// 対象は、手掛かりから決まらないファイルの持ち主全部と、手掛かりで別の商品に決まったファイルのほかの持ち主。
    /// 実在しなくなった場所は <see cref="LocalFileMerger"/> が落とすので、移した物は新しい場所に置き換わる。
    ///
    /// 錠の中で今の値に当て、読んでからここまでの間に人がこの商品から外した（印を付けた）なら足さない。
    /// 足すと <see cref="LocalFileMerger"/> が印を下ろしてしまい、外した判断を取り込みが覆す。
    /// </summary>
    /// <param name="probe">周回の頭で作った見方（無い場所を外すかを、見回りと同じ部品で決める。<see cref="LocalFileMerger.Merge(IReadOnlyList{LocalFileRecord}, IEnumerable{LocalFileRecord}, FilePresenceProbe)"/>）。</param>
    private async Task RelinkMovedFilesAsync(
        IReadOnlyDictionary<string, List<LocalFileRecord>> relinked,
        FilePresenceProbe probe,
        CancellationToken cancellationToken)
    {
        // 根の覚えは結び直しの間だけ（走査とハッシュの間に外付けを外していたら、周回の頭の「つながっている」は古い）
        var presence = probe.Renewed();
        foreach (var (itemId, discovered) in relinked)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _store.Items.ChangeLocalAsync(
                itemId,
                current =>
                {
                    var stillOwned = current.AttachedFiles.Select(file => file.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var files = discovered.Where(file => stillOwned.Contains(file.Hash)).ToList();
                    return files.Count == 0
                        ? null
                        : current with { LocalFiles = LocalFileMerger.Merge(current.LocalFiles, files, presence) };
                },
                LocalOwners.Import,
                cancellationToken);
        }
    }

    /// <summary>
    /// 見つけた物のうち、錠の中の今の値でこの商品から外してあるハッシュを除く。
    ///
    /// 行き先は読んだ時点の外した印で決めるが、書くのは後。その間に人が「この商品から外す」を押すと、
    /// 外した行に見つけた物を重ねることになり、<see cref="LocalFileMerger.Merge"/> の「両方が外していた時だけ残す」で
    /// 印が下りて、人の判断を取り込みが覆していた（2026-10-05・見つからない・移動の点検 1。<see cref="RelinkMovedFilesAsync"/> と同じ考え）。
    /// 外した物の場所は「この商品から外す」が未確定へ移している。
    /// </summary>
    private static List<LocalFileRecord> NotDetachedIn(LocalBlock current, IEnumerable<LocalFileRecord> discovered)
    {
        var detached = current.LocalFiles
            .Where(file => file.Detached)
            .Select(file => file.Hash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return discovered.Where(file => !detached.Contains(file.Hash)).ToList();
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
}
