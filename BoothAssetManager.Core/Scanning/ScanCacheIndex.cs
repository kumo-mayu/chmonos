using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Scanning;

/// <summary>
/// パス → サイズ・更新日時・ハッシュ のキャッシュ。
/// ライブラリ全体をハッシュし直すと実測で30分近くかかるため、
/// 「パス・サイズ・更新日時が前回と同じなら計算しない」を判断するのがこのクラスの役目。
/// 丸ごと捨てても再計算が走るだけで、データは壊れない。
/// </summary>
public sealed class ScanCacheIndex
{
    private readonly Dictionary<string, ScanCacheEntry> _byPath;

    public ScanCacheIndex(IEnumerable<ScanCacheEntry>? entries = null)
    {
        _byPath = new Dictionary<string, ScanCacheEntry>(StringComparer.OrdinalIgnoreCase);
        if (entries is null)
        {
            return;
        }

        foreach (var entry in entries)
        {
            _byPath[entry.Path] = entry;
        }
    }

    public int Count => _byPath.Count;

    /// <summary>
    /// 記録済みのハッシュを再利用できるかを判定する。
    /// サイズだけでは中身の差し替えを見逃し、更新日時だけではコピーで壊れるので3点で照合する。
    /// </summary>
    public bool TryGetHash(string path, long sizeBytes, DateTimeOffset modifiedAtUtc, out string hash)
    {
        if (_byPath.TryGetValue(path, out var entry)
            && entry.SizeBytes == sizeBytes
            && entry.ModifiedAtUtc == modifiedAtUtc)
        {
            hash = entry.Hash;
            return true;
        }

        hash = string.Empty;
        return false;
    }

    public void Set(string path, long sizeBytes, DateTimeOffset modifiedAtUtc, string hash)
    {
        _byPath[path] = new ScanCacheEntry
        {
            Path = path,
            SizeBytes = sizeBytes,
            ModifiedAtUtc = modifiedAtUtc,
            Hash = hash,
        };
    }

    /// <summary>
    /// 控えてある zip の手掛かり（中のテキストの商品ID）を引く。パス・大きさ・日時が今と合い、ハッシュも同じときだけ。
    /// まだ読んでいなければ false。
    /// </summary>
    public bool TryGetClueItemIds(ScannedFile file, string hash, out IReadOnlyList<string> itemIds)
    {
        if (_byPath.TryGetValue(file.Path, out var entry)
            && entry.SizeBytes == file.SizeBytes
            && entry.ModifiedAtUtc == file.ModifiedAtUtc
            && string.Equals(entry.Hash, hash, StringComparison.OrdinalIgnoreCase)
            && entry.ClueItemIds is { } ids)
        {
            itemIds = ids;
            return true;
        }

        itemIds = [];
        return false;
    }

    /// <summary>読んだ zip の手掛かりを控える。ハッシュの控えが今のファイルと合うときだけ（合わなければ次にまた読む）。</summary>
    public void SetClueItemIds(ScannedFile file, string hash, IReadOnlyList<string> itemIds)
    {
        if (_byPath.TryGetValue(file.Path, out var entry)
            && entry.SizeBytes == file.SizeBytes
            && entry.ModifiedAtUtc == file.ModifiedAtUtc
            && string.Equals(entry.Hash, hash, StringComparison.OrdinalIgnoreCase))
        {
            _byPath[file.Path] = new ScanCacheEntry
            {
                Path = entry.Path,
                SizeBytes = entry.SizeBytes,
                ModifiedAtUtc = entry.ModifiedAtUtc,
                Hash = entry.Hash,
                ClueItemIds = itemIds,
            };
        }
    }

    /// <summary>存在しなくなったパスを落とす。放置するとキャッシュが際限なく育つため。</summary>
    public void RemoveMissing()
    {
        var missing = _byPath.Keys.Where(path => !File.Exists(path)).ToList();
        foreach (var path in missing)
        {
            _byPath.Remove(path);
        }
    }

    public List<ScanCacheEntry> ToList() => _byPath.Values.ToList();
}
