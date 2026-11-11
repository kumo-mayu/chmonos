using Chmonos.Core.Storage;

namespace Chmonos.Query;

/// <summary>
/// zip のハッシュごとの控えのフォルダ（<c>unitypackages/</c>・<c>zipimages/</c>）を引く表：鍵 → それを含む控えの番号（plan 32）。
/// 全部の控えを読んで作り、フォルダが変わるまで使い回す。
///
/// **持つのは 128 ビットの鍵と控えの番号だけ**（中のパスも商品も持たない）。中身は当たった控えだけを読み直して取り、商品は要求ごとの写しで引く。
/// パスまで持つと、ui の写しを25倍にした作り物（控え 2,000・GUID 約8万）で +34MB、鍵と番号だけなら +4.7MB だった（docs/spec/local-api-stored.md）。
/// フォルダの印（数・大きさの合計・最新の日時）は引くたびに見る：取り込みの裏が書き足すのを、見張りの対象（<see cref="StoreChanges"/>）に足さずに拾うため。
/// </summary>
/// <param name="directory">控えのフォルダ。中の <c>&lt;ハッシュ&gt;.json</c> だけを読む。</param>
/// <param name="keysOf">控え1つの鍵（読めなければ null）。</param>
internal sealed class HashKeyedIndex(string directory, Func<string, IEnumerable<UInt128>?> keysOf)
{
    private readonly object _gate = new();
    private string? _stamp;
    private string[] _hashes = [];

    // 1つの控えにしか無い鍵がほとんどなので、2つ目からは別の表に分ける（全部に一覧を持たせると1件ごとに配列の分が増える）
    private Dictionary<UInt128, int> _first = [];
    private Dictionary<UInt128, List<int>> _more = [];

    public int KeyCount { get { lock (_gate) { return _first.Count; } } }

    public int FileCount { get { lock (_gate) { return _hashes.Length; } } }

    /// <summary>鍵ごとの、それを含む控えのハッシュ（無い鍵は入らない）。表は錠の中で今の控えに合わせてから引く。</summary>
    public Dictionary<UInt128, IReadOnlyList<string>> Find(IEnumerable<UInt128> keys)
    {
        lock (_gate)
        {
            Refresh();
            var result = new Dictionary<UInt128, IReadOnlyList<string>>();
            foreach (var key in keys)
            {
                if (result.ContainsKey(key) || !_first.TryGetValue(key, out var first))
                {
                    continue;
                }

                result[key] = [_hashes[first], .. (_more.GetValueOrDefault(key) ?? []).Select(index => _hashes[index])];
            }

            return result;
        }
    }

    private void Refresh()
    {
        var files = ListFiles();
        var stamp = $"{files.Count}:{files.Sum(file => file.Length)}:{(files.Count == 0 ? 0 : files.Max(file => file.LastWriteTimeUtc.Ticks))}";
        if (stamp == _stamp)
        {
            return;
        }

        var hashes = new List<string>(files.Count);
        var first = new Dictionary<UInt128, int>();
        var more = new Dictionary<UInt128, List<int>>();
        foreach (var file in files)
        {
            var hash = Path.GetFileNameWithoutExtension(file.Name);
            if (keysOf(hash) is not { } keys)
            {
                continue;
            }

            var index = hashes.Count;
            hashes.Add(hash);
            foreach (var key in keys)
            {
                if (!first.TryAdd(key, index) && first[key] != index)
                {
                    var list = more.TryGetValue(key, out var known) ? known : more[key] = [];
                    if (!list.Contains(index))
                    {
                        list.Add(index);
                    }
                }
            }
        }

        first.TrimExcess();
        _hashes = [.. hashes];
        _first = first;
        _more = more;
        _stamp = stamp;
    }

    private List<FileInfo> ListFiles()
    {
        try
        {
            var info = new DirectoryInfo(directory);
            return info.Exists
                ? info.EnumerateFiles("*.json").Where(file => StoreIds.IsPackageHash(Path.GetFileNameWithoutExtension(file.Name))).ToList()
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
