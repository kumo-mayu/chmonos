namespace BoothAssetManager.Core.Scanning;

/// <summary>フォルダ走査で見つかった1ファイル。</summary>
public sealed class ScannedFile
{
    public required string Path { get; init; }

    public required long SizeBytes { get; init; }

    public required DateTimeOffset ModifiedAtUtc { get; init; }

    /// <summary>小文字の拡張子（ドット付き）。</summary>
    public required string Extension { get; init; }

    /// <summary>BoothID解決の対象になるアーカイブか。zip以外はユーザに問い合わせる扱いになる。</summary>
    public bool IsArchive => Extension is ".zip";
}

/// <summary>
/// 取り込み対象フォルダの走査。ここではファイルを列挙するだけで、ハッシュもZIPの中身も見ない。
/// 重い処理を後段に分けているのは、3フェーズの進捗を正しく出すため。
/// </summary>
public sealed class FolderScanner
{
    /// <summary>
    /// 取り込み対象の拡張子。zip以外もリストに含めるのは、ユーザに紐付けを問い合わせるため。
    /// </summary>
    public static readonly IReadOnlySet<string> TargetExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar",
        ".psd", ".ai", ".lip", ".pdf",
        ".mp3", ".m4a", ".wav", ".aif", ".aiff", ".flac",
        ".epub",
        ".vroid", ".vroidcustomitem", ".vrm", ".vrma",
        ".xwear", ".xavatar", ".xroid",
        ".jpg", ".jpeg", ".gif", ".png",
        ".mp4", ".mov", ".avi",
    };

    private static readonly EnumerationOptions RecursiveOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
    };

    /// <summary>
    /// 指定フォルダ以下を再帰的に走査する。アクセスできないフォルダは飛ばして続行する
    /// （1つの権限エラーで全体を止めないため）。
    /// アーカイブの展開先とみなしたフォルダの中身は、取り込み対象から外して別に返す。
    /// </summary>
    public ScanResult Scan(string rootFolder, CancellationToken cancellationToken = default)
    {
        // ファイルが直接指定されたら、そのファイルだけを対象にする。
        // 親フォルダへ広げると、ダウンロードフォルダの1件を落としただけで
        // フォルダ全体が取り込み対象になってしまう。
        // 一時展開（#56）の中は取り込まない。閉じると消えるので、紐付けても「見つからない」になるだけ
        if (Services.TemporaryUnpacker.IsInsideDefaultRoot(rootFolder))
        {
            return new ScanResult();
        }

        if (File.Exists(rootFolder))
        {
            var missed = 0;
            var single = Describe(rootFolder, ref missed);
            return new ScanResult { Files = single is null ? [] : [single], Unreadable = missed };
        }

        if (!Directory.Exists(rootFolder))
        {
            return new ScanResult();
        }

        var unpacked = FindUnpackedFolders(rootFolder, cancellationToken);
        var files = new List<ScannedFile>();
        var skipped = 0;
        var unreadable = 0;

        foreach (var path in Directory.EnumerateFiles(rootFolder, "*", RecursiveOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension) || !TargetExtensions.Contains(extension))
            {
                continue;
            }

            if (IsInsideUnpackedFolder(path, unpacked))
            {
                skipped++;
                continue;
            }

            var scanned = Describe(path, ref unreadable);
            if (scanned is not null)
            {
                files.Add(scanned);
            }
        }

        return new ScanResult
        {
            Files = files,
            UnpackedFolders = unpacked,
            SkippedInsideUnpackedFolders = skipped,
            Unreadable = unreadable,
        };
    }

    /// <summary>1ファイルを見て取り込み対象なら情報を返す。対象外・読めない場合は null（読めなかったものは数える）。</summary>
    private static ScannedFile? Describe(string path, ref int unreadable)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension) || !TargetExtensions.Contains(extension))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(path);
            return new ScannedFile
            {
                Path = path,
                SizeBytes = info.Length,
                ModifiedAtUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                Extension = extension.ToLowerInvariant(),
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 走査中に消えた・触れないファイルは飛ばすが、**数は残す**（E4：無言で消えていた）
            unreadable++;
            return null;
        }
    }

    private static List<UnpackedFolder> FindUnpackedFolders(string rootFolder, CancellationToken cancellationToken)
    {
        var found = new List<UnpackedFolder>();

        foreach (var directory in Directory.EnumerateDirectories(rootFolder, "*", RecursiveOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parent = Path.GetDirectoryName(directory);
            if (parent is null)
            {
                continue;
            }

            string[] siblings;
            try
            {
                siblings = Directory.GetFiles(parent);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var archive = UnpackedFolderDetector.FindMatchingArchive(
                Path.GetFileName(directory),
                siblings.Select(Path.GetFileName).Where(name => name is not null).Select(name => name!));

            if (archive is null)
            {
                continue;
            }

            var (count, bytes) = MeasureFolder(directory);
            found.Add(new UnpackedFolder
            {
                Path = directory,
                ArchivePath = Path.Combine(parent, archive),
                FileCount = count,
                TotalBytes = bytes,
            });
        }

        return found;
    }

    private static (int Count, long Bytes) MeasureFolder(string directory)
    {
        var count = 0;
        long bytes = 0;

        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*", RecursiveOptions))
            {
                try
                {
                    bytes += new FileInfo(path).Length;
                    count++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // 個別のファイルが読めなくても集計は続ける
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // フォルダごと読めない場合はそこまでの集計で返す
        }

        return (count, bytes);
    }

    private static bool IsInsideUnpackedFolder(string path, List<UnpackedFolder> unpacked)
    {
        foreach (var folder in unpacked)
        {
            if (path.StartsWith(folder.Path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class ScanResult
{
    public IReadOnlyList<ScannedFile> Files { get; init; } = [];

    /// <summary>アーカイブの展開先とみなしたフォルダ。削除機能の対象候補にもなる。</summary>
    public IReadOnlyList<UnpackedFolder> UnpackedFolders { get; init; } = [];

    public int SkippedInsideUnpackedFolders { get; init; }

    /// <summary>
    /// 権限などで**読めなかった**ファイルの数（E4・ユーザ判断 2026-09-20）。
    /// 黙って飛ばすと、取り込んだつもりの物が入っていないのに気付けない。
    /// 1件ずつ言うと何千件も出るので、数だけを取り込みの結果に足す
    /// （`SkippedInsideUnpackedFolders` と同じ道）。
    /// </summary>
    public int Unreadable { get; init; }
}
