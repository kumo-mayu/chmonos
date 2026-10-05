using Chmonos.Core.Models;

namespace Chmonos.Core.Scanning;

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
///
/// **ここは場所に何かが在るかしか見ない**（中身までは見ない）。同じ名前で上書きされた場所を古い中身の記録から外すのは、
/// その場所のハッシュを取った取り込みの仕事（<c>ImportPipeline.DropReplacedPathsAsync</c>・2026-09-30）。
/// </summary>
public static class LocalFileMerger
{
    /// <summary>
    /// 人の登録操作（このIDで登録・BOOTHに無い商品・見つからないIDのまま登録・IDを変える・zipで登録し直す）で足し合わせる。
    /// <see cref="Merge"/> と同じ決まりで、**無い場所を外さない**ことだけが違う（2026-10-05・file-lifecycle.md「気になった所」13）。
    ///
    /// 無い場所を外すのは、取り込みが走査で「移した・消した」と見た時の仕事（import.md）。人が1件登録しただけで、
    /// 同じ商品のほかのファイルの覚えていた場所が消えると、見つからない物は場所を残して日時で示す決まり
    /// （data-model.md「この欄のために場所は外さない」）と食い違い、「見つからないファイルを探す」の手掛かりも消える。
    /// 在る場所が1つでもあれば日時を消すのは同じ（登録した物そのものが在る、など）。
    /// </summary>
    public static IReadOnlyList<LocalFileRecord> MergeByHand(
        IReadOnlyList<LocalFileRecord> existing,
        IEnumerable<LocalFileRecord> discovered,
        Func<string, bool>? pathExists = null)

        // 無い場所を「今は見えないだけ」と同じに扱えば、残す道は1つで済む。ドライブの根も見に行かない
        => Merge(existing, discovered, pathExists, onMissingVolume: _ => true);

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
            var paths = new List<string>(record.Paths.Count);
            var found = false;
            foreach (var path in record.Paths)
            {
                if (exists(path))
                {
                    paths.Add(path);
                    found = true;
                }
                else if (unreachable(path))
                {
                    paths.Add(path);
                }
            }

            // どのパスにも実体が無くなったレコードも残す。
            // 「ファイルが見つからない」として扱い、再スキャンでの復旧やBOOTHからの再取得へ繋げるため。
            // 場所だけを差し替える（欄を1つずつ写すと、記録に欄を足したときにここで落ちる）
            var next = paths.Count == record.Paths.Count ? record : record with { Paths = paths };

            // 在る場所が1つでもあれば、見つからなくなった日時は消す（移した先を取り込んだ・同じ中身を別の所に置いた。
            // ユーザ判断 2026-10-04）。無くなった側は日時を付けない——ここは時計を持たず、日時は記録の場所を全部見る所
            // （FileMissingMarks）が付ける。場所が空になった物は、日時が無くても印と条件に当たる（ItemRecord.HasMissingFile）
            merged.Add(found && next.MissingSince is not null ? next with { MissingSince = null } : next);
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

            // 見つかったかは上（Merge）で場所を見て決める。ここで落とすと、つながっていないドライブの上の場所しか無い物まで消える
            MissingSince = current.MissingSince,
        };
    }
}
