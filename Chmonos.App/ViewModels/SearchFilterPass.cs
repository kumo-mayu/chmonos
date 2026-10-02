using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 絞り込みの1回：全商品を1回なめて、結果と、選択肢の件数を数える材料を同時に作る（照らす重さの案b・
/// `docs/research/search-modules-2026-10-01.md` §9-4）。
///
/// 選択肢の件数は「その条件を除いた他の全部」を当てた後の商品で数える。前は条件ごとに全商品を照らし直していて、
/// 条件 M 個なら全商品×M 回（中で M−1 個ずつ照らす）になり、条件の数の2乗で伸びた（作り物の1万件・条件20で 63ms）。
/// ここでは商品ごとに条件を上から照らし、**外れた条件を2つ目まで数える**。外れの無い商品はどの条件の「他の全部」にも入り、
/// 外れが1つの商品はその条件の「他の全部」にだけ入り、2つ外れた商品はどこにも入らない（そこで打ち切る）。
/// 数える相手が今の作りと同じになることは試験で確かめてある（`SearchFilterPassTests`）。
///
/// **照らす単位は「まとまり」**（2026-10-02・メモ2-②）：ふつうの条件は1つで1まとまり。範囲・日付の同じ種類
/// （<see cref="SearchModuleInfo.OrSameKind"/>）は1つのまとまりにし、除かない物の**どれかに当てはまり**、除く物の**どれにも当てはまらない**物を通す
/// （除かない物が無ければ、除く物だけで決める）。外れを数えるのもまとまりの単位で、まとまりの中の条件の件数の相手は「このまとまりを除いた他の全部」。
///
/// 文字列と、非表示・R-18 の設定（どの条件の件数にも掛かる物）は条件より先に照らす。文字列で外れる商品を先に落とせる。
/// </summary>
public sealed class SearchFilterPass
{
    private readonly List<ItemRecord> _matches;
    private readonly List<ItemRecord>[] _onlyFailed;
    private readonly Dictionary<SearchModule, int> _unitOf;

    private SearchFilterPass(List<ItemRecord> matches, List<ItemRecord>[] onlyFailed, Dictionary<SearchModule, int> unitOf)
    {
        _matches = matches;
        _onlyFailed = onlyFailed;
        _unitOf = unitOf;
    }

    /// <summary>全部の条件に当たった商品（元の並びのまま）。</summary>
    public IReadOnlyList<ItemRecord> Matches => _matches;

    /// <param name="modules">パネルの条件の全部（効いていない物も。準備は全部にする——件数を数えるのは効いていない条件も同じ）。</param>
    /// <param name="passesFirst">条件より先に照らす物（非表示・R-18 の設定・文字列）。外れた商品はどの件数にも入らない。</param>
    public static SearchFilterPass Run(
        IEnumerable<ItemRecord> items,
        IEnumerable<SearchModule> modules,
        Func<ItemRecord, bool> passesFirst,
        SearchModuleContext context)
    {
        var all = modules.ToList();
        foreach (var module in all)
        {
            module.Prepare(context);
        }

        var units = Units(all.Where(module => module.IsActive));
        var unitOf = new Dictionary<SearchModule, int>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < units.Count; index++)
        {
            foreach (var member in units[index].Members)
            {
                unitOf[member] = index;
            }
        }

        var matches = new List<ItemRecord>();
        var onlyFailed = units.Select(_ => new List<ItemRecord>()).ToArray();

        foreach (var item in items)
        {
            if (!passesFirst(item))
            {
                continue;
            }

            var failed = -1;
            var twice = false;
            for (var index = 0; index < units.Count; index++)
            {
                if (units[index].Passes(item, context))
                {
                    continue;
                }

                if (failed >= 0)
                {
                    twice = true;
                    break;
                }

                failed = index;
            }

            if (twice)
            {
                continue;
            }

            if (failed < 0)
            {
                matches.Add(item);
            }
            else
            {
                onlyFailed[failed].Add(item);
            }
        }

        return new SearchFilterPass(matches, onlyFailed, unitOf);
    }

    /// <summary>
    /// 効いている条件を照らす単位に分ける。並びは画面の並び（まとまりは最初の1つの位置）。
    /// 和集合でつなぐ種類は、離れて置いてあっても同じまとまり（並べ替えで結果が変わらないように）。
    /// </summary>
    internal static List<Unit> Units(IEnumerable<SearchModule> active)
    {
        var units = new List<Unit>();
        var byKind = new Dictionary<SearchModuleKind, Unit>();
        foreach (var module in active)
        {
            if (!module.Info.OrSameKind)
            {
                units.Add(new Unit([module]));
                continue;
            }

            if (byKind.TryGetValue(module.Kind, out var unit))
            {
                unit.Members.Add(module);
                continue;
            }

            unit = new Unit([module]);
            byKind[module.Kind] = unit;
            units.Add(unit);
        }

        return units;
    }

    /// <summary>
    /// この条件を除いた他の全部に当たった商品（選択肢の件数を数える相手）。効いていない条件なら結果そのもの。
    /// 和集合のまとまりの中の条件は、まとまりごと除いた他の全部。並びは数えるだけなので問わない。
    /// </summary>
    public IReadOnlyList<ItemRecord> OthersFor(SearchModule module)
    {
        if (!_unitOf.TryGetValue(module, out var index) || _onlyFailed[index].Count == 0)
        {
            return _matches;
        }

        var others = new List<ItemRecord>(_matches.Count + _onlyFailed[index].Count);
        others.AddRange(_matches);
        others.AddRange(_onlyFailed[index]);
        return others;
    }

    /// <summary>照らす単位（条件1つ、または和集合でつなぐ同じ種類の条件）。</summary>
    internal sealed class Unit(List<SearchModule> members)
    {
        public List<SearchModule> Members { get; } = members;

        /// <summary>
        /// 1つならその条件のまま。2つ以上なら、除かない物のどれかに当たり（除かない物が無ければ問わない）、除く物を全部通る物
        /// （除く物は <see cref="SearchModule.Passes"/> が「当てはまらず、値も分かる」を返す）。
        /// </summary>
        public bool Passes(ItemRecord item, SearchModuleContext context)
        {
            if (Members.Count == 1)
            {
                return Members[0].Passes(item, context);
            }

            var anyIncluded = false;
            var included = false;
            foreach (var member in Members)
            {
                if (member.IsExcluded)
                {
                    if (!member.Passes(item, context))
                    {
                        return false;
                    }

                    continue;
                }

                anyIncluded = true;
                included = included || member.Passes(item, context);
            }

            return !anyIncluded || included;
        }
    }
}
