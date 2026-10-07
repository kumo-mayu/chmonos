using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>見つからない記録を消した（消す）数。</summary>
/// <param name="Items">記録を消した商品の数。</param>
/// <param name="Files">消したファイルの記録の数。</param>
/// <param name="Folders">消したフォルダの記録の数。</param>
/// <param name="Unowned">消した後に手元のファイルが無くなる（未所持になる）商品の数。</param>
/// <param name="Failed">書けずに消せなかった商品の数（消す時だけ）。</param>
public sealed record MissingRecordCleanup(int Items, int Files, int Folders, int Unowned, int Failed = 0);

/// <summary>
/// 消す相手の控え（数えた時に「無い」と確かめた物）。消す時はこの範囲だけを見る（外部の点検 2026-10-07。
/// 前は消す時に全部を選び直していたので、確かめの窓を出している間に見回りが日時を付けた物まで消し、窓の件数を超えた）
/// </summary>
public sealed record MissingRecordPlan(
    MissingRecordCleanup Counts,
    IReadOnlyDictionary<string, MissingRecordTargets> Targets);

/// <summary>1つの商品で消す相手（ファイルはハッシュ、フォルダは場所）。</summary>
public sealed record MissingRecordTargets(IReadOnlySet<string> Hashes, IReadOnlySet<string> Folders);

/// <summary>
/// 見つからないファイル・フォルダの記録を、全部の商品からまとめて消す（ユーザ判断 2026-10-07）。
///
/// 大量に見つからなくなる（ダウンロードの置き場ごと消した・移した後で古い所を片付けた）と、「見つからない」のチップが
/// 大量の商品に出続ける。1件ずつ判断して外すのは、件数が多いと我慢を強いるので、判断なしで片付けられるようにする。
/// **外した印（<see cref="LocalFileRecord.Detached"/>）は付けずに、記録ごと消す。**外した印は「この商品の物ではない」の意味で、
/// 次の取り込みで同じ商品へ戻るのを止める。見つからないだけの物は、後でファイルが戻れば取り込みでまた紐づいてよい。
///
/// **消すのは「無い」と確かめられた物だけ**（見回りと同じ <see cref="FilePresenceProbe"/>）。今どこかに在る物・
/// つながっていないドライブの上の物・同じ文字に別のディスクが来ている物・権限が無くて確かめられない物は残す（外部の点検 2026-10-07。
/// 前は在るかを <c>File.Exists</c> だけで見ていて、権限が無い・別のディスクの物も「無い」として消せた）。
/// 外した物・上書きで残った古い版（チップには数えない）も残す。商品そのもの（購入の記録・メモなど）は消さない。
/// </summary>
public sealed class MissingRecordCleaner(DataStore store, Func<FilePresenceProbe>? newProbe = null)
{
    private readonly Func<FilePresenceProbe> _newProbe = newProbe ?? (() => new FilePresenceProbe());

    /// <summary>消す相手を確かめて数える（読むだけ。画面のスレッドの外で確かめる）。</summary>
    public async Task<MissingRecordPlan> PlanAsync(CancellationToken cancellationToken = default)
    {
        var load = await store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        return await Task.Run(
            () =>
            {
                var probe = _newProbe();
                var targets = new Dictionary<string, MissingRecordTargets>(StringComparer.Ordinal);
                int files = 0, folders = 0, unowned = 0;
                foreach (var item in load.Items.Where(item => item.HasMissingFile))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var found = Find(item.Local, probe);
                    if (found.Hashes.Count + found.Folders.Count == 0)
                    {
                        continue;
                    }

                    targets[item.Id] = found;
                    files += found.Hashes.Count;
                    folders += found.Folders.Count;
                    unowned += item.Local.IsOwned && !Strip(item.Local, found)!.IsOwned ? 1 : 0;
                }

                return new MissingRecordPlan(new MissingRecordCleanup(targets.Count, files, folders, unowned), targets);
            },
            cancellationToken);
    }

    /// <summary>
    /// 控えた相手の範囲で消す。消す直前にもう一度確かめ（数えた後に戻った物は残す）、商品ごとの錠の中で今の値に当てる。
    /// 書けない商品があっても続け、数を返す。消した場所は走査の控えからも外す（外さないと、戻したファイルを監視が新しいと数えない）
    /// </summary>
    public Task<MissingRecordCleanup> ForgetAsync(MissingRecordPlan plan, CancellationToken cancellationToken = default)
        => Task.Run(() => ForgetCoreAsync(plan, cancellationToken), cancellationToken);

    private async Task<MissingRecordCleanup> ForgetCoreAsync(MissingRecordPlan plan, CancellationToken cancellationToken)
    {
        var probe = _newProbe();
        var forgottenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int items = 0, files = 0, folders = 0, unowned = 0, failed = 0;
        foreach (var (itemId, planned) in plan.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await store.Items.LoadAsync(itemId, cancellationToken) is not { } item)
                {
                    continue;
                }

                // 控えた相手のうち、今も「無い」と確かめられる物だけ（錠の外で見る。錠の中では記録だけで当てる）
                var now = Find(item.Local, probe);
                var targets = new MissingRecordTargets(
                    now.Hashes.Where(planned.Hashes.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase),
                    now.Folders.Where(planned.Folders.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase));
                if (targets.Hashes.Count + targets.Folders.Count == 0)
                {
                    continue;
                }

                LocalBlock? before = null;
                LocalBlock? after = null;
                await store.Items.ChangeLocalAsync(
                    itemId,
                    local =>
                    {
                        before = local;
                        after = Strip(local, targets);
                        return after;
                    },
                    [LocalField.LocalFiles, LocalField.LocalFolders],
                    cancellationToken);

                if (before is null || after is null)
                {
                    continue;
                }

                var removedFiles = before.LocalFiles.Where(file => !after.LocalFiles.Contains(file)).ToList();
                var removedFolders = before.LocalFolders.Where(folder => !after.LocalFolders.Contains(folder)).ToList();
                items++;
                files += removedFiles.Count;
                folders += removedFolders.Count;
                unowned += before.IsOwned && !after.IsOwned ? 1 : 0;
                forgottenPaths.UnionWith(removedFiles.SelectMany(file => file.Paths));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Diagnostics.AppLog.Error("見つからない記録をまとめて消す", exception);
                failed++;
            }
        }

        if (forgottenPaths.Count > 0)
        {
            try
            {
                await store.ScanCache.UpdateAsync(
                    entries => [.. (entries ?? []).Where(entry => !forgottenPaths.Contains(entry.Path))],
                    cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 記録は消せている。控えが残ると、同じファイルを戻しても監視は新しいと数えないが、取り込み直せば紐付く
                Diagnostics.AppLog.Error("見つからない記録を消した後に走査の控えを片付ける", exception);
            }
        }

        return new MissingRecordCleanup(items, files, folders, unowned, failed);
    }

    /// <summary>「無い」と確かめられた、消してよい記録（ディスクを見る。錠の外で呼ぶ）。</summary>
    internal static MissingRecordTargets Find(LocalBlock local, FilePresenceProbe probe)
    {
        var hashes = (local.LocalFiles ?? [])
            .Where(file => !file.Detached
                && !file.IsOldVersion
                && (file.Paths.Count == 0 || file.MissingSince is not null)
                && (file.Paths.Count == 0 || probe.Of(file) == FilePresence.Missing))
            .Select(file => file.Hash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var folders = (local.LocalFolders ?? [])
            .Where(folder => folder.MissingSince is not null && probe.OfFolder(folder.Path, folder.Volume) == FilePresence.Missing)
            .Select(folder => folder.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new MissingRecordTargets(hashes, folders);
    }

    /// <summary>相手の記録を外した値（記録だけで当てる。錠の中で呼ぶ）。消す物が無ければ null。</summary>
    internal static LocalBlock? Strip(LocalBlock local, MissingRecordTargets targets)
    {
        var keptFiles = (local.LocalFiles ?? [])
            .Where(file => !(targets.Hashes.Contains(file.Hash) && !file.Detached && !file.IsOldVersion
                && (file.Paths.Count == 0 || file.MissingSince is not null)))
            .ToList();
        var keptFolders = (local.LocalFolders ?? [])
            .Where(folder => !(targets.Folders.Contains(folder.Path) && folder.MissingSince is not null))
            .ToList();
        return keptFiles.Count == (local.LocalFiles ?? []).Count && keptFolders.Count == (local.LocalFolders ?? []).Count
            ? null
            : local with { LocalFiles = keptFiles, LocalFolders = keptFolders };
    }
}
