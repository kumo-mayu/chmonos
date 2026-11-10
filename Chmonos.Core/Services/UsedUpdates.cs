using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 使っている商品の更新の促し（<c>docs/spec/used-updates.md</c>）：控えの作り方と、改変の行ごとの判定。
/// 商品として促すかは <see cref="LocalBlock.HasPendingContentUpdate"/>。
/// </summary>
public static class UsedUpdates
{
    /// <summary>
    /// 促す更新を見つけた時の控え。その時に持っているファイル（外した物・古い版を除く）を控える。前の控えと「確認した」は捨てる。
    /// </summary>
    /// <param name="targets">特定のアバター・素体向けの更新なら、その向き先（<see cref="UsedUpdateKind.TargetsOf"/>）。全部向けなら null。</param>
    public static ContentUpdate Found(LocalBlock local, DateTimeOffset now, UpdateTargets? targets = null) => new()
    {
        FoundAt = now,
        FilesBefore = local.OwnedFiles.Select(file => file.Hash).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        ForAvatars = targets is { Avatars.Count: > 0 } ? targets.Avatars : null,
        ForBases = targets is { Bases.Count: > 0 } ? targets.Bases : null,
    };

    /// <summary>
    /// その更新が、このアバターで使っている所に当たるか（ユーザ判断 2026-11-09：特定のアバター向けの更新は、そのアバターで使う所だけ促す）。
    /// 全部向けの更新・アバターの分からない所（改変に紐付かない Unity のプロジェクト）は当たる。
    /// アバターが向き先に入っているか、アバターの共通素体が向き先の素体なら当たる。
    /// </summary>
    /// <param name="avatarItemId">使っている所のアバター（改変のアバター）。分からなければ null。</param>
    public static bool AppliesTo(ContentUpdate update, string? avatarItemId, AvatarRegistry registry)
    {
        if (!update.IsTargeted || avatarItemId is null)
        {
            return true;
        }

        if (update.ForAvatars?.Contains(avatarItemId, StringComparer.Ordinal) == true)
        {
            return true;
        }

        return update.ForBases is { Count: > 0 } bases
            && registry.Entries.FirstOrDefault(entry => entry.ItemId == avatarItemId)?.BaseName is { } baseName
            && bases.Contains(baseName, AvatarBaseNames.Comparer);
    }

    /// <summary>
    /// 改変の行に札を出すか（行ごと・ユーザ判断 2026-11-09）。
    /// その行が使ったファイルが、更新を見つける前から持っていた物で、この改変で「確認した」を押しておらず、改変のアバターに当たる更新なら出す。
    /// 新しいファイルで送り直せば、行のファイルが控えに無い物になって消える。
    /// 使ったファイルが分からない行（手で足して選ばなかった・フォルダの中の物）は、商品の印に従う。外した行には出さない。
    /// </summary>
    /// <param name="modificationAvatarId">その改変のアバター。</param>
    public static bool IsMemberBehind(ItemRecord item, ModificationMember member, string? modificationAvatarId = null, AvatarRegistry? registry = null)
    {
        if (member.Detached || item.Local.ContentUpdate is not { } update)
        {
            return false;
        }

        if (member.UpdateCheckedAt is { } checkedAt && checkedAt >= update.FoundAt)
        {
            return false;
        }

        if (!AppliesTo(update, modificationAvatarId, registry ?? new AvatarRegistry()))
        {
            return false;
        }

        return member.FileHash is { } hash
            ? update.FilesBefore.Contains(hash, StringComparer.OrdinalIgnoreCase)
            : item.Local.HasPendingContentUpdate;
    }
}
