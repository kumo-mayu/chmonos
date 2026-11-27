using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>条件「保存した検索」が当てられない理由。</summary>
public enum SavedSearchProblem
{
    None,

    /// <summary>名前の保存した検索が無い（消した・手で直した JSON）。</summary>
    Missing,

    /// <summary>入れ子をたどると自分に戻る（手で直した JSON。画面からは保存の時に断る）。</summary>
    Cycle,
}

/// <summary>
/// 条件「保存した検索」（ユーザ判断 2026-11-27・`docs/spec/search-saved.md`「条件にする・フォルダ」）：その保存した検索に当たる商品。
///
/// **今の中身で当てる**（足した時の中身で固めない）。使うのは打った文字・条件・探す対象と区別・別表記だけで、表示順・カードかリストかは絞り込みではないので使わない。
/// 元が無い・輪になっているときは何にも当たらない（除くと全部が通る。消したタグを条件に残したときと同じ・ユーザ判断 2026-11-27 ②a）。
/// 中身を組むのは材料（<see cref="SearchModuleContext"/>）1つにつき名前ごとに1回で、同じ数え直しの中の入れ子・複数の条件で使い回す
/// </summary>
public sealed class SavedSearchCondition(string name, bool useText) : ItemCondition
{
    private SavedSearchMatcher? _matcher;

    public string Name => name;

    /// <summary>名前を選んでいれば絞る（元が無くても絞る：何にも当たらない条件として効かせる）。</summary>
    public override bool IsActive => name.Trim().Length > 0;

    public SavedSearchProblem Problem => _matcher?.Problem ?? SavedSearchProblem.None;

    public override void Prepare(SearchModuleContext context) => _matcher = SavedSearchMatcher.For(context, name);

    public override bool Matches(ItemRecord item, SearchModuleContext context)
        => (_matcher ??= SavedSearchMatcher.For(context, name)).Matches(item, context, useText);
}

/// <summary>保存した検索1件を組んだ物（文字の照らし方と、条件の照らす単位）。材料の中に名前ごとに覚える。</summary>
public sealed class SavedSearchMatcher
{
    private static readonly SavedSearchMatcher MissingMatcher = new(SavedSearchProblem.Missing);
    private static readonly SavedSearchMatcher CycleMatcher = new(SavedSearchProblem.Cycle);

    private readonly Services.SearchNode? _text;
    private readonly SearchOptions _options = SearchOptions.Default;
    private readonly List<List<(ItemCondition Condition, bool Excluded)>> _units = [];

    private SavedSearchMatcher(SavedSearchProblem problem) => Problem = problem;

    private SavedSearchMatcher(SearchHistoryEntry entry, SearchModuleContext context)
    {
        if (entry.Text.Trim().Length > 0)
        {
            var node = SearchQuery.Parse(entry.Text);

            // 別表記は、索引が組めていれば広げる（組むのは画面の裏の作業に任せ、ここでは組まない・ユーザ判断 2026-11-27 ①a）
            _text = entry.SearchAlternates && context.WidenText is { } widen ? widen(node) : node;
            var targets = entry.Targets
                .Select(target => SearchQuery.FieldNames.TryGetValue(target, out var field) ? field : (SearchField?)null)
                .OfType<SearchField>()
                .ToHashSet();
            _options = new SearchOptions
            {
                Targets = targets.Count > 0 ? targets : SearchOptions.DefaultTargets,
                CaseSensitive = entry.CaseSensitive,
                WidthSensitive = entry.WidthSensitive,
                KanaSensitive = !entry.KanaInsensitive,
            };
        }

        // 画面の絞り込みの1回（App の SearchFilterPass）と同じ単位：範囲・日付の同じ種類は1つのまとまりにし、除かない物のどれかに当たり、除く物を全部通る物
        var byKind = new Dictionary<SearchModuleKind, List<(ItemCondition, bool)>>();
        foreach (var state in entry.Modules)
        {
            if (state is null || !state.Enabled || !Enum.TryParse<SearchModuleKind>(state.Kind, out var kind) || !Enum.IsDefined(kind))
            {
                continue;
            }

            var condition = SearchModuleConditions.From(state);
            if (condition is null)
            {
                continue;
            }

            condition.Prepare(context);
            if (!condition.IsActive)
            {
                continue;
            }

            var member = (condition, SearchModuleConditions.SupportsExclude(kind) && state.Exclude);
            if (SearchModuleConditions.OrSameKind(kind) && byKind.TryGetValue(kind, out var unit))
            {
                unit.Add(member);
                continue;
            }

            unit = [member];
            if (SearchModuleConditions.OrSameKind(kind))
            {
                byKind[kind] = unit;
            }

            _units.Add(unit);
        }
    }

    public SavedSearchProblem Problem { get; }

    /// <summary>
    /// 材料の中の覚えから引き、無ければ組む。輪の中の保存した検索は組まずに「輪」とする（組むと入れ子をたどり続ける）。
    /// 輪の判定は並び全体を1回見て決め、同じ材料の中で覚える
    /// </summary>
    public static SavedSearchMatcher For(SearchModuleContext context, string name)
    {
        var cache = context.SavedSearchMatchers;
        var key = name.Trim();
        if (cache.TryGetValue(key, out var found))
        {
            return found;
        }

        var entry = SavedSearches.Find(context.SavedSearches, key);
        SavedSearchMatcher matcher;
        if (entry is null)
        {
            matcher = MissingMatcher;
        }
        else if ((context.SavedSearchCycles ??= SavedSearchReferences.CycleNames(context.SavedSearches)).Contains(key))
        {
            matcher = CycleMatcher;
        }
        else
        {
            matcher = new SavedSearchMatcher(entry, context);
        }

        cache[key] = matcher;
        return matcher;
    }

    public bool Matches(ItemRecord item, SearchModuleContext context, bool useText)
    {
        if (Problem != SavedSearchProblem.None)
        {
            return false;
        }

        // 検索用の文字列が無い商品は、画面の検索と同じく文字では落とさない
        if (useText && _text is not null && context.Haystack?.Invoke(item) is { } haystack
            && !SearchQuery.Matches(_text, haystack, _options, context.Facts))
        {
            return false;
        }

        foreach (var unit in _units)
        {
            if (!Passes(unit, item, context))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Passes(List<(ItemCondition Condition, bool Excluded)> unit, ItemRecord item, SearchModuleContext context)
    {
        if (unit.Count == 1)
        {
            var (condition, excluded) = unit[0];
            return excluded ? condition.MatchesExcluded(item, context) : condition.Matches(item, context);
        }

        var anyIncluded = false;
        var included = false;
        foreach (var (condition, excluded) in unit)
        {
            if (excluded)
            {
                if (!condition.MatchesExcluded(item, context))
                {
                    return false;
                }

                continue;
            }

            anyIncluded = true;
            included = included || condition.Matches(item, context);
        }

        return !anyIncluded || included;
    }
}

/// <summary>保存した検索どうしのつながり（条件「保存した検索」で指す名前）。輪の判定と、名前の変更に付いていく書き換え。</summary>
public static class SavedSearchReferences
{
    public const string Kind = nameof(SearchModuleKind.SavedSearch);

    /// <summary>この保存した検索が条件で指す名前（効かせていない条件も含める：効かせ直すと輪になるため）。</summary>
    public static IEnumerable<string> Of(SearchHistoryEntry entry)
        => entry.Modules
            .Where(module => module is not null && module.Kind == Kind)
            .Select(module => module.Items.FirstOrDefault()?.Trim() ?? string.Empty)
            .Where(name => name.Length > 0);

    /// <summary>
    /// 入れ子をたどると自分に戻る保存した検索の名前（綴りは並びの物）。比べ方は <see cref="SavedSearches.SameName"/>。
    /// 輪に入らず、輪の物を指しているだけの物は含めない（それは中の条件が当たらないだけ）
    /// </summary>
    public static IReadOnlySet<string> CycleNames(IReadOnlyList<SearchHistoryEntry> entries)
    {
        var result = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry?.Name is { } name && Reaches(entries, name.Trim(), name.Trim()))
            {
                result.Add(name.Trim());
            }
        }

        return result;
    }

    /// <summary>この名前が輪の中にあるか（保存・上書きを断る判定）。</summary>
    public static bool InCycle(IReadOnlyList<SearchHistoryEntry> entries, string name) => Reaches(entries, name.Trim(), name.Trim());

    private static bool Reaches(IReadOnlyList<SearchHistoryEntry> entries, string from, string target)
    {
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var stack = new Stack<string>([from]);
        while (stack.Count > 0)
        {
            var current = SavedSearches.Find(entries, stack.Pop());
            if (current is null)
            {
                continue;
            }

            foreach (var next in Of(current))
            {
                if (SavedSearches.SameName(next, target))
                {
                    return true;
                }

                if (seen.Add(next))
                {
                    stack.Push(next);
                }
            }
        }

        return false;
    }

    /// <summary>当てられない理由（画面の条件の欄に出す）。材料を作らずに、並びだけから決める</summary>
    public static SavedSearchProblem ProblemOf(IReadOnlyList<SearchHistoryEntry> entries, string name)
        => name.Trim().Length == 0 ? SavedSearchProblem.None
            : SavedSearches.Find(entries, name) is null ? SavedSearchProblem.Missing
            : InCycle(entries, name) ? SavedSearchProblem.Cycle
            : SavedSearchProblem.None;

    /// <summary>条件1つの指す名前を替える（名前の変更に付いていく）。当たらなければ同じ物を返す</summary>
    public static SearchModuleState Rename(SearchModuleState module, string oldName, string newName)
        => module.Kind == Kind && module.Items.FirstOrDefault() is { } current && SavedSearches.SameName(current, oldName)
            ? module with { Items = [newName.Trim()] }
            : module;
}
