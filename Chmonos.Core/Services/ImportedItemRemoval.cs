using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>消した数と、人が手を入れていたので残した数と、未確定に戻したファイルの数。</summary>
public readonly record struct ImportedItemsRemoved(int Deleted, int Kept, int ReturnedFiles = 0);

/// <summary>
/// 取り込みの中断の窓の「中断して、この取り込みで新しく登録した n 件を削除する」（open 119・ユーザ判断 2026-11-19）。
///
/// 消す相手は取り込みが記録した <see cref="ImportState.CreatedItems"/> だけ（推し量らない）。**消すかは商品の錠の中で今の値を見て決める**——
/// 取り込みの間に人が名前・メモ・タグなどを入れた商品は、窓を出した後に入れた物も含めて残す。
/// 消すのは商品の JSON・控え（<c>.prev</c>）・その商品のために取った説明と画像（<see cref="ItemRepository.DeleteIfAsync"/>）。
/// ディスクの購入ファイルには触らない。消した商品が持っていたファイルは**未確定に戻す**（open 119・ユーザ判断 2026-11-20）——
/// 戻さないと、ファイルはどの一覧にも出ず、取り込み直すまで手の付けようが無い。戻し方は商品ページでファイルを外した時と同じ
/// （<see cref="LocalFileEditor.DetachFileAsync"/>。実体のあるパスだけ・ほかの商品が持つ中身は戻さない・同じ中身は1行）。
/// フォルダの登録は未確定に出す形が無いので戻さない。消した商品は記録から外す（押し直しで数え直さない）。
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
        var released = new List<LocalFileRecord>();
        foreach (var id in ids)
        {
            var touched = false;
            IReadOnlyList<LocalFileRecord> files = [];
            if (await store.Items.DeleteIfAsync(
                    id,
                    item =>
                    {
                        // 戻すファイルは消す錠の中の今の値で決める（窓を出した後に取り込みが足したファイルも戻す）。
                        // 古い版（上書きで残った記録）は戻さない：同じパスに今あるのは新しい中身で、記録の中身と違う
                        touched = HasHumanInput(item.Local);
                        files = item.Local.OwnedFiles;
                        return !touched;
                    },
                    cancellationToken))
            {
                deleted++;
                gone.Add(id);
                released.AddRange(files);
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

        var returned = await ReturnToUnresolvedAsync(released, cancellationToken);
        return new ImportedItemsRemoved(deleted, kept, returned);
    }

    /// <summary>
    /// 消した商品が持っていたファイルを未確定に戻す。ほかの商品（残した商品・前からある商品）がまだ持つ中身は戻さない——
    /// 行き先は決まっていて、未確定に出すとそこから除外・別の商品への登録ができ、持っている商品の手元の物まで変わる（DetachFileAsync と同じ）。
    /// ほかの持ち主は、消し終えた後に全件を1回だけ読んで見る（ファイルごとに読むと、件数×全件になる）
    /// </summary>
    private async Task<int> ReturnToUnresolvedAsync(List<LocalFileRecord> released, CancellationToken cancellationToken)
    {
        if (released.Count == 0)
        {
            return 0;
        }

        var loaded = await store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var heldElsewhere = loaded.Items
            .SelectMany(item => item.Local.AttachedFiles)
            .Select(file => file.Hash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var entries = new List<UnresolvedFile>();
        foreach (var file in released.DistinctBy(file => file.Hash, StringComparer.OrdinalIgnoreCase))
        {
            if (heldElsewhere.Contains(file.Hash))
            {
                continue;
            }

            // 実体が残っているものだけ戻す。既に消えているファイルを並べても、紐付け直す相手がいない
            var alive = file.Paths.Where(File.Exists).ToList();
            if (alive.Count > 0)
            {
                entries.Add(LocalFileEditor.ToUnresolved(file, alive));
            }
        }

        return await LocalFileEditor.AddUnresolvedAsync(store, entries, cancellationToken);
    }

    /// <summary>
    /// 人が手を入れたか。人が決める欄（<see cref="LocalOwners.EditScreen"/>・画像・お気に入り）のどれかが入っていれば残す。
    /// 取り込みと見回りが自分で書く欄は、自動で書いた値なら見ない——見ると取り込んだ商品が全部「残した」になり、何も消えない：
    /// ファイルとフォルダ（<see cref="LocalOwners.Import"/>）、検出した対応アバター・素体（<see cref="LocalOwners.Detection"/>。
    /// 検出も「確かめ済み」を付けるので、人の操作が付ける Manual と違うと言った行だけ見る）、変換扱いの推した値（<c>ConversionScan</c>。人が決めた形・確かめた行だけ見る）、
    /// 取得の記録と中身の更新の控え。手で付けたファイルとフォルダは取り込みの物と見分けられないので見ない
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
            || local.Purchases?.Count > 0
            || local.AcquiredAt is not null
            || !local.NotifyOnUpdate
            || local.IsHidden
            || local.IsFavorite
            || local.Avatars?.Any(link => link.Source == AvatarLinkSource.Manual || link.Rejected) == true
            || local.AvatarBases?.Any(link => link.Source == AvatarLinkSource.Manual || link.Rejected) == true
            || local.Conversion is { } conversion
                && (conversion.Mode != ConversionMode.Auto || conversion.Targets.Any(target => target.Confirmed || target.Rejected));
}
