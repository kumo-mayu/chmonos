using Chmonos.Core.Models;

namespace Chmonos.Core.Scanning;

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
        Touch(path);
    }

    /// <summary>
    /// 読んだ後に足した・直したパスと、落としたパス。書くときはこれだけを**今の控えに重ねる**（<see cref="MergeInto"/>）。
    ///
    /// 控えは取り込みのほかに、見つからないファイルを探す所も書く。読んだ時の写しで丸ごと書くと、その間に相手が足した分を消す。
    /// </summary>
    private readonly HashSet<string> _changed = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase);

    private void Touch(string path)
    {
        _changed.Add(path);
        _removed.Remove(path);
    }

    /// <summary>書く物が残っているか。</summary>
    public bool HasChanges => _changed.Count > 0 || _removed.Count > 0;

    /// <summary>
    /// 今の控え（<paramref name="current"/>）に、ここで足した・直した・落とした分だけを重ねた一覧。
    /// 錠の中（<c>JsonFileStore.UpdateAsync</c>）で呼ぶ。重ねた分は「書いた」として忘れる。
    /// </summary>
    public List<ScanCacheEntry> MergeInto(IEnumerable<ScanCacheEntry> current)
    {
        var merged = new Dictionary<string, ScanCacheEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in current)
        {
            merged[entry.Path] = entry;
        }

        foreach (var path in _changed)
        {
            if (_byPath.TryGetValue(path, out var entry))
            {
                merged[path] = entry;
            }
        }

        foreach (var path in _removed)
        {
            merged.Remove(path);
        }

        _changed.Clear();
        _removed.Clear();
        return [.. merged.Values];
    }

    /// <summary>
    /// 取り込み元（<paramref name="roots"/>）の下で、もう無いパスを落とす。**つながっていないボリュームの上のパスは落とさない**
    /// （外付けを外している間は「無くなった」ではなく「今は見えない」。つなぎ直したときに全部を読み直すことになる）。
    ///
    /// 前は落とす所がどこからも呼ばれず、移した・消したファイルの控えが際限なく残っていた。
    /// 取り込み元の下だけを見るのは、ほかの場所（見つからないファイルを探した監視フォルダ）の控えを
    /// その取り込みと関係なく確かめて回らないため。
    /// </summary>
    /// <returns>落とした数。</returns>
    public int RemoveMissingUnder(
        IEnumerable<string> roots,
        Func<string, bool>? exists = null,
        Func<string, bool>? onMissingVolume = null)
    {
        // 取り込み元はフォルダのことも、落としたファイル1つのこともある。ドライブの直下（D:\）も取り込み元になり得る
        var prefixes = roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.TrimEndingDirectorySeparator(root.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var isThere = exists ?? File.Exists;
        var unreachable = onMissingVolume ?? UnresolvedMerge.IsOnMissingVolume;

        bool Under(string path) => prefixes.Any(root =>
            path.Equals(root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(
                Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));

        var gone = _byPath.Keys
            .Where(path => Under(path) && !isThere(path) && !unreachable(path))
            .ToList();
        foreach (var path in gone)
        {
            _byPath.Remove(path);
            _changed.Remove(path);
            _removed.Add(path);
        }

        return gone.Count;
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
            Touch(file.Path);
        }
    }

    public List<ScanCacheEntry> ToList() => _byPath.Values.ToList();
}
