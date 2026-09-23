using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Scanning;

/// <summary>
/// 管理対象から外したファイルの判定。
/// **外したのはその中身で、場所ではない**（ユーザ判断 2026-09-23）。同一性はハッシュで決まる。
/// パスは計算ゼロで弾くためのショートカット、ハッシュは移動されても効かせるための最終判定という役割分担。
/// この2段構えのおかげで、除外済みファイルのために毎回ハッシュを計算せずに済む。
/// </summary>
public sealed class ExclusionFilter
{
    private readonly HashSet<string> _paths;
    private readonly HashSet<string> _hashes;

    public ExclusionFilter(IEnumerable<ExcludedEntry>? entries = null)
    {
        _paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (entries is null)
        {
            return;
        }

        foreach (var entry in entries)
        {
            _hashes.Add(entry.Hash);
            foreach (var path in entry.Paths)
            {
                _paths.Add(path);
            }
        }
    }

    /// <summary>
    /// ハッシュを計算する前の第一段。ここで弾ければ読み込みすら発生しない。
    ///
    /// **パスが一致するだけでは外さない。**そのパスの走査の控えが今の大きさ・更新日時と合い、
    /// 控えのハッシュが外した物であるときだけ外す（控えの3点照合をそのまま使う）。
    /// 前はパスだけで弾いていたので、同じ名前で落とし直した更新版まで外し続けていた。
    /// 合わない（控えが無い・大きさか日時が違う）ときは false を返し、ハッシュを取り直して
    /// <see cref="IsExcludedByHash"/> に任せる——同じ中身なら外れ、別の中身なら新しい物として取り込まれる。
    /// 除外の記録に大きさや日時の欄を足さずに済むのは、控えが既にそれを持っているため。
    /// </summary>
    public bool IsExcludedWithoutHashing(ScannedFile file, ScanCacheIndex cache)
        => _paths.Contains(file.Path)
            && cache.TryGetHash(file.Path, file.SizeBytes, file.ModifiedAtUtc, out var hash)
            && _hashes.Contains(hash);

    /// <summary>移動・改名されたファイル向けの第二段。</summary>
    public bool IsExcludedByHash(string hash) => _hashes.Contains(hash);
}
