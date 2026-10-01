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
/// 文字列と、非表示・R-18 の設定（どの条件の件数にも掛かる物）は条件より先に照らす。文字列で外れる商品を先に落とせる。
/// </summary>
public sealed class SearchFilterPass
{
    private readonly List<ItemRecord> _matches;
    private readonly List<ItemRecord>[] _onlyFailed;
    private readonly List<SearchModule> _active;

    private SearchFilterPass(List<ItemRecord> matches, List<ItemRecord>[] onlyFailed, List<SearchModule> active)
    {
        _matches = matches;
        _onlyFailed = onlyFailed;
        _active = active;
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

        var active = all.Where(module => module.IsActive).ToList();
        var matches = new List<ItemRecord>();
        var onlyFailed = active.Select(_ => new List<ItemRecord>()).ToArray();

        foreach (var item in items)
        {
            if (!passesFirst(item))
            {
                continue;
            }

            var failed = -1;
            var twice = false;
            for (var index = 0; index < active.Count; index++)
            {
                if (active[index].Passes(item, context))
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

        return new SearchFilterPass(matches, onlyFailed, active);
    }

    /// <summary>
    /// この条件を除いた他の全部に当たった商品（選択肢の件数を数える相手）。効いていない条件なら結果そのもの。
    /// 並びは数えるだけなので問わない。
    /// </summary>
    public IReadOnlyList<ItemRecord> OthersFor(SearchModule module)
    {
        var index = _active.IndexOf(module);
        if (index < 0 || _onlyFailed[index].Count == 0)
        {
            return _matches;
        }

        var others = new List<ItemRecord>(_matches.Count + _onlyFailed[index].Count);
        others.AddRange(_matches);
        others.AddRange(_onlyFailed[index]);
        return others;
    }
}
