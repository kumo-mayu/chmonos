namespace Chmonos.Core.Scanning;

/// <summary>ファイルの今の様子。2回見て同じで、<see cref="Ready"/> なら書き終わったと見る。</summary>
/// <param name="Ready">書いている途中の印が無い（ダウンロードの途中のファイルが隣に無く、ほかのアプリが書き込みのために掴んでいない）。</param>
public readonly record struct FileSettle(long SizeBytes, DateTime ModifiedAtUtc, bool Ready);

/// <summary>
/// 監視フォルダに置かれたファイルが書き終わったかを見る（plan 49）。試験は作り物に差し替える。
/// </summary>
public interface IFileSettleProbe
{
    /// <summary>今の様子。無くなっていれば null。</summary>
    FileSettle? Probe(string path);
}

/// <summary>
/// 手元のディスクを見る <see cref="IFileSettleProbe"/>。
///
/// 書いている途中と見るのは：
/// <list type="bullet">
/// <item>隣にダウンロードの途中のファイルがある（Firefox は出来上がる名前の空のファイルを先に作り、<c>.part</c> に書いてから差し替える）</item>
/// <item>書き込みの共有を許さずに開けない（ダウンロードやコピーが書き込みのために掴んでいる）</item>
/// </list>
/// 大きさ・更新日時が動いているかは、呼び手が2回見て比べる。
/// </summary>
public sealed class FileSettleProbe : IFileSettleProbe
{
    /// <summary>
    /// ブラウザがダウンロードの途中に使う名前の後ろ。Chrome・Edge は <c>.crdownload</c>、Firefox は <c>.part</c>、
    /// 古い Edge は <c>.partial</c>、Opera は <c>.opdownload</c>。出来上がると後ろを外した名前に改名する
    /// </summary>
    public static readonly IReadOnlyList<string> PartialSuffixes = [".crdownload", ".part", ".partial", ".opdownload", ".download"];

    /// <summary>ダウンロードの途中の名前か（出来事を探し直しのきっかけに数えない。出来上がると改名の出来事が来る）。</summary>
    public static bool IsPartialName(string path)
        => PartialSuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
           || path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);

    public FileSettle? Probe(string path)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        var ready = !HasPartialSibling(path) && CanOpenWithoutWriters(info);
        return new FileSettle(info.Length, info.LastWriteTimeUtc, ready);
    }

    private static bool HasPartialSibling(string path)
        => PartialSuffixes.Any(suffix => File.Exists(path + suffix));

    private static bool CanOpenWithoutWriters(FileInfo info)
    {
        // オンラインのみの物は開かない（開くとダウンロードが始まる。走査もこれを数えない・FolderScanner.IsOnlineOnly）
        if (FolderScanner.IsOnlineOnly(info.Attributes))
        {
            return true;
        }

        try
        {
            // 書き込みの共有を許さずに開く。書いている人がいれば共有の違反で開けない
            using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // 権限が無いのは書いている途中ではない。取り込みが「読めなかったファイル」として数える
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
