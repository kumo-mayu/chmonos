using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 保存した検索（<c>saved-searches.json</c> の中身）。並びは人が決めた順。
///
/// 1件の形は検索の履歴と同じ <see cref="SearchHistoryEntry"/>（打った文字・条件の並び・表示順）に、
/// 名前（<see cref="SearchHistoryEntry.Name"/>）とカードかリストか（<see cref="SearchHistoryEntry.View"/>）を足した物。
/// 同じ「条件一式」を2つの形で持つと、条件の種類を足すたびに両方を直すことになる。
/// 履歴とファイルを分けたのは、履歴は古い物から押し出す・同じ条件を重ねない、保存した物は押し出さず人が並べる、と扱いが違うため
/// </summary>
public sealed class SavedSearchList
{
    public IReadOnlyList<SearchHistoryEntry> Entries { get; init; } = [];
}

/// <summary>
/// 保存した検索を足す・上書きする・名前を変える・消す・並べ替える規則。
/// どれも「今の並び」を受けて新しい並びを返す関数で、錠の中で今の値に当てる（<see cref="Commands.UiCommand.ChangeSavedSearches"/>）。
///
/// **名前で指す。**名前は1つの並びの中で重ならない（大文字小文字と前後の空白は区別しない）。
/// 同じ条件を別の名前で残すのは許す（「夏の衣装」と「夏の衣装（リスト）」を分けたい人がいる）
/// </summary>
public static class SavedSearches
{
    /// <summary>同じ名前か。画面の「既にあります」と、ここでの重なりの判定を同じにする</summary>
    public static bool SameName(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.CurrentCultureIgnoreCase);

    public static SearchHistoryEntry? Find(IEnumerable<SearchHistoryEntry> entries, string name)
        => entries.FirstOrDefault(entry => SameName(entry.Name, name));

    /// <summary>
    /// 末尾に足す。**同じ名前が既にあれば、その場所で置き換える**（画面は「既にあります」で止めるが、
    /// 2つの画面から同時に同じ名前で保存されたときに2行にしない）。名前の空白は前後を落とす
    /// </summary>
    public static IReadOnlyList<SearchHistoryEntry> Add(IEnumerable<SearchHistoryEntry> entries, SearchHistoryEntry entry)
    {
        var list = entries.ToList();
        var named = entry with { Name = entry.Name?.Trim() };
        var at = list.FindIndex(other => SameName(other.Name, named.Name));
        if (at >= 0)
        {
            list[at] = named;
        }
        else
        {
            list.Add(named);
        }

        return list;
    }

    /// <summary>名前と場所はそのまま、中身を今の検索に替える。名前が無ければ（別の所で消された）何もしない</summary>
    public static IReadOnlyList<SearchHistoryEntry> Overwrite(IEnumerable<SearchHistoryEntry> entries, string name, SearchHistoryEntry entry)
        => entries.Select(other => SameName(other.Name, name) ? entry with { Name = other.Name } : other).ToList();

    /// <summary>
    /// 名前を変える。新しい名前がほかの行と重なるなら何もしない（大文字小文字だけを直すのは、同じ行なので通す）
    /// </summary>
    public static IReadOnlyList<SearchHistoryEntry> Rename(IEnumerable<SearchHistoryEntry> entries, string name, string newName)
    {
        var list = entries.ToList();
        var trimmed = newName.Trim();
        if (trimmed.Length == 0 || list.Any(other => SameName(other.Name, trimmed) && !SameName(other.Name, name)))
        {
            return list;
        }

        return list.Select(other => SameName(other.Name, name) ? other with { Name = trimmed } : other).ToList();
    }

    public static IReadOnlyList<SearchHistoryEntry> Remove(IEnumerable<SearchHistoryEntry> entries, string name)
        => entries.Where(other => !SameName(other.Name, name)).ToList();

    /// <summary>1つ上（<paramref name="delta"/> = -1）か下（+1）へ動かす。端では動かさない</summary>
    public static IReadOnlyList<SearchHistoryEntry> Move(IEnumerable<SearchHistoryEntry> entries, string name, int delta)
    {
        var list = entries.ToList();
        var at = list.FindIndex(other => SameName(other.Name, name));
        var to = at + delta;
        if (at < 0 || to < 0 || to >= list.Count)
        {
            return list;
        }

        (list[at], list[to]) = (list[to], list[at]);
        return list;
    }

    // ---- 名前の変更・統合に付いていく（ユーザ判断 2026-10-04） ----
    // 保存した検索の条件はタグ・属性・共通素体を名前の文字で指す。名前を変えても条件が古い名前のままだと、呼び出したとき0件になる。
    // **消したときは何もしない**（残した古い名前が条件の欄に見え、0件の理由が読める。「今は無い値はそのまま条件に入れる」と同じ）。
    // 並びと名前は触らず、条件の中の名前だけを書き換える。モジュールの要約（Summary）は見るだけの控えで、呼び出すと作り直されるので触らない

    private const string UserTagKind = "UserTag";
    private const string AttributeKind = "Attribute";
    private const string AvatarKind = "Avatar";
    private const string BaseKeyPrefix = "base:";

    private static IReadOnlyList<SearchHistoryEntry> RewriteModules(
        IEnumerable<SearchHistoryEntry> entries,
        string kind,
        Func<SearchModuleState, SearchModuleState> rewrite)
        => entries
            .Select(entry => entry.Modules.Any(module => module.Kind == kind)
                ? entry with { Modules = entry.Modules.Select(module => module.Kind == kind ? rewrite(module) : module).ToList() }
                : entry)
            .ToList();

    private static bool SameTag(string? left, string? right)
        => string.Equals(left, right, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// 大分類の名前の変更・統合。統合で同じ大分類の条件が2つになったら、小分類を合わせて1つにする
    /// （「小分類なし」はどちらかが持っていれば持つ。小分類の結び方は、もとから寄せ先の名前だった側のものを残す）
    /// </summary>
    public static IReadOnlyList<SearchHistoryEntry> RenameUserTagTop(IEnumerable<SearchHistoryEntry> entries, string oldName, string newName)
    {
        var target = newName.Trim();
        if (target.Length == 0)
        {
            return entries.ToList();
        }

        var caseOnly = SameTag(oldName, target);
        return RewriteModules(entries, UserTagKind, module =>
        {
            var merged = new List<UserTagCondition>();
            foreach (var condition in module.UserTags)
            {
                var wasOld = SameTag(condition.Top, oldName);
                var renamed = wasOld ? condition with { Top = target } : condition;
                var at = merged.FindIndex(other => SameTag(other.Top, renamed.Top));
                if (at < 0)
                {
                    merged.Add(renamed);
                    continue;
                }

                var existing = merged[at];
                merged[at] = (wasOld && !caseOnly ? existing : renamed) with
                {
                    Top = target,
                    Subs = existing.Subs.Concat(renamed.Subs).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList(),
                    NoSub = existing.NoSub || renamed.NoSub,
                };
            }

            return module with { UserTags = merged };
        });
    }

    /// <summary>小分類の名前の変更・統合（同じ大分類の条件の中だけ）。統合で同じ名前が並んだら1つにする</summary>
    public static IReadOnlyList<SearchHistoryEntry> RenameUserTagSub(IEnumerable<SearchHistoryEntry> entries, string top, string oldName, string newName)
    {
        var target = newName.Trim();
        if (target.Length == 0)
        {
            return entries.ToList();
        }

        return RewriteModules(entries, UserTagKind, module => module with
        {
            UserTags = module.UserTags.Select(condition => SameTag(condition.Top, top)
                ? condition with
                {
                    Subs = condition.Subs.Select(sub => SameTag(sub, oldName) ? target : sub)
                        .Distinct(StringComparer.CurrentCultureIgnoreCase).ToList(),
                }
                : condition).ToList(),
        });
    }

    /// <summary>
    /// 小分類を別の大分類へ移したのに付いていく。「元の大分類の小分類 x」を「移動先の大分類の小分類 x」へ書き換える。
    /// 元の条件から x を抜いて小分類が1つも残らなくなったら、元の条件は消す（残すと「その大分類の全部」へ広がってしまう）。
    /// 移動先に条件が既にあれば小分類を足して1つにする。ただし移動先が小分類を絞っていない（その大分類の全部）なら、
    /// x はもう含まれているので足さない（足すと絞り込みになり、条件が狭まってしまう）
    /// </summary>
    public static IReadOnlyList<SearchHistoryEntry> MoveUserTagSub(
        IEnumerable<SearchHistoryEntry> entries, string fromTop, string sub, string toTop, string subSpelling)
        => RewriteModules(entries, UserTagKind, module =>
        {
            var result = new List<UserTagCondition>();
            var carried = false;
            var carriedMatchAll = false;
            var carriedAt = -1;
            foreach (var condition in module.UserTags)
            {
                if (!SameTag(condition.Top, fromTop) || !condition.Subs.Any(entry => SameTag(entry, sub)))
                {
                    result.Add(condition);
                    continue;
                }

                var rest = condition.Subs.Where(entry => !SameTag(entry, sub)).ToList();
                if (!carried)
                {
                    carried = true;
                    carriedMatchAll = condition.MatchAll;
                    carriedAt = result.Count;
                }

                if (rest.Count > 0 || condition.NoSub)
                {
                    result.Add(condition with { Subs = rest });
                    carriedAt = -1;
                }
            }

            if (carried)
            {
                AddSubTo(result, toTop, subSpelling, carriedMatchAll, carriedAt);
            }

            return module with { UserTags = result };
        });

    /// <summary>
    /// 大分類を別の大分類の小分類にしたのに付いていく。「大分類 X」の条件を「移動先の大分類の小分類 X」へ書き換える。
    /// X が小分類を持つ条件は、小分類のある大分類は小分類にできない決まり（断られて何も変わらない）なので触らない
    /// </summary>
    public static IReadOnlyList<SearchHistoryEntry> NestUserTagTop(
        IEnumerable<SearchHistoryEntry> entries, string top, string intoTop, string subSpelling)
        => RewriteModules(entries, UserTagKind, module =>
        {
            var result = new List<UserTagCondition>();
            var carried = false;
            var carriedMatchAll = false;
            var carriedAt = -1;
            foreach (var condition in module.UserTags)
            {
                if (!SameTag(condition.Top, top) || condition.Subs.Count > 0)
                {
                    result.Add(condition);
                    continue;
                }

                if (!carried)
                {
                    carried = true;
                    carriedMatchAll = condition.MatchAll;
                    carriedAt = result.Count;
                }
            }

            if (carried)
            {
                AddSubTo(result, intoTop, subSpelling, carriedMatchAll, carriedAt);
            }

            return module with { UserTags = result };
        });

    /// <summary>移動先の条件へ小分類を足す。無ければ <paramref name="insertAt"/>（元の条件があった位置。無ければ末尾）に作る</summary>
    private static void AddSubTo(List<UserTagCondition> conditions, string top, string sub, bool matchAll, int insertAt)
    {
        var at = conditions.FindIndex(condition => SameTag(condition.Top, top));
        if (at < 0)
        {
            var created = new UserTagCondition { Top = top, Subs = [sub], MatchAll = matchAll };
            conditions.Insert(insertAt >= 0 ? insertAt : conditions.Count, created);
            return;
        }

        var existing = conditions[at];
        if (existing.HasSubConditions)
        {
            conditions[at] = existing with { Subs = existing.Subs.Append(sub).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList() };
        }
    }

    /// <summary>
    /// 属性の名前の変更・統合。幅の条件と、その属性で並べた表示順（「質感 が高い順」）の両方を寄せる。
    /// 統合で両方の幅を持っていたら、寄せ先の幅を残す（商品側の統合の既定「寄せ先の値を残す」と同じ向き）
    /// </summary>
    public static IReadOnlyList<SearchHistoryEntry> RenameAttribute(IEnumerable<SearchHistoryEntry> entries, string oldName, string newName)
    {
        var target = newName.Trim();
        if (target.Length == 0)
        {
            return entries.ToList();
        }

        var caseOnly = SameTag(oldName, target);
        var renamed = RewriteModules(entries, AttributeKind, module =>
        {
            var hasTarget = module.Ranges.Any(range => SameTag(range.Name, target));
            return module with
            {
                Ranges = module.Ranges
                    .Where(range => caseOnly || !SameTag(range.Name, oldName) || !hasTarget)
                    .Select(range => SameTag(range.Name, oldName) ? range with { Name = target } : range)
                    .ToList(),
            };
        });

        return renamed.Select(entry => entry with { Sort = RenameSort(entry.Sort, oldName, target) }).ToList();
    }

    private static string? RenameSort(string? sort, string oldName, string target)
    {
        foreach (var suffix in new[] { " が高い順", " が低い順" })
        {
            if (sort == oldName + suffix)
            {
                return target + suffix;
            }
        }

        return sort;
    }

    /// <summary>共通素体の名前の変更・統合。対応アバターの条件の中の「base:名前」の鍵を寄せる</summary>
    public static IReadOnlyList<SearchHistoryEntry> RenameBase(IEnumerable<SearchHistoryEntry> entries, string oldName, string newName)
    {
        var target = newName.Trim();
        if (target.Length == 0)
        {
            return entries.ToList();
        }

        return RewriteModules(entries, AvatarKind, module => module with
        {
            Items = module.Items
                .Select(key => key.StartsWith(BaseKeyPrefix, StringComparison.Ordinal) && SameTag(key[BaseKeyPrefix.Length..], oldName)
                    ? BaseKeyPrefix + target
                    : key)
                .Distinct(StringComparer.Ordinal).ToList(),
        });
    }
}
