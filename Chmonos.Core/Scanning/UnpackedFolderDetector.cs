namespace Chmonos.Core.Scanning;

/// <summary>アーカイブの展開先とみなしたフォルダ。</summary>
public sealed class UnpackedFolder
{
    public required string Path { get; init; }

    /// <summary>展開元とみなしたアーカイブのパス。</summary>
    public required string ArchivePath { get; init; }

    public int FileCount { get; init; }

    /// <summary>フォルダ全体の容量。削除すればこのぶんが空く。</summary>
    public long TotalBytes { get; init; }
}

/// <summary>
/// アーカイブを展開したフォルダを見つける。
///
/// 実測では <c>Kipfel_1.2.0.zip</c> の隣に <c>Kipfel_1.2.0/</c>（png47・psd9）が置かれている、
/// という構造が7フォルダぶんあり、そのまま取り込むと未確定ファイルが183件になった。
/// これらは「既に持っているアセットの中身」であって別の商品ではないので、取り込み対象から外す。
/// zipの <c>contents</c> は別途記録しているため、情報は失われない。
/// </summary>
public static class UnpackedFolderDetector
{
    private static readonly string[] ArchiveExtensions = [".zip", ".rar", ".7z"];

    /// <summary>
    /// フォルダ名が、同じ場所にあるアーカイブの名前と一致するか。
    /// 一致すればそのアーカイブの展開先とみなす。
    /// </summary>
    public static string? FindMatchingArchive(string directoryName, IEnumerable<string> siblingFileNames)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return null;
        }

        foreach (var fileName in siblingFileNames)
        {
            var extension = System.IO.Path.GetExtension(fileName);
            if (!ArchiveExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
            if (string.Equals(stem, directoryName, StringComparison.OrdinalIgnoreCase))
            {
                return fileName;
            }
        }

        return null;
    }
}
