using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 購入記録ごとの日付の決まり（メモ45・ユーザ判断 2026-10-05）。統計・検索・ショップの場面はここだけを呼ぶ。
///
/// - 購入1件の日付 ＝ <see cref="Purchase.PurchasedAt"/> があればそれ、無ければ**商品の日付**。
/// - 商品の日付は今までの2通りのまま：ファイルの日付で代える場面（<c>Resolve…</c>）と、手で入れた値だけを見る場面（<c>Entered…</c>）。
/// - 商品を1つの日付で並べる場面の代表 ＝ **購入1件の日付のうち最も早い物**（購入記録が無ければ商品の日付）。
///   入手日は「最初に手に入れた日」の意味で使ってきた（ファイルの日付も古い方を採る）ので、それに揃える（1-A）。
///
/// 場面ごとに書くと、統計の月と検索の月が食い違う（同じ商品が統計では3月、検索では1月に出る）。
/// </summary>
public static class PurchaseDates
{
    /// <summary>購入1件の日付。商品の日付は場面ごとに渡す（代える／代えない）。</summary>
    public static DateOnly? Of(Purchase purchase, DateOnly? itemDate) => purchase.PurchasedAt ?? itemDate;

    // ---- 手で入れた値だけを見る場面（検索の並べ替え・区切りの札・条件「入手日」） ----

    /// <summary>
    /// 代表の日付（代えない）。購入1件の日付のうち最も早い物、購入記録が無ければ商品の入手日。どれも無ければ null。
    /// 検索は全件を照らすので、並べのたびに一覧を作らない書き方にしてある。
    /// </summary>
    public static DateOnly? EnteredEarliest(ItemRecord item)
    {
        var purchases = item.Local.Purchases;
        var itemDate = item.Local.AcquiredAt;
        if (purchases.Count == 0)
        {
            return itemDate;
        }

        DateOnly? earliest = null;
        foreach (var purchase in purchases)
        {
            if (Of(purchase, itemDate) is { } date && (earliest is null || date < earliest))
            {
                earliest = date;
            }
        }

        return earliest;
    }

    /// <summary>
    /// どれか1件の日付（代えない）が当てはまるか（検索の条件「入手日」・2-A）。
    /// 2025年3月に別の種類を買い足した商品が「2025年3月」で出るように、代表の日付1つでは見ない。
    /// </summary>
    public static bool AnyEntered(ItemRecord item, Func<DateOnly, bool> predicate)
    {
        var purchases = item.Local.Purchases;
        var itemDate = item.Local.AcquiredAt;
        if (purchases.Count == 0)
        {
            return itemDate is { } only && predicate(only);
        }

        foreach (var purchase in purchases)
        {
            if (Of(purchase, itemDate) is { } date && predicate(date))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>手で入れた日付が1つでもあるか（代えない）。無い商品は「日付の分からない商品」。</summary>
    public static bool HasEntered(ItemRecord item) => AnyEntered(item, _ => true);

    /// <summary>手で入れた日付の全部（代えない）。検索の日付の条件を足したときの両端に使う。</summary>
    public static IEnumerable<DateOnly> AllEntered(ItemRecord item)
    {
        var itemDate = item.Local.AcquiredAt;
        if (item.Local.Purchases.Count == 0)
        {
            if (itemDate is { } only)
            {
                yield return only;
            }

            yield break;
        }

        foreach (var purchase in item.Local.Purchases)
        {
            if (Of(purchase, itemDate) is { } date)
            {
                yield return date;
            }
        }
    }

    // ---- 空ならファイルの日付で代える場面（統計・ショップ） ----

    /// <summary>
    /// 購入1件ずつの日付（代える）。購入記録が無ければ、購入を持たない1件（<c>Purchase = null</c>）として商品の日付を返す。
    /// <paramref name="which"/> で場面が見ない購入を外す（外して1件も残らなければ、購入記録が無いのと同じ扱い）。
    /// 商品の日付はファイルを見に行くので、要る（日付の無い購入がある）ときだけ1回求める。
    /// </summary>
    public static IReadOnlyList<(Purchase? Purchase, AcquiredDate Date)> Resolve(ItemRecord item, Func<Purchase, bool>? which = null)
    {
        AcquiredDate? itemDate = null;
        AcquiredDate ItemDate() => itemDate ??= AcquiredDateResolver.Resolve(item);

        var result = new List<(Purchase?, AcquiredDate)>();
        foreach (var purchase in item.Local.Purchases)
        {
            if (which is not null && !which(purchase))
            {
                continue;
            }

            result.Add((purchase, purchase.PurchasedAt is { } date ? new AcquiredDate(date, false) : ItemDate()));
        }

        if (result.Count == 0)
        {
            result.Add((null, ItemDate()));
        }

        return result;
    }

    /// <summary>代表の日付（代える）。最も早い物。同じ日なら手で入れた方を採る（ファイルの日付と断らずに済む）。</summary>
    public static AcquiredDate ResolveEarliest(ItemRecord item)
        => Pick(Resolve(item), earliest: true);

    /// <summary>最も遅い購入の日付（代える）。ショップの「最後に買った日」。</summary>
    public static AcquiredDate ResolveLatest(ItemRecord item)
        => Pick(Resolve(item), earliest: false);

    private static AcquiredDate Pick(IReadOnlyList<(Purchase? Purchase, AcquiredDate Date)> dates, bool earliest)
    {
        AcquiredDate picked = default;
        foreach (var (_, date) in dates)
        {
            if (date.Value is not { } value)
            {
                continue;
            }

            if (picked.Value is not { } current
                || (earliest ? value < current : value > current)
                || (value == current && picked.IsFallback && !date.IsFallback))
            {
                picked = date;
            }
        }

        return picked;
    }
}
