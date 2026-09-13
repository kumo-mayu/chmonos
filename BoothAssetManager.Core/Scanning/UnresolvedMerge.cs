using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Scanning;

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
    /// <param name="offlineTargets">取り込み元のうち、ボリュームがつながっていない物。</param>
    public static List<UnresolvedFile> ForImport(
        IReadOnlyList<UnresolvedFile> current,
        IReadOnlyList<UnresolvedFile> lastWritten,
        IReadOnlyList<UnresolvedFile> found,
        RegisteredFolderSet offlineTargets)
    {
        var before = Hashes(lastWritten);
        var now = Hashes(current);
        var result = new List<UnresolvedFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in found)
        {
            // 前に書いた後で人が一覧から外した物（割り当てた・管理から外した）は戻さない
            var removedByPerson = before.Contains(file.Hash) && !now.Contains(file.Hash);
            if (!removedByPerson && seen.Add(file.Hash))
            {
                result.Add(file);
            }
        }

        foreach (var file in current)
        {
            if (seen.Contains(file.Hash))
            {
                continue;
            }

            // 前に書いた後で人が足した物（外して戻した）と、外付けを外していて今回見られなかった物は残す。
            // それ以外で今回見つからなかった物は、この取り込みが判じ直した（片付いた・消えた）ので落とす
            var addedByPerson = !before.Contains(file.Hash);
            var unseen = file.Paths.Any(offlineTargets.Contains);
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

    private static HashSet<string> Hashes(IReadOnlyList<UnresolvedFile> files)
        => files.Select(file => file.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
