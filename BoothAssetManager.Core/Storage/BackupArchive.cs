using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace BoothAssetManager.Core.Storage;

/// <summary>書き出した結果。何を入れて何を入れなかったかを人に見せるために持つ。</summary>
public sealed record BackupResult(int Files, long Bytes, int SkippedLocked);

/// <summary>バックアップの中に入れる説明（<c>backup-info.json</c>）。zip を開いた人が読める形。</summary>
public sealed record BackupInfo
{
    public required DateTimeOffset CreatedAt { get; init; }

    public required bool IncludesImages { get; init; }

    public required int Files { get; init; }

    public string Note { get; init; } =
        "Chmonos のバックアップです。設定画面の「バックアップから戻す」で、空のフォルダに展開してそこへ移れます。";
}

/// <summary>
/// 保存先を1つの zip に書き出す・zip から戻す（#61）。
///
/// **戻すときは今の保存先に重ねない。**別の空の場所に展開し、そこへ移る（ユーザ判断）。
/// 重ねると、今のデータとバックアップのデータが混ざり、どちらが正しいか分からなくなる。
/// 今のデータは元の場所にそのまま残るので、戻したのが間違いでも取り返せる。
///
/// 中身はどれも人が読めるファイルなので、zip を普通に開いて1ファイルだけ取り出すこともできる。
/// </summary>
public static class BackupArchive
{
    public const string InfoFileName = "backup-info.json";

    /// <summary>
    /// 入れない物。**計算し直せる物と、戻すと害になる物。**
    /// 途中で残った .tmp は壊れかけの書きかけ、search-bridge.cache は辞書から組み直せる索引（18MB ある）、
    /// location.json は「保存先はどこか」を覚えるファイルで、戻すと行き先が狂う。
    /// </summary>
    private static bool IsLeftOut(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        return name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".cache", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "location.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, InfoFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImage(string relativePath)
        => relativePath.StartsWith("images" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 書き出す。書き出し先の zip が保存先の中にあっても、自分自身は入れない。
    /// 他のプログラムが掴んでいて開けないファイル（ロックなど）は飛ばして数える。
    /// </summary>
    public static BackupResult Export(string root, string zipPath, bool includeImages, CancellationToken cancellationToken = default)
    {
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var zipFull = Path.GetFullPath(zipPath);

        // 書きかけの zip を本物の名前で残さない。書き終えてから置き換える
        var temporary = zipFull + ".tmp";
        var files = 0;
        var bytes = 0L;
        var skipped = 0;

        using (var stream = File.Create(temporary))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8))
        {
            foreach (var path in Directory.EnumerateFiles(rootFull, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.Equals(Path.GetFullPath(path), zipFull, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFullPath(path), temporary, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(rootFull, path);
                if (IsLeftOut(relative) || (!includeImages && IsImage(relative)))
                {
                    continue;
                }

                try
                {
                    using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var entry = archive.CreateEntry(relative.Replace(Path.DirectorySeparatorChar, '/'), CompressionLevel.Optimal);
                    entry.LastWriteTime = File.GetLastWriteTime(path);
                    using var target = entry.Open();
                    source.CopyTo(target);
                    files++;
                    bytes += source.Length;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    skipped++;
                }
            }

            var info = new BackupInfo { CreatedAt = DateTimeOffset.Now, IncludesImages = includeImages, Files = files };
            using var infoStream = archive.CreateEntry(InfoFileName).Open();
            JsonSerializer.Serialize(infoStream, info, JsonStore.Options);
        }

        File.Move(temporary, zipFull, overwrite: true);
        return new BackupResult(files, bytes, skipped);
    }

    /// <summary>このアプリのバックアップに見えるか。商品か設定のどちらかが入っていれば、そう見る。</summary>
    public static bool LooksLikeBackup(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            return archive.Entries.Any(entry =>
                string.Equals(entry.FullName, "settings.json", StringComparison.OrdinalIgnoreCase)
                || entry.FullName.StartsWith("items/", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 空の場所へ展開する。展開先が空でなければ何もせずに投げる——混ざるのを防ぐため。
    /// zip の外へ書き出そうとする名前は飛ばす。
    /// </summary>
    /// <returns>展開したファイルの数。</returns>
    public static int Restore(string zipPath, string destinationRoot, CancellationToken cancellationToken = default)
    {
        if (!LooksLikeBackup(zipPath))
        {
            throw new InvalidDataException("Chmonos のバックアップではありません（商品も設定も入っていません）。");
        }

        if (!StoreLocation.IsEmpty(destinationRoot))
        {
            throw new IOException($"展開先が空ではありません：{destinationRoot}");
        }

        Directory.CreateDirectory(destinationRoot);
        var destinationFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationRoot)) + Path.DirectorySeparatorChar;
        var files = 0;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.Equals(entry.FullName, InfoFileName, StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith('/'))
            {
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(destinationFull, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(destinationFull, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
            files++;
        }

        return files;
    }
}
