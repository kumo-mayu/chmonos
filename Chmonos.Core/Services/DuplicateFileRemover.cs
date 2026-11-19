using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>1か所ぶんの削除結果。削除しなかった場合は理由を持つ。</summary>
/// <param name="ChangedItemIds">送った後の見回りが記録を書いた商品（画面が検索の写しへ知らせる）。</param>
public sealed record DuplicateRemoval(string Path, bool Removed, string? Reason, long FreedBytes, IReadOnlyList<string>? ChangedItemIds = null);

/// <summary>
/// 統計の「空けられる場所」で、同じ中身のファイルの1か所をごみ箱へ送る（ユーザ判断 2026-11-20）。
///
/// **統計の集計は古い写しなので、消す直前に今の記録とディスクで確かめ直す。**
/// 同じ中身のほかのファイルが、記録に在って・今ディスクに在って・中身も同じ（ハッシュを取り直す）と分かるまでは送らない。
/// 確かめずに送ると、最後の1つをごみ箱へ送ってしまう（集計の後に、ほかの場所を手で消していた場合など）。
/// 記録の場所は外さない：消した後の見回り（<see cref="MissingMarksSweep"/>）が、同じ中身が別の場所に在るのを確かめて外す
/// （1つの場所を複数の商品が持つことがあり、ここで1商品ぶんだけ外すと残りの商品とずれる）。
///
/// 実際に消す処理は外から渡す（<see cref="UnpackedFolderRemover"/> と同じ。ごみ箱送りは App の仕事）。
/// </summary>
public sealed class DuplicateFileRemover
{
    private readonly DataStore _store;
    private readonly Func<string, CancellationToken, Task> _deleteFile;
    private readonly Func<string, CancellationToken, Task<string>> _hash;
    private readonly MissingMarksSweep? _sweep;

    /// <param name="hash">ファイルのハッシュを取る処理（試験で差し替える。既定は <see cref="FileHasher"/>）。</param>
    /// <param name="sweep">送った後に記録の場所を見回る部品。無ければ見回らない（試験）。</param>
    public DuplicateFileRemover(
        DataStore store,
        Func<string, CancellationToken, Task> deleteFile,
        Func<string, CancellationToken, Task<string>>? hash = null,
        MissingMarksSweep? sweep = null)
    {
        _store = store;
        _deleteFile = deleteFile;
        _hash = hash ?? FileHasher.ComputeSha256Async;
        _sweep = sweep;
    }

    public async Task<DuplicateRemoval> RemoveAsync(string hash, string path, CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        if (loaded.FailedItemIds.Count > 0 || loaded.SkippedMalformed > 0)
        {
            return Refused(path, "読めない商品の記録があり、同じ中身のほかのファイルが在るか確かめられないので削除しません。通知の画面から直せます。");
        }

        var records = loaded.Items
            .SelectMany(item => item.Local.OwnedFiles)
            .Where(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var recorded = records
            .SelectMany(file => file.Paths)
            .DistinctBy(Normalized, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!recorded.Any(place => string.Equals(Normalized(place), Normalized(path), StringComparison.OrdinalIgnoreCase)))
        {
            return Refused(path, "記録に無い場所なので削除しません。統計を読み直してください。");
        }

        if (!File.Exists(path))
        {
            return Refused(path, "ファイルが見つかりません。");
        }

        var size = records[0].SizeBytes;
        var length = new FileInfo(path).Length;
        if (length != size)
        {
            return Refused(path, "記録と大きさが違うので削除しません。");
        }

        // 残る方を先に確かめる（中身を読むのは大きいので、記録と大きさが合う物だけ）
        var survivors = recorded
            .Where(place => !string.Equals(Normalized(place), Normalized(path), StringComparison.OrdinalIgnoreCase))
            .Where(place => File.Exists(place) && new FileInfo(place).Length == size)
            .ToList();

        string? survivor = null;
        foreach (var place in survivors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(await _hash(place, cancellationToken), hash, StringComparison.OrdinalIgnoreCase))
            {
                survivor = place;
                break;
            }
        }

        if (survivor is null)
        {
            return Refused(path, "同じ中身のほかのファイルが手元に見つからないので、削除しません。");
        }

        if (!string.Equals(await _hash(path, cancellationToken), hash, StringComparison.OrdinalIgnoreCase))
        {
            return Refused(path, "記録と中身が違うので削除しません。");
        }

        try
        {
            await _deleteFile(path, cancellationToken);
        }
        catch (NotRecyclableException exception)
        {
            return Refused(path, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Diagnostics.AppLog.Error("重複したファイルを消す", exception);
            return Refused(path, FailureText.Cause(exception));
        }

        // 消した場所の記録は、見回りが外す（同じ中身が同じ記録のほかの場所に在ると確かめるので）。外れなかった記録には「見つからない」の印が付く
        var changed = _sweep is null ? null : await _sweep.NoteFilesAsync(cancellationToken: cancellationToken);
        return new DuplicateRemoval(path, Removed: true, Reason: null, FreedBytes: size, ChangedItemIds: changed);
    }

    /// <summary>
    /// zip と、それを展開したフォルダの組（<see cref="DuplicateKind.ArchiveAndUnpacked"/>）で、**zip だけ**をごみ箱へ送る
    /// （ユーザ判断 2026-11-20。展開したフォルダには触らない）。
    /// 集計は古い写しなので、送る直前に今の記録とディスクで確かめ直す：同じ商品に、zip と名前が合う展開先のフォルダが
    /// 記録に在り・今ディスクに在り・中身が空でないと分かるまでは送らない（展開先を手で消した後に zip を送ると、最後の1つを送る）。
    /// zip の中身が記録と同じかは、ハッシュを取り直して照らす
    /// </summary>
    public async Task<DuplicateRemoval> RemoveArchiveAsync(string itemId, string path, CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        if (loaded.FailedItemIds.Count > 0 || loaded.SkippedMalformed > 0)
        {
            return Refused(path, "読めない商品の記録があり、展開したフォルダが在るか確かめられないので削除しません。通知の画面から直せます。");
        }

        var item = loaded.Items.FirstOrDefault(candidate => candidate.Id == itemId);
        var record = item?.Local.OwnedFiles.FirstOrDefault(file => file.Paths.Any(
            place => string.Equals(Normalized(place), Normalized(path), StringComparison.OrdinalIgnoreCase)));
        if (item is null || record is null)
        {
            return Refused(path, "記録に無い場所なので削除しません。統計を読み直してください。");
        }

        if (!File.Exists(path))
        {
            return Refused(path, "ファイルが見つかりません。");
        }

        if (new FileInfo(path).Length != record.SizeBytes)
        {
            return Refused(path, "記録と大きさが違うので削除しません。");
        }

        // 残る方（展開したフォルダ）を先に確かめる。名前の合わせ方は集計と同じ
        var archiveName = Path.GetFileName(path);
        var unpacked = item.Local.LocalFolders.FirstOrDefault(folder =>
            Directory.Exists(folder.Path)
            && UnpackedFolderDetector.FindMatchingArchive(Path.GetFileName(folder.Path.TrimEnd('\\', '/')), [archiveName]) is not null
            && Directory.EnumerateFileSystemEntries(folder.Path).Any());
        if (unpacked is null)
        {
            return Refused(path, "展開したフォルダが手元に見つからないので、削除しません。");
        }

        if (!string.Equals(await _hash(path, cancellationToken), record.Hash, StringComparison.OrdinalIgnoreCase))
        {
            return Refused(path, "記録と中身が違うので削除しません。");
        }

        try
        {
            await _deleteFile(path, cancellationToken);
        }
        catch (NotRecyclableException exception)
        {
            return Refused(path, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Diagnostics.AppLog.Error("zip を消す", exception);
            return Refused(path, FailureText.Cause(exception));
        }

        // 消した場所の記録は見回りが外す。展開したフォルダの記録はそのまま残る
        var changed = _sweep is null ? null : await _sweep.NoteFilesAsync(cancellationToken: cancellationToken);
        return new DuplicateRemoval(path, Removed: true, Reason: null, FreedBytes: record.SizeBytes, ChangedItemIds: changed);
    }

    private static DuplicateRemoval Refused(string path, string reason) => new(path, Removed: false, reason, FreedBytes: 0);

    private static string Normalized(string path)
        => Path.TrimEndingDirectorySeparator(path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
}
