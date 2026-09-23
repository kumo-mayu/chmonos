namespace BoothAssetManager.Core.Scanning;

/// <summary>1フォルダぶんの削除結果。削除しなかった場合は理由を持つ。</summary>
public sealed class UnpackedFolderRemoval
{
    public required string Path { get; init; }

    public required bool Removed { get; init; }

    /// <summary>削除しなかった理由。<see cref="Removed"/> が true なら null。</summary>
    public string? Reason { get; init; }

    /// <summary>実際に空いた容量。削除しなかった場合は0。</summary>
    public long FreedBytes { get; init; }
}

/// <summary>
/// アーカイブの展開先フォルダを削除する。
///
/// 中身を捨てても構わないのは「展開元のアーカイブが手元に残っている」場合だけなので、
/// 削除の直前に必ずそれを確かめ直す。検出時の情報をそのまま信じない。
/// 取り込みから削除までの間にアーカイブの方が消えている、という順序があり得るため。
///
/// 実際に消す処理は外から渡す。Coreはプラットフォームに依存せず、
/// 呼び出し側（WPF）がごみ箱送りを選べるようにするための分け方。
/// </summary>
public sealed class UnpackedFolderRemover
{
    private readonly Func<string, CancellationToken, Task> _deleteDirectory;

    /// <param name="deleteDirectory">フォルダを実際に消す処理。</param>
    public UnpackedFolderRemover(Func<string, CancellationToken, Task> deleteDirectory)
    {
        _deleteDirectory = deleteDirectory;
    }

    public async Task<IReadOnlyList<UnpackedFolderRemoval>> RemoveAsync(
        IEnumerable<UnpackedFolder> folders,
        CancellationToken cancellationToken = default)
    {
        var results = new List<UnpackedFolderRemoval>();

        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RemoveOneAsync(folder, cancellationToken));
        }

        return results;
    }

    private async Task<UnpackedFolderRemoval> RemoveOneAsync(UnpackedFolder folder, CancellationToken cancellationToken)
    {
        var refusal = FindRefusal(folder);
        if (refusal is not null)
        {
            return new UnpackedFolderRemoval { Path = folder.Path, Removed = false, Reason = refusal };
        }

        // 検出時の値ではなく、今の実サイズを報告する
        var bytes = MeasureDirectory(folder.Path);

        try
        {
            await _deleteDirectory(folder.Path, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Diagnostics.AppLog.Error("展開したフォルダを消す", exception);
            return new UnpackedFolderRemoval
            {
                Path = folder.Path,
                Removed = false,
                Reason = Services.FailureText.Cause(exception),
            };
        }

        return new UnpackedFolderRemoval { Path = folder.Path, Removed = true, FreedBytes = bytes };
    }

    /// <summary>削除してはいけない理由を探す。無ければ null。</summary>
    private static string? FindRefusal(UnpackedFolder folder)
    {
        if (!Directory.Exists(folder.Path))
        {
            return "フォルダが見つかりません。";
        }

        if (!File.Exists(folder.ArchivePath))
        {
            return "展開元のアーカイブが見つかりません。中身を復元できなくなるため削除しません。";
        }

        var folderParent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder.Path));
        var archiveParent = Path.GetDirectoryName(folder.ArchivePath);
        if (folderParent is null
            || archiveParent is null
            || !string.Equals(folderParent, archiveParent, StringComparison.OrdinalIgnoreCase))
        {
            return "展開元のアーカイブが同じ場所にありません。対応を確認できないため削除しません。";
        }

        // 検出時と同じ判定をやり直す。フォルダ名かアーカイブ名が変わっていれば対応が崩れている
        var directoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder.Path));
        var archiveName = Path.GetFileName(folder.ArchivePath);
        if (UnpackedFolderDetector.FindMatchingArchive(directoryName, [archiveName]) is null)
        {
            return "フォルダ名と展開元の名前が一致しません。削除しません。";
        }

        return null;
    }

    private static long MeasureDirectory(string path)
    {
        try
        {
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(file => file.Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
