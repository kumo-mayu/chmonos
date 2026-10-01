using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using static Chmonos.Core.Services.PathText;

namespace Chmonos.Core.Services;

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

    private IReadOnlyDictionary<string, string> _latest = new Dictionary<string, string>();

    /// <summary>
    /// 最後に確かめた読み替え。検索の画面は絞り込むたびに画面のスレッドで使うので、
    /// 通し番号を読み直さず、ここに置いた物を読む（読み直すのは <see cref="RefreshRemap"/> と <see cref="ObserveAsync"/>）。
    /// </summary>
    public IReadOnlyDictionary<string, string> Latest => Volatile.Read(ref _latest);

    /// <summary>記録のパスを、最後に確かめた読み替えで今の場所にする。</summary>
    public string Current(string path) => Apply(path, Latest);

    /// <summary>
    /// 読み替えだけを確かめ直す（表は書かない。控えるのは取り込みとフォルダビューを開いた時・ユーザ判断）。
    /// 通し番号を読むので画面のスレッドの外で呼ぶ。
    /// </summary>
    public IReadOnlyDictionary<string, string> RefreshRemap()
    {
        var remap = Remap(store.Volumes.Load(), reader.Mounted());
        Volatile.Write(ref _latest, remap);
        return remap;
    }

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
        Volatile.Write(ref _latest, remap);

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
    ///
    /// **同じ通し番号が2つ以上の文字に見えているときも読み替えない**（点検 2026-09-23）。
    /// 通し番号はボリュームを作ったときに決まるだけなので、ディスクを丸ごと複製すると同じ番号が2台に付く。
    /// どちらが記録した方かは番号からは分からず、取り違えて別のディスクの下に記録を出すより、出さない方がよい。
    /// 通し番号0（番号を持たない種類のボリューム）も同じ理由で見ない。
    /// </summary>
    public static IReadOnlyDictionary<string, string> Remap(
        IReadOnlyList<VolumeRecord> known,
        IReadOnlyList<MountedVolume> mounted)
    {
        var remap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in known)
        {
            if (!IsDistinctive(record.Serial)
                || mounted.Any(volume => Same(volume.Letter, record.Letter) && volume.Serial == record.Serial))
            {
                continue;
            }

            var seen = mounted.Where(volume => volume.Serial == record.Serial).ToList();
            if (seen.Count == 1 && !Same(seen[0].Letter, record.Letter))
            {
                remap[record.Letter] = seen[0].Letter.ToUpperInvariant();
            }
        }

        return remap;
    }

    /// <summary>
    /// 通し番号でボリュームを見分けられるか。0 は「番号が無い」の意味で、どのボリュームも同じ値になり得る。
    /// </summary>
    public static bool IsDistinctive(string? serial)
        => !string.IsNullOrWhiteSpace(serial) && serial.Trim('0').Length > 0;

    /// <summary>控えに足す。同じドライブ文字の古い組は置き換える。</summary>
    public static IReadOnlyList<VolumeRecord> Merge(
        IReadOnlyList<VolumeRecord> known,
        IEnumerable<MountedVolume> confirmed,
        DateTimeOffset now)
    {
        // 番号で見分けられない組は控えても読み替えに使えない。控えに残すと、その文字の前の正しい組を消してしまう
        var fresh = confirmed.Where(volume => IsDistinctive(volume.Serial)).ToList();
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
}
