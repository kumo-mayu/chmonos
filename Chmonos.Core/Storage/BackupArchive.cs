using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Chmonos.Core.Storage;

/// <summary>
/// 書き出す・戻すの進み具合（ユーザ判断 2026-09-20・E8）。
/// **先に数えてから詰める。**数えずに回していたので「書き出しています…」としか言えなかった。
/// </summary>
public readonly record struct BackupProgress(int Done, int Total, string CurrentName);

/// <summary>書き出した結果。何を入れて何を入れなかったかを人に見せるために持つ。</summary>
public sealed record BackupResult(int Files, long Bytes, int SkippedLocked);

/// <summary>
/// 書き出しの途中で、写し始めたファイルが読めなくなった。zip は作りかけなので完成扱いにしない（一時の zip は消す）。
/// 呼び手が「何が起き、前の zip はどうなったか」を言えるように、名前と前の zip の有無を持つ。
/// </summary>
public sealed class BackupReadException(string relativePath, bool previousKept, Exception inner)
    : IOException($"書き出しの途中で読めなくなりました：{relativePath}", inner)
{
    /// <summary>読めなくなったファイル（保存先からの相対）。</summary>
    public string RelativePath { get; } = relativePath;

    /// <summary>同じ名前の前の zip が在り、手を付けずに残したか。</summary>
    public bool PreviousKept { get; } = previousKept;
}

/// <summary>保存先の中に、ほかの場所を指すリンクがあるので始めない。<see cref="Exception.Message"/> は画面に出せる文。</summary>
public sealed class StoreLinkException(string relativePath) : IOException(StoreTree.LinkRefusal(relativePath))
{
    /// <summary>リンクの場所（保存先からの相対）。</summary>
    public string RelativePath { get; } = relativePath;
}

/// <summary>バックアップの中に入れる説明（<c>backup-info.json</c>）。zip を開いた人が読める形。</summary>
public sealed record BackupInfo
{
    public required DateTimeOffset CreatedAt { get; init; }

    public required bool IncludesImages { get; init; }

    public required int Files { get; init; }

    public string Note { get; init; } =
        "Chmonosのバックアップです。設定画面の「バックアップから戻す」で、空のフォルダに展開してそこへ移れます。";
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

        // 商品の記録の控え（items/.prev）は本体とほぼ同じ中身で、入れると商品の分が倍になる。
        // 戻した後はアプリが書くたびに作り直す。よけた壊れた記録（items/_broken）は、そこにしか無い入力があるので入れる
        if (relativePath.StartsWith(Path.Combine("items", ".prev") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".cache", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "location.json", StringComparison.OrdinalIgnoreCase)
            // 書き出しの途中の記録は、その回の書き出しのための物。戻すと、戻した先で知らない場所の .tmp を探しに行く
            || string.Equals(name, BackupWritingRecord.FileName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, InfoFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImage(string relativePath)
        => relativePath.StartsWith("images" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 書き出す。書き出し先の zip が保存先の中にあっても、自分自身は入れない。
    /// 他のプログラムが掴んでいて開けないファイル（ロックなど）は飛ばして数える。
    /// </summary>
    public static BackupResult Export(
        string root,
        string zipPath,
        bool includeImages,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var zipFull = Path.GetFullPath(zipPath);

        // 書きかけの zip を本物の名前で残さない。書き終えてから置き換える
        var temporary = zipFull + ".tmp";

        // 失敗したときに「前の zip はそのまま」と言えるかを、書き始める前に見ておく
        var previousKept = File.Exists(zipFull);
        var files = 0;
        var bytes = 0L;
        var skipped = 0;

        // 保存先の中にリンクがあれば始めない（ユーザ判断「A」2026-10-06）。辿ると保存先の外のファイルが zip に入り、
        // 辿らずに書くと、戻したときにリンクの先の中身が無い。どちらにするかは使う人に決めてもらう
        if (StoreTree.FindLink(rootFull) is { } link)
        {
            throw new StoreLinkException(link);
        }

        // 先に数える（E8）。件数が分からないと進み具合を出せない。列挙をもう一度回すだけで、中身は読まない
        var targets = StoreTree.Files(rootFull).ToList();

        // 途中でアプリが止まったときに、次の起動で書きかけを片付けられるよう、場所を書いておく（BackupWritingRecord）
        BackupWritingRecord.Begin(rootFull, temporary);
        try
        {
            WriteArchive();
        }
        catch
        {
            // 中止・失敗のときは書きかけを残さない（公開前の点検 2026-10-01）。
            // 残すと、書き出し先のフォルダに開けない .tmp が残り、何が書けたのかが分からなくなる
            TryDelete(temporary);
            BackupWritingRecord.End(rootFull);
            throw;
        }

        File.Move(temporary, zipFull, overwrite: true);
        BackupWritingRecord.End(rootFull);
        return new BackupResult(files, bytes, skipped);

        void WriteArchive()
        {
            using var stream = File.Create(temporary);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);
            var seen = 0;
            foreach (var path in targets)
            {
                seen++;
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

                // 開く前の失敗（ほかのアプリが掴んでいる・消えた）は、そのファイルを飛ばして数える。zip にはまだ何も書いていない
                FileStream source;
                DateTime lastWrite;
                try
                {
                    source = OpenSource(path);
                    lastWrite = File.GetLastWriteTime(path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    skipped++;
                    continue;
                }

                // 写し始めた後の失敗は飛ばさない（外部の点検 2026-10-06）。項目は作りかけのまま zip に残り、
                // 前は「飛ばした」と数えて完成扱いにし、前の正常な zip を上書きしていた。書き出しごと失敗にし、前の zip を残す
                using (source)
                {
                    var entry = archive.CreateEntry(relative.Replace(Path.DirectorySeparatorChar, '/'), CompressionLevel.Optimal);
                    entry.LastWriteTime = lastWrite;
                    try
                    {
                        using var target = entry.Open();
                        source.CopyTo(target);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        throw new BackupReadException(relative, previousKept, exception);
                    }

                    files++;
                    bytes += source.Length;
                }

                progress?.Report(new BackupProgress(seen, targets.Count, Path.GetFileName(path)));
            }

            var info = new BackupInfo { CreatedAt = DateTimeOffset.Now, IncludesImages = includeImages, Files = files };
            using var infoStream = archive.CreateEntry(InfoFileName).Open();
            JsonSerializer.Serialize(infoStream, info, JsonStore.Options);
        }
    }

    private static FileStream OpenSource(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 消せなくても、元の失敗の方を伝える（名前が .tmp なので、戻すときにバックアップと取り違えない）
        }
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
    /// <param name="commit">
    /// 展開し終えてから呼ぶ（呼び手はここで <c>location.json</c> を書き換える）。投げたら、止めた・失敗したときと同じく展開した物を消して投げ直す。
    /// 展開の外で書き換えて失敗すると、書き込みの門を閉じたまま（開き直す前提）なのに開き直す先が無く、
    /// 展開先にも中身が残って同じ場所へ戻し直せなかった（2026-10-06）
    /// </param>
    /// <returns>展開したファイルの数。</returns>
    public static int Restore(
        string zipPath,
        string destinationRoot,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default,
        Action? commit = null)
    {
        if (!LooksLikeBackup(zipPath))
        {
            throw new InvalidDataException("Chmonos のバックアップではありません（商品も設定も入っていません）。");
        }

        if (!StoreLocation.IsEmpty(destinationRoot))
        {
            throw new IOException($"展開先が空ではありません：{destinationRoot}");
        }

        // 片付けのために、**ここで作った**ファイルとフォルダを控える（外部の点検 2026-10-06）。
        // 前は展開先を丸ごと再帰で消していたので、戻している間に人がそこへ置いた物（別のアプリが書いた物）まで、ごみ箱を通さずに消していた
        var createdFiles = new List<string>();
        var createdFolders = new List<string>();
        StoreMover.CreateFolder(destinationRoot, createdFolders);

        try
        {
            var extracted = Extract();
            commit?.Invoke();
            return extracted;
        }
        catch
        {
            // 失敗・中止のときは展開した物を消す（ユーザ判断 2026-10-01）。
            // 残すと展開先が空でなくなり、同じ場所へ戻し直すと「空ではありません」で断られ、手で片付けるまで使えない
            ClearExtracted(createdFiles, createdFolders);
            throw;
        }

        int Extract()
        {
            var destinationFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationRoot)) + Path.DirectorySeparatorChar;
            var files = 0;

            using var archive = ZipFile.OpenRead(zipPath);
            var total = archive.Entries.Count;
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

                StoreMover.CreateFolder(Path.GetDirectoryName(target)!, createdFolders);

                // 新しく作れたときだけ控える（同じ名前の物が先に在れば CreateNew が投げ、その物には触れない）。
                // 書く途中で落ちた書きかけは控えに入っているので、片付けで消える
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    createdFiles.Add(target);
                    using var input = entry.Open();
                    input.CopyTo(output);
                }

                File.SetLastWriteTime(target, entry.LastWriteTime.DateTime);
                files++;
                progress?.Report(new BackupProgress(files, total, Path.GetFileName(target)));
            }

            return files;
        }
    }

    /// <summary>
    /// 戻すが作ったファイルを消し、作ったフォルダを空になった物だけ深い方から畳む。
    /// 後から人が置いた物はどれにも入っていないので残り、それが入ったフォルダも空でないので残る。
    /// </summary>
    private static void ClearExtracted(IReadOnlyList<string> createdFiles, IReadOnlyList<string> createdFolders)
    {
        foreach (var file in createdFiles)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 消しきれなければ残る。元の失敗の方を伝える（こちらはログにだけ残す）
                Diagnostics.AppLog.Error("戻すの途中の物を消す", exception);
            }
        }

        foreach (var folder in createdFolders.OrderByDescending(path => path.Length))
        {
            try
            {
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Diagnostics.AppLog.Error("戻すの途中のフォルダを畳む", exception);
            }
        }
    }
}
