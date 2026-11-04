using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 共通素体の中のアバターの並び（ユーザ指示 2026-11-04）。商品ページの対応アバター・検索の候補・素体の詳細は、どれもここだけで並べる
/// （場所ごとに並べ方を持つと、並べ替えた順が場所によって食い違う）。
/// </summary>
public static class AvatarBaseOrder
{
    /// <summary>
    /// 素体の一員を並べる。人が並べた順（<see cref="AvatarBaseGroup.MemberOrder"/>）にある物をその順に、
    /// 無い物（並べた後に入った・並べていない）は後ろへ、持っている物が先→名前順で。
    /// </summary>
    /// <param name="memberOrder">人が並べた順。null か空なら並べていない。</param>
    /// <param name="members">並べる物。一員でない ID が <paramref name="memberOrder"/> にあっても無視する。</param>
    public static IReadOnlyList<T> Arrange<T>(
        IReadOnlyList<string>? memberOrder,
        IEnumerable<T> members,
        Func<T, string> idOf,
        Func<T, bool> isOwned,
        Func<T, string> nameOf)
    {
        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        if (memberOrder is not null)
        {
            for (var index = 0; index < memberOrder.Count; index++)
            {
                rank.TryAdd(memberOrder[index], index);
            }
        }

        return members
            .OrderBy(member => rank.TryGetValue(idOf(member), out var position) ? position : int.MaxValue)
            .ThenByDescending(isOwned)
            .ThenBy(nameOf, StringComparer.CurrentCulture)
            .ToList();
    }
}
