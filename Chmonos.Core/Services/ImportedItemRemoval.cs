using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>消した数と、人が手を入れていたので残した数。</summary>
public readonly record struct ImportedItemsRemoved(int Deleted, int Kept);

/// <summary>
/// 取り込みの中断の窓の「中断して、この取り込みで新しく登録した n 件を削除する」（open 119・ユーザ判断 2026-11-19）。
///
/// 消す相手は取り込みが記録した <see cref="ImportState.CreatedItems"/> だけ（推し量らない）。**消すかは商品の錠の中で今の値を見て決める**——
/// 取り込みの間に人が名前・メモ・タグなどを入れた商品は、窓を出した後に入れた物も含めて残す。
/// 消すのは商品の JSON・控え（<c>.prev</c>）・その商品のために取った説明と画像（<see cref="ItemRepository.DeleteIfAsync"/>）。
/// ディスクの購入ファイルには触らない。消した商品は記録から外す（押し直しで数え直さない）。
/// **取り込みが止まってから呼ぶ**（走っている間に消すと、後の段がまた書くことがある）
/// </summary>
public sealed class ImportedItemRemoval(DataStore store)
{
    /// <summary>今の記録で消す候補の数（窓の n）。人が手を入れた物も含む（残すかは消す時に錠の中で決める）。</summary>
    public int CountCandidates() => (store.ImportState.Load().CreatedItems ?? []).Count;

    public async Task<ImportedItemsRemoved> RemoveAsync(CancellationToken cancellationToken = default)
    {
        var ids = store.ImportState.Load().CreatedItems ?? [];
        var gone = new HashSet<string>(StringComparer.Ordinal);
        var deleted = 0;
        var kept = 0;
        foreach (var id in ids)
        {
            var touched = false;
            if (await store.Items.DeleteIfAsync(
                    id,
                    item =>
                    {
                        touched = HasHumanInput(item.Local);
                        return !touched;
                    },
                    cancellationToken))
            {
                deleted++;
                gone.Add(id);
            }
            else if (touched)
            {
                kept++;
            }
            else
            {
                // もう無い（取り込みの後に人が消した・ID を変えた）。数えずに記録から外す
                gone.Add(id);
            }
        }

        if (gone.Count > 0)
        {
            await store.ImportState.UpdateAsync(
                state => state with
                {
                    CreatedItems = (state.CreatedItems ?? []).Where(id => !gone.Contains(id)).ToList() is { Count: > 0 } left ? left : null,
                },
                cancellationToken);
        }

        return new ImportedItemsRemoved(deleted, kept);
    }

    /// <summary>
    /// 人が手を入れたか。取り込みが作る商品は、ファイル・通知の既定・取得の記録しか持たない（<c>ImportPipeline.Fetch</c>）。
    /// 人が決める欄（<see cref="LocalOwners.EditScreen"/> と画像・お気に入り・変換扱い・登録したフォルダ）のどれかが入っていれば残す。
    /// 購入の記録は購入履歴の取り込みでも入るが、消すと取り直すまで戻らないので残す側に倒す。
    /// 対応アバター（検出が書く）と通知の設定（既定のまま作る）は見ない
    /// </summary>
    public static bool HasHumanInput(LocalBlock local)
        => !string.IsNullOrEmpty(local.DisplayName)
            || local.Shop is not null
            || !string.IsNullOrEmpty(local.Category)
            || local.UserImages?.Count > 0
            || local.ThumbnailImage is not null
            || local.ImageRoles?.Count > 0
            || local.UserTags?.Count > 0
            || local.Attributes?.Count > 0
            || !string.IsNullOrEmpty(local.Memo)
            || local.VariationTargets?.Count > 0
            || local.Conversion is not null
            || local.Purchases?.Count > 0
            || local.LocalFolders?.Count > 0
            || local.AcquiredAt is not null
            || local.IsHidden
            || local.IsFavorite;
}
