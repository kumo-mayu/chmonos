namespace BoothAssetManager.Core.Services;

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
