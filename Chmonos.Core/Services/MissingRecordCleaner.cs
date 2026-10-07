using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>見つからない記録を消した（消す）数。</summary>
/// <param name="Items">記録を消した商品の数。</param>
/// <param name="Files">消したファイルの記録の数。</param>
/// <param name="Folders">消したフォルダの記録の数。</param>
/// <param name="Unowned">消した後に手元のファイルが無くなる（未所持になる）商品の数。</param>
public sealed record MissingRecordCleanup(int Items, int Files, int Folders, int Unowned);

/// <summary>
/// 見つからないファイル・フォルダの記録を、全部の商品からまとめて消す（ユーザ判断 2026-10-07）。
///
/// 大量に見つからなくなる（ダウンロードの置き場ごと消した・移した後で古い所を片付けた）と、「見つからない」のチップが
/// 大量の商品に出続ける。1件ずつ判断して外すのは、件数が多いと我慢を強いるので、判断なしで片付けられるようにする。
/// **外した印（<see cref="LocalFileRecord.Detached"/>）は付けずに、記録ごと消す。**外した印は「この商品の物ではない」の意味で、
/// 次の取り込みで同じ商品へ戻るのを止める。見つからないだけの物は、後でファイルが戻れば取り込みでまた紐づいてよい。
///
/// 消さない物：今どこかの場所に在る物（日時が古いだけ）・つながっていないドライブの上の物（外付けを外しているだけかもしれない）・
/// 外した物・上書きで残った古い版（チップには数えない）。商品そのもの（購入の記録・メモなど）は消さない。
/// </summary>
public sealed class MissingRecordCleaner(DataStore store)
{
    /// <summary>消すとどうなるかを数える（読むだけ）。</summary>
    public async Task<MissingRecordCleanup> PlanAsync(CancellationToken cancellationToken = default)
    {
        var load = await store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        return await Task.Run(
            () =>
            {
                int items = 0, files = 0, folders = 0, unowned = 0;
                foreach (var item in load.Items.Where(item => item.HasMissingFile))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Strip(item.Local) is { } stripped)
                    {
                        items++;
                        files += stripped.Files;
                        folders += stripped.Folders;
                        unowned += item.Local.IsOwned && !stripped.Next.IsOwned ? 1 : 0;
                    }
                }

                return new MissingRecordCleanup(items, files, folders, unowned);
            },
            cancellationToken);
    }

    /// <summary>消す。商品ごとの錠の中で今の値に当てる（数えた後に取り込みが結び直した物は消さない）。</summary>
    public async Task<MissingRecordCleanup> ForgetAsync(CancellationToken cancellationToken = default)
    {
        var load = await store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        int items = 0, files = 0, folders = 0, unowned = 0;
        foreach (var item in load.Items.Where(item => item.HasMissingFile))
        {
            cancellationToken.ThrowIfCancellationRequested();
            (LocalBlock Next, int Files, int Folders)? applied = null;
            var wasOwned = false;
            await store.Items.ChangeLocalAsync(
                item.Id,
                local =>
                {
                    wasOwned = local.IsOwned;
                    applied = Strip(local);
                    return applied?.Next;
                },
                [LocalField.LocalFiles, LocalField.LocalFolders],
                cancellationToken);

            if (applied is { } done)
            {
                items++;
                files += done.Files;
                folders += done.Folders;
                unowned += wasOwned && !done.Next.IsOwned ? 1 : 0;
            }
        }

        return new MissingRecordCleanup(items, files, folders, unowned);
    }

    /// <summary>見つからない記録を外した値。消す物が無ければ null。ディスクを見る（在るか・ドライブがつながっているか）。</summary>
    internal static (LocalBlock Next, int Files, int Folders)? Strip(LocalBlock local)
    {
        var keptFiles = (local.LocalFiles ?? []).Where(file => !IsGone(file)).ToList();
        var keptFolders = (local.LocalFolders ?? []).Where(folder => !IsGone(folder)).ToList();
        var files = (local.LocalFiles ?? []).Count - keptFiles.Count;
        var folders = (local.LocalFolders ?? []).Count - keptFolders.Count;
        return files + folders == 0
            ? null
            : (local with { LocalFiles = keptFiles, LocalFolders = keptFolders }, files, folders);
    }

    private static bool IsGone(LocalFileRecord file)
        => !file.Detached
            && !file.IsOldVersion
            && (file.Paths.Count == 0 || file.MissingSince is not null)
            && !file.Paths.Any(path => File.Exists(path) || UnresolvedMerge.IsOnMissingVolume(path));

    private static bool IsGone(LocalFolderRecord folder)
        => folder.MissingSince is not null
            && !Directory.Exists(folder.Path)
            && !UnresolvedMerge.IsOnMissingVolume(folder.Path);
}
