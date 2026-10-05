using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// ファイルの記録の「場所 → ディスクの通し番号」（<see cref="LocalFileRecord.Volumes"/>）を読む・書き足す（2026-10-05・点検の3・ユーザ判断 3-A）。
/// </summary>
/// <remarks>
/// **書くのは、その場所に在ると確かめたときだけ**（取り込みが走査で読んだ・見回りや取り込みが在ると見た）。
/// 欄の無い記録はそのとき埋まる（古いデータに合わせる救済ではなく、ふつうの書き込み）。在ると見ていない場所に、今その文字に来ているディスクを書くと、
/// 別のディスクを記録のディスクと取り違える（それを防ぐための欄なので）。
/// 場所は大文字小文字を区別せずに引く（手で直した JSON でも引けるように）。場所を外した後に残った番号は、書き足すときに落とす。
/// </remarks>
public static class PlaceVolumes
{
    /// <summary>その場所のディスクの通し番号。分からなければ null（控え <c>volumes.json</c> で見る今までの見方になる）。</summary>
    public static string? Of(LocalFileRecord file, string path)
    {
        if (file.Volumes is not { Count: > 0 } volumes)
        {
            return null;
        }

        if (volumes.TryGetValue(path, out var serial))
        {
            return VolumeTable.IsDistinctive(serial) ? serial : null;
        }

        foreach (var (place, value) in volumes)
        {
            if (string.Equals(place, path, StringComparison.OrdinalIgnoreCase))
            {
                return VolumeTable.IsDistinctive(value) ? value : null;
            }
        }

        return null;
    }

    /// <summary>
    /// 番号を書き足す。<paramref name="overwrite"/> が false なら、もう番号のある場所は変えない（見回り。在ると見たのは控えたディスクの上なので同じ番号）。
    /// true なら置き換える（取り込みが今そのディスクから読んだ）。今の場所に無い番号は落とす。変わらなければ同じ物を返す。
    /// </summary>
    public static LocalFileRecord With(LocalFileRecord file, IEnumerable<KeyValuePair<string, string>> seen, bool overwrite)
    {
        var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in file.Paths)
        {
            if (Of(file, path) is { } known)
            {
                next[path] = known;
            }
        }

        foreach (var (path, serial) in seen)
        {
            if (!VolumeTable.IsDistinctive(serial)
                || file.Paths.FirstOrDefault(place => string.Equals(place, path, StringComparison.OrdinalIgnoreCase)) is not { } place
                || (!overwrite && next.ContainsKey(place)))
            {
                continue;
            }

            next[place] = serial;
        }

        return Same(file.Volumes, next)
            ? file
            : file with { Volumes = next.Count == 0 ? null : new SortedDictionary<string, string>(next, StringComparer.OrdinalIgnoreCase) };
    }

    /// <summary>今読んだ場所全部に、今その文字に来ているディスクの番号を書く（取り込みが走査で見つけた記録）。</summary>
    public static LocalFileRecord Stamped(LocalFileRecord file, VolumeSnapshot volumes)
        => With(
            file,
            file.Paths.Select(path => volumes.SerialAt(path) is { } serial ? new KeyValuePair<string, string>(path, serial) : default)
                .Where(pair => pair.Key is not null),
            overwrite: true);

    private static bool Same(IReadOnlyDictionary<string, string>? before, Dictionary<string, string> after)
    {
        var count = before?.Count ?? 0;
        if (count != after.Count)
        {
            return false;
        }

        return after.All(pair => before!.TryGetValue(pair.Key, out var value) && string.Equals(value, pair.Value, StringComparison.Ordinal));
    }
}
