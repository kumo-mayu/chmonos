using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Scanning;

/// <summary>
/// 管理対象から外したファイルの判定。
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

    /// <summary>ハッシュを計算する前の第一段。ここで弾ければ読み込みすら発生しない。</summary>
    public bool IsExcludedByPath(string path) => _paths.Contains(path);

    /// <summary>移動・改名されたファイル向けの第二段。</summary>
    public bool IsExcludedByHash(string hash) => _hashes.Contains(hash);
}
