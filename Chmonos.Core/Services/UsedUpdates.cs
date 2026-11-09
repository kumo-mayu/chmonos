using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 使っている商品の更新の促し（<c>docs/spec/used-updates.md</c>）：控えの作り方と、改変の行ごとの判定。
/// 商品として促すかは <see cref="LocalBlock.HasPendingContentUpdate"/>。
/// </summary>
public static class UsedUpdates
{
    /// <summary>促す更新を見つけた時の控え。その時に持っているファイル（外した物・古い版を除く）を控える。前の控えと「確認した」は捨てる。</summary>
    public static ContentUpdate Found(LocalBlock local, DateTimeOffset now) => new()
    {
        FoundAt = now,
        FilesBefore = local.OwnedFiles.Select(file => file.Hash).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
    };

    /// <summary>
    /// 改変の使ったものの行に札を出すか（行ごと・ユーザ判断 2026-11-09）。
    /// その行が使ったファイルが、更新を見つける前から持っていた物で、この改変で「確認した」を押していなければ出す。
    /// 新しいファイルで送り直せば、行のファイルが控えに無い物になって消える。
    /// 使ったファイルが分からない行（手で足して選ばなかった・フォルダの中の物）は、商品の印に従う。外した行には出さない。
    /// </summary>
    public static bool IsMemberBehind(ItemRecord item, ModificationMember member)
    {
        if (member.Detached || item.Local.ContentUpdate is not { } update)
        {
            return false;
        }

        if (member.UpdateCheckedAt is { } checkedAt && checkedAt >= update.FoundAt)
        {
            return false;
        }

        return member.FileHash is { } hash
            ? update.FilesBefore.Contains(hash, StringComparer.OrdinalIgnoreCase)
            : item.Local.HasPendingContentUpdate;
    }
}
