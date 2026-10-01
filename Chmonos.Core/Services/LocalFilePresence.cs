using Chmonos.Core.Scanning;

namespace Chmonos.Core.Services;

/// <summary>商品の手元のファイル1件が、今ディスクの上に見えるか。</summary>
public enum FilePresence
{
    /// <summary>記録の場所の1つ以上に在る。</summary>
    Present,

    /// <summary>どの場所にも無い（移した・消した）。</summary>
    Missing,

    /// <summary>どの場所にも見えないが、つながっていないドライブの上の場所がある。無くなったとは限らない。</summary>
    OnDetachedDrive,
}

/// <summary>
/// 商品ページで「見つかりません」を出すかを決める（点検 2026-09-30 の B）。
/// 前は記録のパスが空のときだけ出していた。取り込みはつながっていないドライブの上のパスを残す
/// （<see cref="LocalFileMerger"/>）ので、移した・消したファイルも、外付けを外したファイルも、
/// 普通の行と［開く ▾］で出て、移した人は気付く手掛かりが無かった。
///
/// **つながっていないドライブの上は「見つかりません」と分ける。**取り込みがそこのパスを残すのと同じ考えで、
/// 無くなったのではなく今は見えないだけ。取り込み直しを勧めるのは当たらず、次の一手は「つなぐ」になる
/// （フォルダの表示の「取り外しているドライブ」と同じ見分け）。
///
/// ディスクを見るので、画面からは画面のスレッドの外で呼ぶ（<see cref="DiskCheck"/>）。
/// </summary>
public static class LocalFilePresence
{
    public static FilePresence Of(
        IReadOnlyList<string> paths,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? onMissingVolume = null)
    {
        var exists = fileExists ?? DiskCheck.FileExists;
        var unreachable = onMissingVolume ?? UnresolvedMerge.IsOnMissingVolume;

        if (paths.Any(exists))
        {
            return FilePresence.Present;
        }

        return paths.Any(unreachable) ? FilePresence.OnDetachedDrive : FilePresence.Missing;
    }
}
