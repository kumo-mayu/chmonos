using System.Text.Json.Serialization;

namespace Chmonos.Core.Models;

/// <summary>
/// 検索の絞り込みの「ユーザータグ」で、大分類1つぶんの条件（ユーザ指示 2026-09-28）。
///
/// 欄の候補にはまず大分類だけを出し、選んだ大分類ごとに小分類を足していく。
/// 小分類を1つも足していなければ「この大分類が付いている商品」で絞る。
/// 前は「大分類」と「大分類 › 小分類」を1本の候補に混ぜていて、小分類の多い大分類では候補が長くなり、
/// 「大分類は付けたが小分類はまだ」の商品を探す手が無かった。
/// </summary>
public sealed record UserTagCondition
{
    /// <summary>大分類の名前（商品は名前で参照している）。</summary>
    public required string Top { get; init; }

    /// <summary>足した小分類の名前。</summary>
    public IReadOnlyList<string> Subs { get; init; } = [];

    /// <summary>
    /// 「小分類なし」を足したか。大分類は付いているが小分類が1つも付いていない商品に当たる。
    /// 小分類の名前の並びに目印の文字として混ぜず、別の欄にする——同じ名前の小分類を作れてしまい、JSON を開いても区別できない。
    /// </summary>
    public bool NoSub { get; init; }

    /// <summary>足した小分類を全部満たす（AND）か。false はどれか（OR）。大分類ごとに選ぶ。</summary>
    public bool MatchAll { get; init; }

    /// <summary>小分類で絞っているか。絞っていなければ大分類が付いているだけで当たる。</summary>
    [JsonIgnore]
    public bool HasSubConditions => NoSub || Subs.Count > 0;

    /// <summary>商品に付いたユーザータグがこの条件に当たるか。名前は大文字小文字を区別しない（編集画面の候補と同じ）。</summary>
    public bool Matches(IEnumerable<UserTagAssignment> tags)
    {
        var assignment = tags.FirstOrDefault(entry => string.Equals(entry.Top, Top, StringComparison.CurrentCultureIgnoreCase));
        if (assignment is null)
        {
            return false;
        }

        if (!HasSubConditions)
        {
            return true;
        }

        var parts = Subs.Select(sub => assignment.Subs.Contains(sub, StringComparer.CurrentCultureIgnoreCase));
        if (NoSub)
        {
            parts = parts.Prepend(assignment.Subs.Count == 0);
        }

        return MatchAll ? parts.All(hit => hit) : parts.Any(hit => hit);
    }

    /// <summary>
    /// 大分類の条件を並べて照らす。大分類どうしは <paramref name="matchAll"/> で結ぶ（前の「候補から積む」形と同じく、既定は OR）。
    /// 条件が1つも無ければ絞らない。
    /// </summary>
    public static bool MatchesAll(IReadOnlyList<UserTagCondition> conditions, bool matchAll, IEnumerable<UserTagAssignment> tags)
    {
        if (conditions.Count == 0)
        {
            return true;
        }

        var assigned = tags as IReadOnlyCollection<UserTagAssignment> ?? tags.ToList();
        return matchAll
            ? conditions.All(condition => condition.Matches(assigned))
            : conditions.Any(condition => condition.Matches(assigned));
    }
}
