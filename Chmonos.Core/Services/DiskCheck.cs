namespace Chmonos.Core.Services;

/// <summary>ディスクを見た答え（<see cref="DiskCheck.FileState"/>）。</summary>
public enum DiskAnswer
{
    Present,

    /// <summary>無いと分かった（ファイルか、途中のフォルダが無い）。</summary>
    Missing,

    /// <summary>確かめられない（アクセスを拒まれた・ドライブやネットワークが答えなかった）。在るとも無いとも言えない。</summary>
    Unknown,
}

/// <summary>
/// 手元のファイル・フォルダが在るか（技術的負債 4-2、2026-09-14）。
///
/// **画面からは Async の方を使う。**利用者のファイルは外付けやネットワークにもあり、落ちているネットワークドライブは
/// 1回確かめるだけで数秒かかることがある。画面のスレッドで見ると、その間画面が止まる。
/// **投げない。**名前として読めないパス・権限の無い場所は「無い」と答える（在るかを聞く所で落ちると、画面ごと止まる）。
/// アプリ自身の保存先の中（サムネイル・改変の写真）は対象にしない——保存先に届かなければアプリがそもそも動かない。
/// </summary>
public static class DiskCheck
{
    public static bool FileExists(string? path) => Safely(path, File.Exists);

    public static bool FolderExists(string? path) => Safely(path, Directory.Exists);

    /// <summary>
    /// ファイルが在るか・無いか・確かめられないか（記録に「無い」と書く所が使う。2026-10-05・点検の13）。
    /// </summary>
    /// <remarks>
    /// <see cref="FileExists"/> は権限の無いフォルダの上のファイルにも false（無い）と答える。それで見回りが日時を付け、
    /// 取り込みが場所を外していた。**無いと分かったとき（ファイル・途中のフォルダが無い）だけ「無い」**と答え、
    /// アクセスを拒まれた・ドライブやネットワークが答えなかった（そのほかの入出力の失敗）は「確かめられない」にする。
    /// 名前として読めないパスは、そこに何も在り得ないので「無い」（<see cref="FileExists"/> と同じ答え）。
    /// </remarks>
    public static DiskAnswer FileState(string? path) => StateOf(path, isFolder: false, File.GetAttributes);

    /// <summary>フォルダについて <see cref="FileState"/> と同じ見分け。</summary>
    public static DiskAnswer FolderState(string? path) => StateOf(path, isFolder: true, File.GetAttributes);

    /// <param name="attributes">属性を読む（試験が、拒まれた・無いを作るために差し替える）。</param>
    internal static DiskAnswer StateOf(string? path, bool isFolder, Func<string, FileAttributes> attributes)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return DiskAnswer.Missing;
        }

        try
        {
            var isDirectory = attributes(path).HasFlag(FileAttributes.Directory);
            return isDirectory == isFolder ? DiskAnswer.Present : DiskAnswer.Missing;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException
                                              or ArgumentException or NotSupportedException)
        {
            return DiskAnswer.Missing;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return DiskAnswer.Unknown;
        }
    }

    public static Task<bool> FileExistsAsync(string? path) => Task.Run(() => FileExists(path));

    public static Task<bool> FolderExistsAsync(string? path) => Task.Run(() => FolderExists(path));

    private static bool Safely(string? path, Func<string, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
                                              or NotSupportedException)
        {
            return false;
        }
    }
}
