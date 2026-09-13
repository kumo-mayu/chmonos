using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// ドライブ文字と通し番号の組を控え、ドライブ文字が変わったボリュームの読み替えを出す（ユーザ判断 2026-09-14）。
///
/// **控えるのは確かなときだけ。**
/// <list type="bullet">
/// <item>取り込み：そのとき記録するパスは今のドライブ文字で書かれるので、組は確か（<see cref="RecordAsync"/>）</item>
/// <item>フォルダビューを開いたとき：記録したパスが今その文字の上に実際に在るときだけ控え直す（<see cref="ObserveAsync"/>）。
///   確かめずに上書きすると、別の外付けが同じ文字に来たときに、記録を取り違える</item>
/// </list>
/// 常に見張ることはしない。
/// </summary>
public sealed class VolumeTable(DataStore store, IVolumeReader reader)
{
    /// <summary>在るかを確かめるパスの数（ドライブ文字ごと）。1本でも在れば、そのボリュームに記録した物と分かる。</summary>
    private const int EvidencePathsPerLetter = 20;

    /// <summary>取り込みとフォルダビューが同時に書くことがあるので、読んで足して書く間を1本にする。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>取り込みで記録したパスのドライブ文字について、今見えているボリュームを控える。</summary>
    public async Task RecordAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        var letters = paths.Select(LetterOf).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (letters.Count == 0)
        {
            return;
        }

        var confirmed = reader.Mounted().Where(volume => letters.Contains(volume.Letter)).ToList();
        await SaveAsync(confirmed, cancellationToken);
    }

    /// <summary>
    /// フォルダビューを開いたとき。ドライブ文字が変わったボリュームの読み替え（元の文字 → 今の文字）を返し、
    /// 記録したパスが今その文字の上に在る組を控え直す。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> ObserveAsync(
        IReadOnlyList<string> recordedPaths,
        CancellationToken cancellationToken = default)
    {
        var mounted = reader.Mounted();
        var known = store.Volumes.Load();
        var remap = Remap(known, mounted);

        var confirmed = new List<MountedVolume>();
        foreach (var group in recordedPaths.Where(path => LetterOf(path) is not null)
                     .GroupBy(path => LetterOf(path)!, StringComparer.OrdinalIgnoreCase))
        {
            // 読み替える文字は控え直さない（そこに今あるのは別のボリューム）
            if (remap.ContainsKey(group.Key)
                || mounted.FirstOrDefault(volume => Same(volume.Letter, group.Key)) is not { } volume
                || known.Any(record => Same(record.Letter, volume.Letter) && record.Serial == volume.Serial))
            {
                continue;
            }

            if (group.Take(EvidencePathsPerLetter).Any(Exists))
            {
                confirmed.Add(volume);
            }
        }

        if (confirmed.Count > 0)
        {
            await SaveAsync(confirmed, cancellationToken);
        }

        return remap;
    }

    /// <summary>
    /// 読み替え。控えた通し番号が、控えたのと違うドライブ文字に見えていれば「元の文字 → 今の文字」。
    /// 元の文字に同じ通し番号がまだ見えていれば読み替えない。どこにも見えていなければ読み替えない（取り外している）。
    /// </summary>
    public static IReadOnlyDictionary<string, string> Remap(
        IReadOnlyList<VolumeRecord> known,
        IReadOnlyList<MountedVolume> mounted)
    {
        var remap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in known)
        {
            if (mounted.Any(volume => Same(volume.Letter, record.Letter) && volume.Serial == record.Serial))
            {
                continue;
            }

            if (mounted.FirstOrDefault(volume => volume.Serial == record.Serial) is { } moved
                && !Same(moved.Letter, record.Letter))
            {
                remap[record.Letter] = moved.Letter.ToUpperInvariant();
            }
        }

        return remap;
    }

    /// <summary>控えに足す。同じドライブ文字の古い組は置き換える。</summary>
    public static IReadOnlyList<VolumeRecord> Merge(
        IReadOnlyList<VolumeRecord> known,
        IEnumerable<MountedVolume> confirmed,
        DateTimeOffset now)
    {
        var fresh = confirmed.ToList();
        return known
            .Where(record => !fresh.Any(volume => Same(volume.Letter, record.Letter)))
            .Concat(fresh.Select(volume => new VolumeRecord
            {
                Letter = volume.Letter.ToUpperInvariant(),
                Serial = volume.Serial,
                Label = volume.Label,
                SeenAt = now,
            }))
            .OrderBy(record => record.Letter, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>記録したパスを、今の場所に読み替える。読み替えが無ければそのまま。</summary>
    public static string Apply(string path, IReadOnlyDictionary<string, string> remap)
        => LetterOf(path) is { } letter && remap.TryGetValue(letter, out var to) ? to + path[2..] : path;

    /// <summary>ドライブ文字（<c>E:</c>）。共有（<c>\\nas\share</c>）など文字の無いパスは null。</summary>
    public static string? LetterOf(string path)
        => path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'
            ? $"{char.ToUpperInvariant(path[0])}:"
            : null;

    private async Task SaveAsync(IReadOnlyList<MountedVolume> confirmed, CancellationToken cancellationToken)
    {
        if (confirmed.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var merged = Merge(store.Volumes.Load(), confirmed, DateTimeOffset.Now);
            await store.Volumes.SaveAsync(merged.ToList(), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool Exists(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
