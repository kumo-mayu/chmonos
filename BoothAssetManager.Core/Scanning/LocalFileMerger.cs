using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Scanning;

/// <summary>
/// 既に記録しているファイルと、走査で見つかったファイルを突き合わせる。
///
/// 同一性はハッシュなので、同じ中身が複数箇所にあれば1レコードが複数のパスを持つ。
/// ファイルを移動した場合も「同じレコードのパスが差し替わった」として扱えるよう、
/// 実在しなくなったパスは落とす。
///
/// ただし**つながっていないボリューム（外付けを外している・NAS が落ちている）の上のパスは残す。**
/// そこは「無くなった」のではなく「今は見えない」だけで、外している間に同じ商品へ別のファイルを足すと
/// 外付けの上の記録が消えていた（点検 2026-09-23）。未確定の一覧の引き継ぎ
/// （<see cref="UnresolvedMerge.IsOnMissingVolume"/>）と同じ考え。
/// </summary>
public static class LocalFileMerger
{
    public static IReadOnlyList<LocalFileRecord> Merge(
        IReadOnlyList<LocalFileRecord> existing,
        IEnumerable<LocalFileRecord> discovered,
        Func<string, bool>? pathExists = null,
        Func<string, bool>? onMissingVolume = null)
    {
        var exists = pathExists ?? File.Exists;
        var unreachable = onMissingVolume ?? UnresolvedMerge.IsOnMissingVolume;
        var byHash = new Dictionary<string, LocalFileRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in existing)
        {
            byHash[record.Hash] = record;
        }

        foreach (var record in discovered)
        {
            byHash[record.Hash] = byHash.TryGetValue(record.Hash, out var current)
                ? Combine(current, record)
                : record;
        }

        var merged = new List<LocalFileRecord>();
        foreach (var record in byHash.Values)
        {
            var paths = record.Paths.Where(path => exists(path) || unreachable(path)).ToList();

            // どのパスにも実体が無くなったレコードも残す。
            // 「ファイルが見つからない」として扱い、再スキャンでの復旧やBOOTHからの再取得へ繋げるため。
            // 場所だけを差し替える（欄を1つずつ写すと、記録に欄を足したときにここで落ちる）
            merged.Add(paths.Count == record.Paths.Count ? record : record with { Paths = paths });
        }

        return merged;
    }

    private static LocalFileRecord Combine(LocalFileRecord current, LocalFileRecord discovered)
    {
        var paths = current.Paths
            .Concat(discovered.Paths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new LocalFileRecord
        {
            Hash = current.Hash,
            Paths = paths,
            SizeBytes = discovered.SizeBytes,

            // variationの紐付けとアーカイブ内容はユーザ入力や解析の結果なので、既存を優先して失わない。
            VariationId = current.VariationId ?? discovered.VariationId,
            Contents = current.Contents.Count > 0 ? current.Contents : discovered.Contents,

            // 走査で見つけた方はまだ読んでいない（null）。読んだ結果を失わない
            UnityPackages = current.UnityPackages ?? discovered.UnityPackages,

            // 外した印は、両方が外したものだったときだけ残す。外したファイルをこの商品へ選び直した
            // （未確定から同じ商品を選んだ）なら、ユーザが改めて決めたのだから印を下ろす
            Detached = current.Detached && discovered.Detached,

            // 開けなかった印は、新しく見た方の答えに合わせる。同じ中身は何度開いても同じ答えになるが、
            // 見つけた方が開いていない（ほかのアプリが開いていた・開かずに場所だけ足した。中身の一覧が空）ときは
            // 壊れていないと分かったわけではないので、今の印を残す
            ArchiveBroken = discovered.ArchiveBroken || (current.ArchiveBroken && discovered.Contents.Count == 0),
        };
    }
}
