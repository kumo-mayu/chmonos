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
            // 置き換える行のフォルダは残す（同じ名前の行を別の所から足しただけで、人が入れたフォルダから出さない）
            list[at] = named with { Folder = list[at].Folder };
        }
        else
        {
            list.Add(named);
        }

        return list;
    }

    /// <summary>名前と場所はそのまま、中身を今の検索に替える。名前が無ければ（別の所で消された）何もしない</summary>
    public static IReadOnlyList<SearchHistoryEntry> Overwrite(IEnumerable<SearchHistoryEntry> entries, string name, SearchHistoryEntry entry)
        => entries.Select(other => SameName(other.Name, name) ? entry with { Name = other.Name, Folder = other.Folder } : other).ToList();

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

    /// <summary>
    /// 1つ上（<paramref name="delta"/> = -1）か下（+1）へ動かす。**同じフォルダの中の隣**と入れ替え、フォルダの中の端では動かさない
    /// （一覧はフォルダごとにまとめて出すので、並びの上の隣が別のフォルダの行だと、入れ替えても見た目が動かない）
    /// </summary>
    public static IReadOnlyList<SearchHistoryEntry> Move(IEnumerable<SearchHistoryEntry> entries, string name, int delta)
    {
        var list = entries.ToList();
        var at = list.FindIndex(other => SameName(other.Name, name));
        if (at < 0 || delta == 0)
        {
            return list;
        }

        var folder = FolderOf(list[at]);
        var step = Math.Sign(delta);
        var to = at + step;
        while (to >= 0 && to < list.Count && !SameFolder(FolderOf(list[to]), folder))
        {
            to += step;
        }

        if (to < 0 || to >= list.Count)
        {
            return list;
        }

        (list[at], list[to]) = (list[to], list[at]);
        return list;
    }

    // ---- フォルダ（1階層・ユーザ判断 2026-11-27） ----
    // フォルダの一覧は持たず、1件ごとの欄（SearchHistoryEntry.Folder）だけで表す。中身の無いフォルダは残らない。
    // 一覧はフォルダの外の物を先に、フォルダは並びに初めて出てきた順で出す（並びは人が決めた順のまま、まとめて見せる）

    /// <summary>行のフォルダの名前。前後の空白を落とし、空ならフォルダの外（null）。手で直した JSON の空白だけの名前も外として読む</summary>
    public static string? FolderOf(SearchHistoryEntry entry)
        => string.IsNullOrWhiteSpace(entry.Folder) ? null : entry.Folder.Trim();

    /// <summary>同じフォルダか（どちらもフォルダの外なら同じ）。名前の比べ方は保存した検索の名前と同じ</summary>
    public static bool SameFolder(string? left, string? right)
        => (left is null && right is null) || (left is not null && right is not null && SameName(left, right));

    /// <summary>フォルダの名前（並びに初めて出てきた順。綴りは最初の行の物）。</summary>
    public static IReadOnlyList<string> Folders(IEnumerable<SearchHistoryEntry> entries)
    {
        var folders = new List<string>();
        foreach (var entry in entries)
        {
            if (FolderOf(entry) is { } folder && !folders.Any(other => SameName(other, folder)))
            {
                folders.Add(folder);
            }
        }

        return folders;
    }

    /// <summary>一覧に出す順：フォルダの外の物を先に、続けてフォルダごと（<see cref="Folders"/> の順）。同じフォルダの中は並びのまま</summary>
    public static IReadOnlyList<SearchHistoryEntry> Grouped(IEnumerable<SearchHistoryEntry> entries)
    {
        var list = entries.ToList();
        var result = list.Where(entry => FolderOf(entry) is null).ToList();
        foreach (var folder in Folders(list))
        {
            result.AddRange(list.Where(entry => SameFolder(FolderOf(entry), folder)));
        }

        return result;
    }

    /// <summary>
    /// フォルダへ移す（<paramref name="folder"/> が空ならフォルダの外へ）。移した行は、移した先の最後の行のすぐ後ろに置く
    /// （置き場所を変えないと、移した行が並びの前の方にあるとき、フォルダの出る順まで変わってしまう）。移した先に行が無ければ末尾。
    /// 今あるフォルダの綴りに寄せる（大文字小文字だけ違うフォルダを2つ作らない）。名前が無ければ（別の所で消された）何もしない
    /// </summary>
    public static IReadOnlyList<SearchHistoryEntry> MoveToFolder(IEnumerable<SearchHistoryEntry> entries, string name, string? folder)
    {
        var list = entries.ToList();
        var at = list.FindIndex(other => SameName(other.Name, name));
        if (at < 0)
        {
            return list;
        }

        var target = string.IsNullOrWhiteSpace(folder) ? null : folder.Trim();
        target = target is null ? null : Folders(list).FirstOrDefault(other => SameName(other, target)) ?? target;
        if (SameFolder(FolderOf(list[at]), target))
        {
            return list;
        }

        var moved = list[at] with { Folder = target };
        list.RemoveAt(at);
        var last = list.FindLastIndex(other => SameFolder(FolderOf(other), target));
        list.Insert(last < 0 ? list.Count : last + 1, moved);
        return list;
    }

    /// <summary>
    /// フォルダの名前を変える（中身をまとめて移す）。新しい名前がほかのフォルダと同じなら、そのフォルダへまとめる
    /// （画面は「既にあります」で止めるが、2つの所から同時に変えられたときに同じ名前のフォルダを2つにしない）。空の名前なら何もしない
    /// </summary>
    public static IReadOnlyList<SearchHistoryEntry> RenameFolder(IEnumerable<SearchHistoryEntry> entries, string folder, string newFolder)
    {
        var list = entries.ToList();
        var trimmed = newFolder.Trim();
        if (trimmed.Length == 0)
        {
            return list;
        }

        var target = Folders(list).FirstOrDefault(other => SameName(other, trimmed) && !SameName(other, folder)) ?? trimmed;
        return list.Select(entry => SameFolder(FolderOf(entry), folder.Trim()) || SameFolder(FolderOf(entry), target)
            ? entry with { Folder = target }
            : entry).ToList();
    }

    /// <summary>フォルダを消す。中身はフォルダの外へ出す（保存した検索は消さない）</summary>
    public static IReadOnlyList<SearchHistoryEntry> RemoveFolder(IEnumerable<SearchHistoryEntry> entries, string folder)
        => entries.Select(entry => SameFolder(FolderOf(entry), folder.Trim()) ? entry with { Folder = null } : entry).ToList();

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

        var avatars = RewriteModules(entries, AvatarKind, module => module with
        {
            Items = module.Items.Select(key => RenamedBaseKey(key, string.Empty, oldName, target)).Distinct(StringComparer.Ordinal).ToList(),
        });

        // 条件「プロファイル」も、変換元（from:）・変換先（to:）の欄の素体名を付いていかせる（2026-11-09。前は対応アバターの条件だけだった）
        return RewriteModules(avatars, ProfileKind, module => module with
        {
            Items = module.Items
                .Select(key => RenamedBaseKey(RenamedBaseKey(key, SearchModuleConditions.SourceKey, oldName, target), SearchModuleConditions.DestinationKey, oldName, target))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
        });
    }

    private const string ProfileKind = "Profile";

    // ---- Unity プロジェクトの場所の付け替えに付いていく（ユーザ判断 2026-11-10） ----
    // 条件「改変プロジェクト」（UnityProject）と「Unityプロジェクト」（UnityProjectContents）は、プロジェクトを場所の文字で指す。
    // フォルダを移した後も古い場所のままだと、呼び出したとき黙って0件になる

    private static readonly IReadOnlySet<string> UnityProjectKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(SearchModuleKind.UnityProject),
        nameof(SearchModuleKind.UnityProjectContents),
    };

    /// <summary>
    /// 条件1つの中の <paramref name="from"/> を <paramref name="to"/> に替える（綴りの揺れは <see cref="UnityProjectPath.Normalize"/> で見る）。
    /// 付け替えた先が既に札にあれば重ねない。Unity プロジェクトの種類でなければ、当たらなければ、同じ物を返す（書き換えたかを参照で見分ける）
    /// </summary>
    public static SearchModuleState RelocateUnityProject(SearchModuleState module, string from, string to)
    {
        if (!UnityProjectKinds.Contains(module.Kind) || !module.Items.Any(key => ModificationService.SamePath(key, from)))
        {
            return module;
        }

        return module with
        {
            Items = module.Items
                .Select(key => ModificationService.SamePath(key, from) ? to : key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    /// <summary>保存した検索の全部に <see cref="RelocateUnityProject(SearchModuleState, string, string)"/> を当てる。並びと名前は触らない。</summary>
    public static IReadOnlyList<SearchHistoryEntry> RelocateUnityProject(
        IEnumerable<SearchHistoryEntry> entries, string from, string to, out int changed)
    {
        var count = 0;
        var result = entries
            .Select(entry =>
            {
                var modules = entry.Modules.Select(module => RelocateUnityProject(module, from, to)).ToList();
                if (modules.SequenceEqual(entry.Modules, ReferenceEqualityComparer.Instance))
                {
                    return entry;
                }

                count++;
                return entry with { Modules = modules };
            })
            .ToList();
        changed = count;
        return result;
    }

    /// <summary>頭（<paramref name="side"/>。対応アバターは空）の後ろが「base:旧名」なら「base:新名」にする。</summary>
    private static string RenamedBaseKey(string key, string side, string oldName, string newName)
        => key.StartsWith(side + BaseKeyPrefix, StringComparison.Ordinal) && SameTag(key[(side.Length + BaseKeyPrefix.Length)..], oldName)
            ? side + BaseKeyPrefix + newName
            : key;
}
