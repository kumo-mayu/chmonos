using System.Security.Cryptography;
using BoothZipInspector.Models;

namespace BoothZipInspector;

/// <summary>
/// 対象ファイルそのものの基本情報を取得する。
/// </summary>
public static class FileInspector
{
    public static FileBasicInfo Inspect(string path)
    {
        var info = new FileInfo(path);

        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        var hashHex = Convert.ToHexString(hash);

        return new FileBasicInfo
        {
            FullPath = info.FullName,
            FileName = info.Name,
            Extension = info.Extension,
            SizeBytes = info.Length,
            CreatedAtUtc = info.CreationTimeUtc,
            ModifiedAtUtc = info.LastWriteTimeUtc,
            Sha256Hex = hashHex,
        };
    }

    /// <summary>
    /// バイト数を読みやすい単位(KiB/MiB/GiB)の文字列に変換する。
    /// </summary>
    public static string ToHumanReadableSize(long bytes)
    {
        string[] units = { "bytes", "KiB", "MiB", "GiB", "TiB" };
        double size = bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{bytes:N0} bytes"
            : $"{size:0.##} {units[unitIndex]}";
    }
}
