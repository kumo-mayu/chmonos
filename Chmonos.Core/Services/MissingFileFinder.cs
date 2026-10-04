using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>見つからないファイルを探した結果。</summary>
public sealed record MissingFileSearchResult
{
    /// <summary>探す前に見つからなかったファイルの数（同じ中身は1件と数える）。場所が空のファイルも入る。</summary>
    public required int MissingBefore { get; init; }

    /// <summary>場所を付け替えられた数（場所が空だったファイルに場所を足した数も入る）。</summary>
    public required int Relinked { get; init; }

    /// <summary>探しても出てこなかった数。</summary>
    public int StillMissing => MissingBefore - Relinked;

    /// <summary>中身まで確かめたファイルの数（大きさが合う物だけ）。</summary>
    public required int Hashed { get; init; }

    /// <summary>見に行けなかったフォルダ（外付けを外している間など）。</summary>
    public IReadOnlyList<string> Unreachable { get; init; } = [];
}

/// <summary>
/// **動かしたファイルを、中身で見つけて結び直す**（ユーザ判断 2026-09-21・G17）。
///
/// 手元のファイルの同一性はハッシュで持っている（パスは入れ物）。
/// だからファイルを別のフォルダへ移した・名前を変えただけなら、
/// **同じ中身を探して場所を書き換えれば元に戻る**。手で1件ずつ結び直すのは現実的ではない。
///
/// 探すのは**監視フォルダの中だけ**。起動時に勝手に走査してよい範囲はそこだけ、という決まりに合わせる
/// （人が押したときに走るので、この操作自体は指示された読み取り）。
/// </summary>
public sealed class MissingFileFinder
{
    private readonly DataStore _store;
    private readonly FolderScanner _scanner = new();

    public MissingFileFinder(DataStore store)
    {
        _store = store;
    }

    /// <remarks>
    /// **全体を呼んだスレッドの外で回す**（ユーザ判断 2026-09-30）。取り込みと同じ形（<c>ImportPipeline.RunAsync</c>）。
    ///
    /// 命令の入口から <c>await</c> でつながっているだけだったので、続きは毎回画面のスレッドへ戻り、
    /// 走査の控えの読みと錠の中の読み直し（8万件・21MB で 1回 0.19〜0.27秒）・監視フォルダの列挙・
    /// 商品のファイルが在るかの確かめ（つながらないネットワークのドライブは1回で数秒待ち得る）・ハッシュの合間が画面を止めていた。
    /// 控えの窓口だけを裏へ出しても、列挙と在るかの確かめは残る（控えが無くても 2万ファイルで 76〜149ms）。
    ///
    /// 中から画面の物には触らない。画面へ出るのは <paramref name="progress"/> だけで、裏のスレッドから呼ぶ。
    /// 受け手が画面の物に触るなら、受け手の側で画面のスレッドへ運ぶ（画面で作った <c>Progress</c> は運ぶ）。
    /// 商品の書き換えは商品ごとの錠の中・控えの書き換えは控えの錠の中で今の値に当てるので、取り込みと重なっても互いの分を消さない。
    /// </remarks>
    /// <param name="progress">今どこを見ているか（見たファイル数・全体は分からないので数だけ）。**裏のスレッドから呼ばれる。**</param>
    public Task<MissingFileSearchResult> FindAsync(
        IReadOnlyList<string> folders,
        IProgress<(int Hashed, string? Detail)>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => FindCoreAsync(folders, progress, cancellationToken));

    private async Task<MissingFileSearchResult> FindCoreAsync(
        IReadOnlyList<string> folders,
        IProgress<(int Hashed, string? Detail)>? progress,
        CancellationToken cancellationToken)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);

        // 中身（ハッシュ）→ その中身を持っているはずの商品と、今そこに無いパス
        //
        // **場所が空の物も探す**（ユーザ判断 2026-10-04）。取り込みはディスクに無いと見た場所を記録から外すので
        // （LocalFileMerger）、全部外れたファイルは「無い場所」を持たない。前は無い場所を持つ物だけを探していたので、
        // 検索の条件「見つからないファイル」とカードの印（ItemRecord.HasMissingFile）が数える物が、ここでは探されなかった。
        // 空の物は差し替える古い場所が無いので、見つけた場所を足す（gone が空の Replace）
        var missing = new Dictionary<string, MissingEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in loaded.Items)
        {
            foreach (var file in item.Local.LocalFiles.Where(file => !file.Detached))
            {
                var gone = file.Paths.Where(path => !DiskCheck.FileExists(path)).ToList();
                if (gone.Count == 0 && file.Paths.Count > 0)
                {
                    continue;
                }

                if (!missing.TryGetValue(file.Hash, out var entry))
                {
                    missing[file.Hash] = entry = new MissingEntry(file.SizeBytes);
                }

                entry.Owners.Add((item.Id, file.Paths, gone));
            }
        }

        if (missing.Count == 0)
        {
            return new MissingFileSearchResult { MissingBefore = 0, Relinked = 0, Hashed = 0 };
        }

        // **大きさが合う物だけ中身を確かめる。**監視フォルダ全部をハッシュすると、
        // 数百GBを読むことになる。大きさが違えば中身も違うので、そこで落とせる
        var sizes = missing.Values.Select(entry => entry.SizeBytes).ToHashSet();

        var cache = new ScanCacheIndex(_store.ScanCache.Load());
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var unreachable = new List<string>();
        var hashed = 0;

        // 計算したハッシュは走査の控えに足す。前は捨てていたので、同じ大きさのファイルを探すたび・取り込むたびに
        // 同じファイルを読み直していた（大きさが合う物は数GB の zip のこともある）
        var computed = new List<(ScannedFile File, string Hash)>();

        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!DiskCheck.FolderExists(folder))
            {
                // 外付けを外している間は「無い」ではなく「見られなかった」
                unreachable.Add(folder);
                continue;
            }

            foreach (var file in _scanner.Scan(folder, cancellationToken).Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!sizes.Contains(file.SizeBytes))
                {
                    continue;
                }

                string hash;
                if (cache.TryGetHash(file.Path, file.SizeBytes, file.ModifiedAtUtc, out var cached))
                {
                    hash = cached;
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

                    computed.Add((file, hash));

                    // 数えるのは知らせの外で（`progress?.Report(++hashed…)` は受け手が無いと数えなかった）
                    hashed++;
                    progress?.Report((hashed, Path.GetFileName(file.Path)));
                }

                if (missing.ContainsKey(hash))
                {
                    found.TryAdd(hash, file.Path);
                }
            }
        }

        if (computed.Count > 0)
        {
            // 控えは取り込みも書くので、錠の中で今の控えに足す（読んだ時の写しで丸ごと書くと、その間に取り込みが足した分を消す）
            await _store.ScanCache.UpdateAsync(
                current =>
                {
                    var index = new ScanCacheIndex(current);
                    foreach (var (file, hash) in computed)
                    {
                        index.Set(file.Path, file.SizeBytes, file.ModifiedAtUtc, hash);
                    }

                    return index.ToList();
                },
                cancellationToken);
        }

        var relinked = 0;
        foreach (var (hash, entry) in missing)
        {
            if (!found.TryGetValue(hash, out var path))
            {
                continue;
            }

            foreach (var (itemId, _, gone) in entry.Owners)
            {
                await _store.Items.ChangeLocalAsync(
                    itemId,
                    current => Replace(current, hash, gone, path),
                    LocalOwners.Import,
                    cancellationToken);
            }

            relinked++;
        }

        await NoteNotFoundAsync(missing, found, cancellationToken);

        return new MissingFileSearchResult
        {
            MissingBefore = missing.Count,
            Relinked = relinked,
            Hashed = hashed,
            Unreachable = unreachable,
        };
    }

    /// <summary>
    /// 無くなったパスを、見つけた場所に差し替える（同じ場所が2つ並ばないようにする）。
    /// <paramref name="gone"/> が空（場所が空だったファイル）なら、見つけた場所を足すだけになる。
    /// </summary>
    /// <remarks>
    /// 錠の中の今の値に当てる。読んでから錠を取るまでに取り込みが同じファイルへ別の場所を足していても、
    /// その場所は消さずに並べる（どちらも同じ中身が実際に在る場所）。その間に人がファイルを外したなら触らない
    /// （探すのは外していないファイルだけ、という決まりを書く時にも当てる）。
    /// </remarks>
    private static LocalBlock? Replace(LocalBlock current, string hash, IReadOnlyList<string> gone, string path)
    {
        var file = current.LocalFiles.FirstOrDefault(entry =>
            string.Equals(entry.Hash, hash, StringComparison.OrdinalIgnoreCase));

        if (file is null || file.Detached)
        {
            return null;
        }

        // 見つけた場所に在るのだから、見つからなくなった日時は消す（ユーザ判断 2026-10-04）。
        // その間に取り込みが同じ場所を足していても、日時が残っていれば消す
        LocalFileRecord next;
        if (file.Paths.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            if (file.MissingSince is null)
            {
                return null;
            }

            next = file with { MissingSince = null };
        }
        else
        {
            var paths = file.Paths.Where(entry => !gone.Contains(entry, StringComparer.OrdinalIgnoreCase)).ToList();
            paths.Add(path);
            next = file with { Paths = paths, MissingSince = null };
        }

        return current with
        {
            LocalFiles = [.. current.LocalFiles.Select(entry => ReferenceEquals(entry, file) ? next : entry)],
        };
    }

    /// <summary>
    /// 探しても見つからなかった物に、見つからなくなった日時を付ける（ユーザ判断 2026-10-05）。
    /// </summary>
    /// <remarks>
    /// 前は結び直した物の日時を消すだけで、見つからなかった物は記録が変わらず、取り込むか使おうとするまで印・検索の条件に出なかった。
    /// 探した今は「どこにも無い」と分かっているので、取り込みの見回りと同じ決まりで書く（<see cref="FileMissingMarks.Apply"/>）：
    /// **つながっていないドライブの上の物には付けない**（外付けを外しているだけかもしれない。<see cref="FilePresenceProbe"/> が見に行かずに分ける）。
    /// 商品ごとの錠の中で今の値に当て、探している間に場所が変わったファイル（取り込みが結び直した物）には当てない。
    /// </remarks>
    private async Task NoteNotFoundAsync(
        Dictionary<string, MissingEntry> missing,
        Dictionary<string, string> found,
        CancellationToken cancellationToken)
    {
        var probe = new FilePresenceProbe();
        var now = DateTimeOffset.Now;
        var sightingsByItem = new Dictionary<string, List<FileSighting>>(StringComparer.Ordinal);

        foreach (var (hash, entry) in missing)
        {
            if (found.ContainsKey(hash))
            {
                continue;
            }

            foreach (var (itemId, paths, _) in entry.Owners)
            {
                if (!sightingsByItem.TryGetValue(itemId, out var sightings))
                {
                    sightingsByItem[itemId] = sightings = [];
                }

                sightings.Add(new FileSighting(hash, paths, probe.Of(paths)));
            }
        }

        foreach (var (itemId, sightings) in sightingsByItem)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _store.Items.ChangeLocalAsync(
                itemId,
                current => FileMissingMarks.Apply(current.LocalFiles, sightings, now) is { } files
                    ? current with { LocalFiles = files }
                    : null,
                LocalOwners.Import,
                cancellationToken);
        }
    }

    private sealed class MissingEntry(long sizeBytes)
    {
        public long SizeBytes { get; } = sizeBytes;

        public List<(string ItemId, IReadOnlyList<string> Paths, IReadOnlyList<string> Gone)> Owners { get; } = [];
    }
}
