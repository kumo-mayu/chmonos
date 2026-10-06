using Chmonos.Core.Models;

namespace Chmonos.Core.Scanning;

/// <summary>
/// 取り込みが未確定の一覧（unresolved.json）を書くときの決まり（技術的負債 1-2・1-3、2026-09-14）。
///
/// 取り込みは周回で見つけた物で一覧を作り直す。ただし一覧は取り込みの最中に人も書く（未確定の画面で割り当てる・
/// 商品ページで外して戻す）ので、作り直した物で丸ごと上書きすると、人の変更が消えていた
/// （割り当てた物が未確定に戻る・外して戻した物が消える）。
/// そこで、この取り込みが前に書いた（始めに読んだ）一覧と今の一覧を比べ、その間の人の変更を残す。
///
/// もう1つ、**外付けを外したまま取り込むと、そこにあった未確定が消えていた。**走査はつながっていないフォルダを
/// 「中身なし」として返すので、見つからなかった＝片付いた、と区別が付かない。取り外したドライブは灰色で残す方針
/// （監視・フォルダビューと同じ）なので、つながっていないボリュームの取り込み元の下にある物は引き継ぐ。
/// </summary>
public static class UnresolvedMerge
{
    /// <param name="current">今の一覧（錠の中で読んだ物）。</param>
    /// <param name="lastWritten">この取り込みが前に書いた一覧。まだ書いていなければ始めに読んだ一覧。</param>
    /// <param name="found">この取り込みがここまでに未確定と判じた物。</param>
    /// <param name="scannedTargets">この取り込みがここまでに走査した取り込み元（ファイルかフォルダ）。</param>
    /// <param name="unseenPlaces">
    /// 走査した取り込み元の中で、今回見ていない場所（ファイルかフォルダ）。ボリュームがつながっていない取り込み元・
    /// 中を読めなかったフォルダ・オンラインのみのファイル・ハッシュを取れなかったファイル。
    /// </param>
    public static List<UnresolvedFile> ForImport(
        IReadOnlyList<UnresolvedFile> current,
        IReadOnlyList<UnresolvedFile> lastWritten,
        IReadOnlyList<UnresolvedFile> found,
        RegisteredFolderSet scannedTargets,
        RegisteredFolderSet unseenPlaces)
    {
        var before = Hashes(lastWritten);
        var now = Hashes(current);
        var result = new List<UnresolvedFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var foundAt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in found)
        {
            // 前に書いた後で人が一覧から外した物（割り当てた・管理から外した）は戻さない
            var removedByPerson = before.Contains(file.Hash) && !now.Contains(file.Hash);
            if (removedByPerson)
            {
                continue;
            }

            // **同じ中身の2か所目は、1か所目の行に場所を足す**（file-lifecycle.md 気になった所8）。
            // 取り込みは1ファイル1件で判じるので、前は最初の1件だけが残り、2か所目はフォルダビューの「?」にも出なかった。
            // 記録の同一性はハッシュで、商品の記録と同じく1件に場所を複数持つ
            if (foundAt.TryGetValue(file.Hash, out var index))
            {
                result[index] = WithPaths(result[index], file.Paths, file.SamePathItemIds);
                continue;
            }

            seen.Add(file.Hash);
            foundAt[file.Hash] = result.Count;
            result.Add(file);
        }

        foreach (var file in current)
        {
            if (foundAt.TryGetValue(file.Hash, out var index))
            {
                // 今回見ていない場所（走査していない取り込み元・外付けを外していた）は、見つかった行に引き継ぐ。
                // 見た場所で見つからなかった物だけが片付いた物（下の決まりと同じ）
                var unseenPaths = file.Paths
                    .Where(path => !scannedTargets.Contains(path) || unseenPlaces.Contains(path))
                    .ToList();
                result[index] = WithPaths(result[index], unseenPaths, []);
                continue;
            }

            if (seen.Contains(file.Hash))
            {
                continue;
            }

            // 前に書いた後で人が足した物（外して戻した）と、今回見ていない物は残す。
            // 見ていないのは、今回走査していない取り込み元の物（対象は「今積んだ物」だけ・G1）と、外付けを外していて見られなかった物、
            // 走査した中でも読めなかったフォルダの下・オンラインのみ・ハッシュを取れなかった物（見つからない・移動の点検 4・2026-10-05。
            // 前はこれらを片付いたとして落とし、控えに載っているので監視も拾い直さず、どこにも出なくなっていた）。
            // 走査した対象の中で見つからなかった物だけが、この取り込みが判じ直した（片付いた・消えた）物なので落とす。
            // 前は外付けだけを見ていて、フォルダ乙を取り込むとフォルダ甲の未確定が消え、甲の物は走査の控えに載るので
            // 監視も新しいと数えず、商品にも未確定にも出なくなっていた（大容量の確かめ E・2026-09-30）
            var addedByPerson = !before.Contains(file.Hash);
            var unseen = file.Paths.Any(path => !scannedTargets.Contains(path) || unseenPlaces.Contains(path));
            if ((addedByPerson || unseen) && seen.Add(file.Hash))
            {
                result.Add(file);
            }
        }

        return result;
    }

    /// <summary>
    /// そのパスのボリューム（ドライブ・共有）がつながっていないか。
    /// フォルダが消えただけ（ボリュームはある）なら false——それは片付いたと判じてよい。
    /// </summary>
    public static bool IsOnMissingVolume(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && !Directory.Exists(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }

    /// <summary>場所（と、同じ場所にあった商品）を重ならないように足す。足す物が無ければ元のまま。</summary>
    private static UnresolvedFile WithPaths(UnresolvedFile file, IReadOnlyList<string> paths, IReadOnlyList<string> samePathItemIds)
    {
        var newPaths = paths.Where(path => !file.Paths.Contains(path, StringComparer.OrdinalIgnoreCase)).ToList();
        var newIds = samePathItemIds.Where(id => !file.SamePathItemIds.Contains(id, StringComparer.Ordinal)).ToList();
        if (newPaths.Count == 0 && newIds.Count == 0)
        {
            return file;
        }

        return new UnresolvedFile
        {
            Hash = file.Hash,
            Paths = [.. file.Paths, .. newPaths],
            SizeBytes = file.SizeBytes,
            ModifiedAtUtc = file.ModifiedAtUtc,
            FirstSeenAt = file.FirstSeenAt,
            Contents = file.Contents,
            ZoneHostUrl = file.ZoneHostUrl,
            ZoneReferrerUrl = file.ZoneReferrerUrl,
            CandidateItemIds = file.CandidateItemIds,
            SamePathItemIds = [.. file.SamePathItemIds, .. newIds],
            ArchiveBroken = file.ArchiveBroken,
            NotOnBooth = file.NotOnBooth,
        };
    }

    private static HashSet<string> Hashes(IReadOnlyList<UnresolvedFile> files)
        => files.Select(file => file.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
