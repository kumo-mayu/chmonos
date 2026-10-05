using Chmonos.Core.Models;
using Chmonos.Core.Scanning;

namespace Chmonos.Core.Services;

/// <summary>
/// ファイル1件について、ディスクを見た結果。<paramref name="Paths"/> は見たときの記録の場所（錠の中で今の場所と比べる）。
/// </summary>
/// <param name="Gone">
/// 無いと確かめた場所。ほかの場所に在ると確かめたときだけ入れる（見回り。<see cref="FilePresenceProbe.Sight"/>）。
/// 使おうとした画面の確かめは入れない（場所を外すのは見回りと取り込みだけ）。
/// </param>
public sealed record FileSighting(string Hash, IReadOnlyList<string> Paths, FilePresence Presence, IReadOnlyList<string>? Gone = null);

/// <summary>
/// ディスクを見た結果を、ファイルの記録の「見つからなくなった日時」（<see cref="LocalFileRecord.MissingSince"/>）に当てる
/// （ユーザ判断 2026-10-04）。取り込みと、使おうとした画面（商品ページ・開く・送る）が同じ決まりで書くよう、ここ1か所にまとめる。
/// </summary>
public static class FileMissingMarks
{
    /// <summary>
    /// 錠の中で読み直した今の一覧に当てる。変える物が無ければ null（書かない。同じ状態を何度も書かない）。
    /// </summary>
    /// <remarks>
    /// - 在る → 日時を消す。無い → 日時が無ければ今を入れる（無い間は最初に見た日時のまま）。
    ///   つながっていないドライブの上にしか場所が無い → 触らない（無くなったとは限らない）。
    /// - **見たときと今で場所が違うファイルには当てない。**見ている間に取り込みや「見つからないファイルを探す」が
    ///   場所を足し替えていれば、見た結果はもう今の場所のことではない（古い答えで「無い」と書くと、見つけた直後に印が戻る）。
    /// - 書くのは日時と、下の無いと確かめた場所だけ。種類・外した印などほかの欄には触れない。
    /// - **ほかの場所に在ると確かめたファイルは、無いと確かめた場所を外す**（<see cref="FileSighting.Gone"/>・2026-10-05・点検の6）。
    ///   同じ中身が2か所にあって片方を消すと、残った方が在るので取り込みの結び直しも <see cref="LocalFileMerger"/> も走らず、
    ///   消した場所が記録に残り続けていた（統計の重複・空けられる量・商品ページの行の名前が消した方の名前のまま）。
    ///   つながっていない・確かめられない場所は外さない。
    /// </remarks>
    public static IReadOnlyList<LocalFileRecord>? Apply(
        IReadOnlyList<LocalFileRecord> current,
        IReadOnlyCollection<FileSighting> sightings,
        DateTimeOffset now)
    {
        var byHash = new Dictionary<string, FileSighting>(StringComparer.OrdinalIgnoreCase);
        foreach (var sighting in sightings)
        {
            byHash[sighting.Hash] = sighting;
        }

        var changed = false;
        var files = new List<LocalFileRecord>(current.Count);
        foreach (var file in current)
        {
            var next = byHash.TryGetValue(file.Hash, out var sighting) && SamePlaces(file.Paths, sighting.Paths)
                ? WithoutGone(Marked(file, sighting.Presence, now), sighting)
                : file;

            changed |= !ReferenceEquals(next, file);
            files.Add(next);
        }

        return changed ? files : null;
    }

    /// <summary>今の記録が、見た結果と食い違うか（命令を出すまでもないかを画面が先に見る）。</summary>
    public static bool Differs(IReadOnlyList<LocalFileRecord> current, IReadOnlyCollection<FileSighting> sightings)
        => Apply(current, sightings, DateTimeOffset.UnixEpoch) is not null;

    /// <summary>
    /// 登録したフォルダを見た結果（場所 → 在るか）を、錠の中で読み直した今の一覧に当てる。変える物が無ければ null。
    /// フォルダは場所が同一性なので、見た後に外された場所は当たる物が無く、そのまま落ちる。
    /// </summary>
    public static IReadOnlyList<LocalFolderRecord>? ApplyFolders(
        IReadOnlyList<LocalFolderRecord> current,
        IReadOnlyDictionary<string, FilePresence> sightings,
        DateTimeOffset now)
    {
        var changed = false;
        var folders = new List<LocalFolderRecord>(current.Count);
        foreach (var folder in current)
        {
            var next = sightings.TryGetValue(folder.Path, out var presence) ? MarkedFolder(folder, presence, now) : folder;
            changed |= !ReferenceEquals(next, folder);
            folders.Add(next);
        }

        return changed ? folders : null;
    }

    /// <summary>
    /// フォルダ1つに、見た結果を当てる（取り込みの数え直しと起動時の見回りが同じ決まりで書くよう、ここ1か所）。
    /// また見つかったら日時を消し、見た日時（<see cref="LocalFolderRecord.LastSeenAt"/>）を今にする。変えなければ同じ物を返す。
    /// 見た日時が空のまま在ると見たときも今を入れる（取り込みの数え直しが空を「変わった」と数えるのと同じ。file-lifecycle.md 気になった所19）。
    /// 在ると見るたびには書き直さない——起動のたびにフォルダを持つ商品を全部書くことになる。
    /// </summary>
    public static LocalFolderRecord MarkedFolder(LocalFolderRecord folder, FilePresence presence, DateTimeOffset now) => presence switch
    {
        FilePresence.Present when folder.MissingSince is not null || folder.LastSeenAt is null
            => folder with { LastSeenAt = now, MissingSince = null },
        FilePresence.Missing when folder.MissingSince is null => folder with { MissingSince = now },
        _ => folder,
    };

    private static LocalFileRecord Marked(LocalFileRecord file, FilePresence presence, DateTimeOffset now) => presence switch
    {
        FilePresence.Present when file.MissingSince is not null => file with { MissingSince = null },
        FilePresence.Missing when file.MissingSince is null => file with { MissingSince = now },
        _ => file,
    };

    /// <summary>ほかの場所に在ると確かめたときだけ、無いと確かめた場所を外す。在る場所は必ず1つ残る。</summary>
    private static LocalFileRecord WithoutGone(LocalFileRecord file, FileSighting sighting)
    {
        if (sighting.Presence != FilePresence.Present || sighting.Gone is not { Count: > 0 } gone)
        {
            return file;
        }

        var kept = file.Paths.Where(path => !gone.Contains(path, StringComparer.OrdinalIgnoreCase)).ToList();
        return kept.Count == file.Paths.Count || kept.Count == 0 ? file : file with { Paths = kept };
    }

    private static bool SamePlaces(IReadOnlyList<string> current, IReadOnlyList<string> seen)
        => current.Count == seen.Count
            && current.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(seen);
}

/// <summary>
/// ファイルが在るかを、ドライブごとにまとめて見る（取り込みが記録の場所を全部見る所・ユーザ判断 2026-10-04）。
/// </summary>
/// <remarks>
/// **つながっていないドライブの上は見ない。**ドライブ（パスの根）がつながっているかを1回だけ見て覚え、
/// つながっていなければその上のファイルは1つも見に行かない。落ちているネットワークドライブは、
/// 1回確かめるだけで数十秒待たされることがあり、ファイルごとに見ると数千倍になる。
/// 根を見るのも <see cref="RootWait"/> で打ち切り、答えが来なければ「つながっていない」として扱う（書かない側に倒す）。
/// **控えた文字に別のディスクが来ていれば、その上も「つながっていない」と同じに扱う**（<see cref="VolumeSnapshot"/>・2026-10-05・点検の2）。
/// 1つの取り込み・1回の画面の確かめの間だけ使う（ドライブは後でつながることがあるので、覚えたまま持ち越さない）。
/// </remarks>
public sealed class FilePresenceProbe
{
    /// <summary>
    /// ドライブの根を待つ長さ。届かない共有の根は、そのまま見ると1回21秒かかった（2026-10-05 に作り物の宛先で測った）。
    /// 手元のドライブは根を含めて5000件を見て約0.12秒なので、待つのは落ちた共有のときだけ。
    /// 長すぎると取り込みの頭で止まって見え、短すぎるとつながっている遅い共有を「外れている」と見て書かないだけ（害は日時が付かないこと）。
    /// </summary>
    public static readonly TimeSpan RootWait = TimeSpan.FromSeconds(3);

    private readonly Dictionary<string, bool> _reachable = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, DiskAnswer> _fileState;
    private readonly Func<string, bool> _rootExists;
    private readonly Func<string, DiskAnswer> _folderState;
    private readonly TimeSpan _rootWait;

    /// <param name="fileExists">在るか無いかだけで答える見方（試験）。<paramref name="fileState"/> が優先。</param>
    /// <param name="volumes">
    /// 控えたボリュームと今のボリュームの写し。無ければ根がつながっているかだけで見る（控えを渡さない組み立て・試験）。
    /// </param>
    /// <param name="fileState">
    /// 在る・無い・確かめられない（権限が無い等）で答える見方。既定は <see cref="DiskCheck.FileState"/>。試験が拒まれた場所を作るために差し替える。
    /// </param>
    public FilePresenceProbe(
        Func<string, bool>? fileExists = null,
        Func<string, bool>? rootExists = null,
        TimeSpan? rootWait = null,
        Func<string, bool>? folderExists = null,
        VolumeSnapshot? volumes = null,
        Func<string, DiskAnswer>? fileState = null,
        Func<string, DiskAnswer>? folderState = null)
        : this(
            fileState ?? Answer(fileExists) ?? DiskCheck.FileState,
            rootExists ?? DiskCheck.FolderExists,
            rootWait ?? RootWait,
            folderState ?? Answer(folderExists) ?? DiskCheck.FolderState,
            volumes ?? VolumeSnapshot.Empty)
    {
    }

    private FilePresenceProbe(
        Func<string, DiskAnswer> fileState,
        Func<string, bool> rootExists,
        TimeSpan rootWait,
        Func<string, DiskAnswer> folderState,
        VolumeSnapshot volumes)
    {
        _fileState = fileState;
        _rootExists = rootExists;
        _rootWait = rootWait;
        _folderState = folderState;
        Volumes = volumes;
    }

    private static Func<string, DiskAnswer>? Answer(Func<string, bool>? exists)
        => exists is null ? null : path => exists(path) ? DiskAnswer.Present : DiskAnswer.Missing;

    /// <summary>控えたボリュームの写し。</summary>
    public VolumeSnapshot Volumes { get; }

    /// <summary>根を見に行った回数（試験と測りで、ドライブごとに1回かを確かめる）。</summary>
    public int RootChecks { get; private set; }

    /// <summary>
    /// 同じ見方・同じボリュームの写しで、根の覚えだけを空にした物。取り込みが商品へ足すのは、周回の頭で根を見てから
    /// BOOTH から取る数分後のことがあり、その間に外付けを外すと、覚えた「つながっている」のまま「無い」と見て場所を外してしまう。
    /// ボリュームの写しは周回の頭の物を使い続ける（その周回で取り込んだ文字を控え直した後の写しでは、控えた文字に来た別のディスクを見分けられない）。
    /// </summary>
    public FilePresenceProbe Renewed() => new(_fileState, _rootExists, _rootWait, _folderState, Volumes);

    /// <summary>
    /// 記録の場所のどれかに在るか（場所を全部見て1つにまとめる）。1つ在れば在る。在る場所が無ければ、つながっていない場所があれば
    /// <see cref="FilePresence.OnDetachedDrive"/>、確かめられない場所があれば <see cref="FilePresence.Unverifiable"/>、どれも無ければ無い。
    /// </summary>
    /// <param name="remap">見る場所（ドライブ文字が変わった分の読み替え。<see cref="VolumeTable.Current"/>）。無ければ記録の場所のまま。</param>
    public FilePresence Of(IReadOnlyList<string> paths, Func<string, string>? remap = null)
    {
        var detached = false;
        var unverifiable = false;
        foreach (var path in paths)
        {
            switch (PlaceOf(path, remap?.Invoke(path)))
            {
                case FilePresence.Present:
                    return FilePresence.Present;
                case FilePresence.OnDetachedDrive:
                    detached = true;
                    break;
                case FilePresence.Unverifiable:
                    unverifiable = true;
                    break;
            }
        }

        return detached ? FilePresence.OnDetachedDrive
            : unverifiable ? FilePresence.Unverifiable
            : FilePresence.Missing;
    }

    /// <summary>
    /// 見回りの見方：ファイルの場所を全部見て、在るかと、ほかの場所に在るときは無いと確かめた場所（<see cref="FileSighting.Gone"/>）を返す。
    /// 1つ在れば在るので、そこで止めずに全部見る（同じ中身の片方を消した場所を見つけるため。点検の6）。
    /// </summary>
    public FileSighting Sight(LocalFileRecord file)
    {
        var places = file.Paths.Select(path => (Path: path, Presence: PlaceOf(path))).ToList();
        var presence = places.Any(place => place.Presence == FilePresence.Present) ? FilePresence.Present
            : places.Any(place => place.Presence == FilePresence.OnDetachedDrive) ? FilePresence.OnDetachedDrive
            : places.Any(place => place.Presence == FilePresence.Unverifiable) ? FilePresence.Unverifiable
            : FilePresence.Missing;
        IReadOnlyList<string>? gone = presence == FilePresence.Present
            ? [.. places.Where(place => place.Presence == FilePresence.Missing).Select(place => place.Path)]
            : null;
        return new FileSighting(file.Hash, file.Paths, presence, gone);
    }

    /// <summary>
    /// 記録の場所1つ。<paramref name="lookedPath"/> は実際に見る場所（読み替えた後。無ければ記録の場所）。
    /// つながっていない・控えたのと別のディスクの上なら、ファイルは見に行かず <see cref="FilePresence.OnDetachedDrive"/>。
    /// 親のフォルダを読む権限が無いなど確かめられなければ <see cref="FilePresence.Unverifiable"/>（無いとは言わない。点検の13）。
    /// </summary>
    public FilePresence PlaceOf(string path, string? lookedPath = null)
    {
        var looked = lookedPath ?? path;
        if (!IsReachable(looked) || Volumes.IsForeign(path, looked))
        {
            return FilePresence.OnDetachedDrive;
        }

        return Presence(_fileState(looked));
    }

    /// <summary>
    /// 登録したフォルダ1つが在るか（起動時の見回り・ユーザ判断 2026-10-05）。ファイルと同じく、
    /// つながっていないドライブ（控えたのと別のディスクを含む）の上なら見に行かず <see cref="FilePresence.OnDetachedDrive"/>（書かない側）。
    /// 確かめられなければ <see cref="FilePresence.Unverifiable"/>（書かない側）。
    /// </summary>
    public FilePresence OfFolder(string path)
        => !IsReachable(path) || Volumes.IsForeign(path, path)
            ? FilePresence.OnDetachedDrive
            : Presence(_folderState(path));

    private static FilePresence Presence(DiskAnswer answer) => answer switch
    {
        DiskAnswer.Present => FilePresence.Present,
        DiskAnswer.Missing => FilePresence.Missing,
        _ => FilePresence.Unverifiable,
    };

    private bool IsReachable(string path)
    {
        string? root;
        try
        {
            root = Path.GetPathRoot(path);
        }
        catch (ArgumentException)
        {
            // 名前として読めない場所は見に行けない（UnresolvedMerge.IsOnMissingVolume と同じく、見えない側に倒す）
            return false;
        }

        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        if (!_reachable.TryGetValue(root, out var reachable))
        {
            RootChecks++;
            var check = Task.Run(() => _rootExists(root));
            reachable = check.Wait(_rootWait) && check.Result;
            _reachable[root] = reachable;
        }

        return reachable;
    }
}
