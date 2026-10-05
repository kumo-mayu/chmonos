using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 控えた「ドライブ文字と通し番号の組」（<c>volumes.json</c>）と、今つながっているボリュームを1回だけ読んだ写し。
/// 在るかを見る所（<see cref="FilePresenceProbe"/>）が、1回の見回り・取り込みの1周回・1回の確かめの間だけ使う。
/// </summary>
/// <remarks>
/// **控えた文字に、控えたのと別のボリュームが来ていたら、その文字の上の記録は「つながっていない」と同じに扱う**（2026-10-05・点検の2）。
/// 外付けA（E:）を外して別のディスクBが E: に来ると、根（<c>E:\</c>）はつながっているので、根だけを見ていた頃は
/// Aの上の全ファイルを「無い」と見て、見回りが日時を付け、取り込みが場所を外していた。Aを挿し直しても場所は戻らない。
///
/// - 控えの無い文字・番号で見分けられない控え（0）・今その文字の番号が読めない（ネットワークドライブ・subst）は今のまま（別とは言えない）。
/// - 見る場所が読み替えた後の文字のとき（商品ページ。<see cref="VolumeTable.Current"/>）は、記録の文字の控えと、見る文字の今の番号を比べる。
///   読み替えは「控えた番号が今その文字に見えている」ときだけ起きるので、読み替えた先は別とは見ない。
/// - 1回だけ読むのは、ボリュームの一覧を読むのにドライブごとの問い合わせが要るため（ファイルごとに読み直さない）。
/// </remarks>
public sealed class VolumeSnapshot
{
    public static readonly VolumeSnapshot Empty = new([], []);

    /// <summary>文字 → 控えた通し番号（番号で見分けられる物だけ）。</summary>
    private readonly Dictionary<string, string> _recorded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>文字 → 今その文字に見えているボリュームの通し番号。</summary>
    private readonly Dictionary<string, string> _mounted = new(StringComparer.OrdinalIgnoreCase);

    public VolumeSnapshot(IReadOnlyList<VolumeRecord> known, IReadOnlyList<MountedVolume> mounted)
    {
        foreach (var record in known.Where(record => VolumeTable.IsDistinctive(record.Serial)))
        {
            if (VolumeTable.LetterOf(record.Letter) is { } letter)
            {
                _recorded[letter] = record.Serial;
            }
        }

        foreach (var volume in mounted)
        {
            if (VolumeTable.LetterOf(volume.Letter) is { } letter)
            {
                _mounted.TryAdd(letter, volume.Serial);
            }
        }
    }

    /// <summary>
    /// 記録の場所 <paramref name="recordedPath"/> を <paramref name="lookedPath"/> で見るとき、そこに今あるのが控えたのと別のボリュームか。
    /// 別なら、その場所の答えは「つながっていない」（在るとも無いとも言えない）。
    /// </summary>
    public bool IsForeign(string recordedPath, string lookedPath)
        => VolumeTable.LetterOf(recordedPath) is { } from
           && _recorded.TryGetValue(from, out var expected)
           && VolumeTable.LetterOf(lookedPath) is { } at
           && _mounted.TryGetValue(at, out var actual)
           && !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
}
