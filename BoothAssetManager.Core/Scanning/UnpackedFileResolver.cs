namespace BoothAssetManager.Core.Scanning;

/// <summary>展開先の中のファイルと、その展開元アーカイブの対応。</summary>
public sealed class UnpackedFileOrigin
{
    /// <summary>指定されたファイル。</summary>
    public required string FilePath { get; init; }

    /// <summary>展開元とみなしたアーカイブ。</summary>
    public required string ArchivePath { get; init; }
}

/// <summary>
/// 名指しで指定されたファイルが、アーカイブの展開先の中にあるかを調べる。
///
/// 展開先が残っている状態では、そのファイルが「配布物そのもの」なのか
/// 「zipの中身を展開したもの」なのかを、ファイル単体からは区別できない。
/// zipが手元にあるならそちらを取り込む方が、配布単位と一致していて後の照合も効く。
/// ただし常にzipへ寄せると、意図してその1ファイルを指した場合に食い違うので、
/// どちらを使うかは呼び出し側（＝ユーザ）に決めさせる。
/// </summary>
public static class UnpackedFileResolver
{
    /// <summary>
    /// 指定されたパスのうち、展開先の中にあるファイルを拾う。
    /// 親をたどって、フォルダ名と一致するアーカイブが同じ場所にあるものを探す。
    /// 見つからなければ結果に含めない（＝そのファイルをそのまま使う）。
    /// </summary>
    public static IReadOnlyList<UnpackedFileOrigin> FindOrigins(IEnumerable<string> paths)
    {
        var found = new List<UnpackedFileOrigin>();

        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var archive = FindArchiveFor(path);
            if (archive is not null)
            {
                found.Add(new UnpackedFileOrigin { FilePath = path, ArchivePath = archive });
            }
        }

        return found;
    }

    /// <summary>このファイルの展開元アーカイブ。無ければ null。</summary>
    public static string? FindArchiveFor(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);

        // 深い階層に置かれていることもあるので、親をたどって探す
        while (!string.IsNullOrEmpty(directory))
        {
            var parent = Path.GetDirectoryName(directory);
            if (string.IsNullOrEmpty(parent))
            {
                return null;
            }

            var directoryName = Path.GetFileName(directory);
            var siblings = SafeEnumerateFileNames(parent);
            var match = UnpackedFolderDetector.FindMatchingArchive(directoryName, siblings);

            if (match is not null)
            {
                return Path.Combine(parent, match);
            }

            directory = parent;
        }

        return null;
    }

    private static IReadOnlyList<string> SafeEnumerateFileNames(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory).Select(Path.GetFileName).OfType<string>().ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
