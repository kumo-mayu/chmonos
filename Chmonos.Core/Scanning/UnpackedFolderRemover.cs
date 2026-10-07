namespace Chmonos.Core.Scanning;

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
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>?>> _registeredFolders;

    /// <param name="deleteDirectory">フォルダを実際に消す処理。</param>
    /// <param name="registeredFolders">
    /// 商品に登録したフォルダの場所を今の記録から読む処理（<see cref="RegisteredFoldersIn"/>）。
    /// 確かめきれない（読めない商品の記録がある）ときは null。
    /// </param>
    public UnpackedFolderRemover(
        Func<string, CancellationToken, Task> deleteDirectory,
        Func<CancellationToken, Task<IReadOnlyList<string>?>> registeredFolders)
    {
        _deleteDirectory = deleteDirectory;
        _registeredFolders = registeredFolders;
    }

    /// <summary>
    /// 保存先の全商品から、登録したフォルダの場所を読む（見つからない印の付いた物・非表示の商品の物も入れる）。
    /// 読めない商品の記録が1件でもあれば null：その商品がどのフォルダを登録しているか分からないので、消してよいと言えない。
    /// </summary>
    public static Func<CancellationToken, Task<IReadOnlyList<string>?>> RegisteredFoldersIn(Storage.DataStore store)
        => async cancellationToken =>
        {
            var loaded = await store.Items.LoadAllAsync(cancellationToken: cancellationToken);
            // 形の外れた ID で外した商品も、どのフォルダを登録しているか見ていないので、読めない商品と同じ扱い（外部の点検 2026-10-06）
            return loaded.FailedItemIds.Count > 0 || loaded.SkippedMalformed > 0
                ? null
                : loaded.Items.SelectMany(item => item.Local.LocalFolders).Select(folder => folder.Path).ToList();
        };

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
        // 登録は消す直前に読み直す（画面が展開先を見つけた後に「zipの代わりにフォルダを登録」したかもしれない）。
        // 全件を読むが、変わっていない商品は写しを返すので2回目からは軽い（ItemRepository.LoadAllAsync）
        var refusal = FindRefusal(folder)
            ?? RegistrationRefusal(folder.Path, await _registeredFolders(cancellationToken))
            ?? await Task.Run(() => UnpackedContentCheck.Refusal(folder.Path, folder.ArchivePath, cancellationToken), cancellationToken);
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
        catch (NotRecyclableException exception)
        {
            // ごみ箱へ送れないなら消さない。取り違えたときに戻せない消し方はしない（2026-10-07）
            return new UnpackedFolderRemoval { Path = folder.Path, Removed = false, Reason = exception.Message };
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

    /// <summary>
    /// 商品に登録したフォルダに重なるなら、消さない理由（2026-10-05・file-lifecycle.md「気になった所」4）。
    /// </summary>
    /// <remarks>
    /// 展開先かは名前で見ているだけなので、zip の隣のフォルダを商品として登録していても（zip が後から来た・
    /// 「zipで登録し直す」の前）展開先に見え、ごみ箱へ送っていた。送った間、その商品のフォルダは「見つからない」になり、
    /// 登録した人の判断（このフォルダがこの商品）も黙って崩れる。そのもの・中に登録がある・登録の中にある、のどれも止める。
    /// 比べるのは区切りまで（"作り物_1.0" が "作り物_1.0.1" を巻き込まないように。RegisteredFolderSet と同じ考え）。
    /// </remarks>
    internal static string? RegistrationRefusal(string folderPath, IReadOnlyList<string>? registered)
    {
        if (registered is null)
        {
            return "読めない商品の記録があり、商品に登録したフォルダか確かめられないので削除しません。通知の画面から直せます。";
        }

        var target = Normalized(folderPath);
        foreach (var path in registered)
        {
            var other = Normalized(path);
            if (string.Equals(other, target, StringComparison.OrdinalIgnoreCase))
            {
                return "商品に登録したフォルダです。削除しません。";
            }

            if (other.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return "中に商品に登録したフォルダがあります。削除しません。";
            }

            if (target.StartsWith(other + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return "商品に登録したフォルダの中にあります。削除しません。";
            }
        }

        return null;
    }

    private static string Normalized(string path)
        => Path.TrimEndingDirectorySeparator(path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));

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

/// <summary>
/// ごみ箱へ送れないので消さなかった（ネットワーク・取り外せるドライブ、ごみ箱に入りきらず完全に消すかを聞かれてやめた）。
/// 消す処理（App）が投げ、<see cref="UnpackedFolderRemover"/> は文をそのまま理由にする。
/// </summary>
public sealed class NotRecyclableException(string reason) : IOException(reason);
