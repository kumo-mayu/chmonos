using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>統計の期間（両端の日を含む）。</summary>
public readonly record struct StatsDateRange(DateOnly From, DateOnly To)
{
    public bool Contains(DateOnly date) => date >= From && date <= To;

    /// <summary>またがる月の数（端の月も1と数える）。グラフを月で並べるか年で並べるかを決める</summary>
    public int MonthSpan => (To.Year - From.Year) * 12 + To.Month - From.Month + 1;
}

/// <summary>購入1件と、その日付（空なら商品の日付で代えた値）。読み込みの時に1回だけ求める。</summary>
public readonly record struct StatsPurchaseDate(Purchase Purchase, DateOnly? Date);

/// <summary>
/// 商品1件の日付の控え。期間を替えるたびにファイルの日付を求め直さないよう、読み込みの時に作って持つ。
/// <see cref="ItemDate"/> は購入記録が無いか自分用・貰った購入が無いときの日付（代える）
/// </summary>
public sealed record StatsDatedItem(ItemRecord Item, IReadOnlyList<StatsPurchaseDate> Purchases, DateOnly? ItemDate);

/// <summary>期間で絞った結果。<see cref="Items"/> を <see cref="StatsService.Build"/> へ通すと期間の集計になる。</summary>
public sealed record StatsNarrowed
{
    /// <summary>期間に手に入れた商品（購入は期間の中の、贈った物以外だけに削った写し）。</summary>
    public required IReadOnlyList<ItemRecord> Items { get; init; }

    /// <summary>期間の中に贈った回数と額（贈った日で数える。所持していない商品も入る）。</summary>
    public required int GivenCount { get; init; }

    public required long GivenSpentYen { get; init; }

    /// <summary>日付が分からず、どの期間にも入らない所持商品の数（全期間でだけ数に入る）。</summary>
    public required int UndatedOwnedCount { get; init; }
}

/// <summary>
/// 統計の期間の決め方と、期間での絞り方（ユーザ判断 2026-11-18・open.md 104）。
///
/// 期間で変わる欄は「期間に何をどこで買ったか」を見る物なので、商品は**手に入れた日**（自分用・貰った購入の日。
/// 購入記録が無ければ商品の日付）で期間に入れる。贈った分は手元に来ないので、贈った日で別に数える。
/// 購入1件の日付の決まりは <see cref="PurchaseDates"/>（統計のグラフ・検索と同じ）。
/// 全期間は「絞らない」：最初と最後の日付の間で切るのではないので、日付の分からない商品も入る
/// </summary>
public static class StatsPeriods
{
    /// <summary>
    /// グラフを月で並べる長さの上限。24ヶ月は前から選べた「直近 24 ヶ月」で、狭い窓でも目盛りが重ならないと確かめた本数。
    /// それより長い期間は年で並べる（ユーザ判断 2026-11-18：並べ方は期間の長さで決め、別に選ばせない）
    /// </summary>
    public const int MaxMonthlyBars = 24;

    public static bool ByMonth(StatsDateRange range) => range.MonthSpan <= MaxMonthlyBars;

    private static DateOnly MonthStart(DateOnly date) => new(date.Year, date.Month, 1);

    private static DateOnly MonthEnd(DateOnly date) => MonthStart(date).AddMonths(1).AddDays(-1);

    /// <summary>
    /// 今月までの <paramref name="months"/> ヶ月（月初から月末まで）。
    /// 入手日が先の日付になっている購入があれば、その月まで伸ばす（前の「直近 n ヶ月」のグラフと同じ。右端に出ないと無かった事に見える）
    /// </summary>
    public static StatsDateRange LastMonths(DateOnly today, int months, DateOnly? latest = null)
    {
        var end = latest is { } last && last > MonthEnd(today) ? MonthEnd(last) : MonthEnd(today);
        return new StatsDateRange(MonthStart(end).AddMonths(1 - months), end);
    }

    public static StatsDateRange ThisMonth(DateOnly today) => new(MonthStart(today), MonthEnd(today));

    public static StatsDateRange LastMonth(DateOnly today) => ThisMonth(MonthStart(today).AddMonths(-1));

    public static StatsDateRange Year(int year) => new(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));

    /// <summary>開始と終了を選んだ期間。逆に選ばれたら入れ替える（どちらを先に選んでも同じ期間になるように）</summary>
    public static StatsDateRange Between(DateOnly a, DateOnly b) => a <= b ? new(a, b) : new(b, a);

    /// <summary>
    /// 期間の見出しの字。年まるごとは「2025年」、月まるごとは「2025年3月」「2024年12月〜2025年11月」、それ以外は日まで
    /// </summary>
    public static string Label(StatsDateRange range)
    {
        var (from, to) = (range.From, range.To);
        if (from.Day == 1 && to == MonthEnd(to))
        {
            if (from.Month == 1 && to.Month == 12 && from.Year == to.Year)
            {
                return $"{from.Year}年";
            }

            return from.Year == to.Year && from.Month == to.Month
                ? $"{from.Year}年{from.Month}月"
                : $"{from.Year}年{from.Month}月〜{to.Year}年{to.Month}月";
        }

        return $"{from:yyyy/MM/dd}〜{to:yyyy/MM/dd}";
    }

    /// <summary>
    /// 商品ごとの日付の控えを作る（重い所。ファイルの日付は要るときだけ求める）。読み込みの時に1回だけ呼ぶ
    /// </summary>
    public static IReadOnlyList<StatsDatedItem> Date(IReadOnlyList<ItemRecord> items)
    {
        var result = new List<StatsDatedItem>(items.Count);
        foreach (var item in items)
        {
            DateOnly? itemDate = null;
            var itemDateKnown = false;
            DateOnly? ItemDate()
            {
                if (!itemDateKnown)
                {
                    itemDate = AcquiredDateResolver.Resolve(item).Value;
                    itemDateKnown = true;
                }

                return itemDate;
            }

            var purchases = new List<StatsPurchaseDate>(item.Local.Purchases.Count);
            foreach (var purchase in item.Local.Purchases)
            {
                purchases.Add(new StatsPurchaseDate(purchase, purchase.PurchasedAt ?? ItemDate()));
            }

            // 自分用・貰った購入が無ければ、手に入れた日は商品の日付（グラフの数え方と同じ）
            var needsItemDate = !purchases.Any(row => row.Purchase.Kind != PurchaseKind.Given);
            result.Add(new StatsDatedItem(item, purchases, needsItemDate ? ItemDate() : itemDate));
        }

        return result;
    }

    /// <summary>
    /// 期間で絞る。手に入れた購入（贈った物以外）が期間に1件でもある商品を、購入をその分だけに削って残す。
    /// 自分用・貰った購入が無い商品は、商品の日付が期間にあれば残す（購入は削らない：贈った購入しか無い）。
    /// 期間に購入が1件も無い商品は商品ごと外す（購入を0件にした写しは「購入記録が無い」に読まれ、商品の日付で数え直されるため）
    /// </summary>
    public static StatsNarrowed Narrow(IReadOnlyList<StatsDatedItem> dated, StatsDateRange range)
    {
        var items = new List<ItemRecord>();
        var givenCount = 0;
        var givenSpent = 0L;
        var undated = 0;

        foreach (var (item, purchases, itemDate) in dated)
        {
            foreach (var row in purchases)
            {
                if (row.Purchase.Kind == PurchaseKind.Given && row.Date is { } given && range.Contains(given))
                {
                    givenCount++;
                    givenSpent += row.Purchase.Price ?? 0;
                }
            }

            var acquired = purchases.Where(row => row.Purchase.Kind != PurchaseKind.Given).ToList();
            if (acquired.Count == 0)
            {
                if (itemDate is { } date)
                {
                    if (range.Contains(date))
                    {
                        // 贈った購入は期間の集計に入れない（上で贈った日で数えた。二重に数えない）
                        items.Add(purchases.Count == 0 ? item : item with { Local = item.Local with { Purchases = [] } });
                    }
                }
                else if (item.IsOwned)
                {
                    undated++;
                }

                continue;
            }

            if (acquired.Any(row => row.Date is null) && item.IsOwned)
            {
                undated++;
            }

            var inRange = acquired.Where(row => row.Date is { } date && range.Contains(date)).Select(row => row.Purchase).ToList();
            if (inRange.Count > 0)
            {
                items.Add(inRange.Count == item.Local.Purchases.Count ? item : item with { Local = item.Local with { Purchases = inRange } });
            }
        }

        return new StatsNarrowed { Items = items, GivenCount = givenCount, GivenSpentYen = givenSpent, UndatedOwnedCount = undated };
    }

    /// <summary>期間の集計。贈った分は <see cref="Narrow"/> が贈った日で数えた値に差し替える</summary>
    public static StatsSnapshot Build(
        StatsNarrowed narrowed, AvatarRegistry registry, int unresolvedCount, bool useDetectedVariationTargets)
        => StatsService.Build(narrowed.Items, registry, unresolvedCount, useDetectedVariationTargets) with
        {
            GivenCount = narrowed.GivenCount,
            GivenSpentYen = narrowed.GivenSpentYen,
        };
}
