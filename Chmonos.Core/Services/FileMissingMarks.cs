using Chmonos.Core.Models;
using Chmonos.Core.Scanning;

namespace Chmonos.Core.Services;

/// <summary>
/// ファイル1件について、ディスクを見た結果。<paramref name="Paths"/> は見たときの記録の場所（錠の中で今の場所と比べる）。
/// </summary>
public sealed record FileSighting(string Hash, IReadOnlyList<string> Paths, FilePresence Presence);

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
    /// - 書くのは日時だけで、場所・種類・外した印などほかの欄には触れない。
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
                ? Marked(file, sighting.Presence, now)
                : file;

            changed |= !ReferenceEquals(next, file);
            files.Add(next);
        }

        return changed ? files : null;
    }

    /// <summary>今の記録が、見た結果と食い違うか（命令を出すまでもないかを画面が先に見る）。</summary>
    public static bool Differs(IReadOnlyList<LocalFileRecord> current, IReadOnlyCollection<FileSighting> sightings)
        => Apply(current, sightings, DateTimeOffset.UnixEpoch) is not null;

    private static LocalFileRecord Marked(LocalFileRecord file, FilePresence presence, DateTimeOffset now) => presence switch
    {
        FilePresence.Present when file.MissingSince is not null => file with { MissingSince = null },
        FilePresence.Missing when file.MissingSince is null => file with { MissingSince = now },
        _ => file,
    };

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
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _rootExists;
    private readonly TimeSpan _rootWait;

    public FilePresenceProbe(
        Func<string, bool>? fileExists = null,
        Func<string, bool>? rootExists = null,
        TimeSpan? rootWait = null)
    {
        _fileExists = fileExists ?? DiskCheck.FileExists;
        _rootExists = rootExists ?? DiskCheck.FolderExists;
        _rootWait = rootWait ?? RootWait;
    }

    /// <summary>根を見に行った回数（試験と測りで、ドライブごとに1回かを確かめる）。</summary>
    public int RootChecks { get; private set; }

    public FilePresence Of(IReadOnlyList<string> paths)
        => LocalFilePresence.Of(
            paths,
            path => IsReachable(path) && _fileExists(path),
            path => !IsReachable(path));

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
