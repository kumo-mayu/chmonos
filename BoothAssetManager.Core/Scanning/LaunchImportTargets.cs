namespace BoothAssetManager.Core.Scanning;

/// <summary>
/// 設定「起動時に自動で取り込む」が入のとき、起動時に取り込みの対象へ積む物を決める。
///
/// **監視フォルダの新着と、前回途中で止まった取り込みの続き**（ユーザ判断 2026-09-29：
/// 「前回途中までで残っていたものも対象にしたい」）。続きは下の帯の「続きから進む」が積む物と同じ
/// （<see cref="ImportState.ResumeTargets"/>）。前は新着だけを見ていたので、①の走査を済ませてから閉じた回は
/// 走査の控えに載っていて新着に数えられず、続きが残っていても起動時には何も始まらなかった。
///
/// 起動時に勝手に走査してよい範囲は「監視フォルダ」と「前回ユーザが取り込みを頼んだ対象」だけで、
/// 取り込み元の履歴を全部積むことはしない（ユーザ判断 2026-09-21・G1）。
/// </summary>
public static class LaunchImportTargets
{
    /// <param name="newFiles">監視フォルダの新着（監視フォルダが無ければ空）。</param>
    /// <param name="state">取り込みの続きの記録。読めなければ null。</param>
    public static IReadOnlyList<string> Collect(IReadOnlyList<string> newFiles, ImportState? state)
    {
        // 続きの記録は、伝えることが残っているときだけ使う（最後まで走り、取れなかった商品も無い回は積まない）
        var resume = state is { HasProgress: true } ? state.ResumeTargets : [];

        return newFiles.Concat(resume)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
