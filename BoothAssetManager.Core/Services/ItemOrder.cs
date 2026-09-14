using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 検索の並べ替えの決まり（技術的負債 4-1・5：検索画面のクラスから、画面に依らない所を切り出して試験を付けた）。
///
/// **値が無い商品は、昇順でも降順でも常に後ろにまとめる。**「値が小さい」のではなく「値が無い」ので、
/// 0 や一番古い日時として混ぜると、昇順にしたときに先頭へ来て誤読させる。後ろにまとめた物は名前順。
/// </summary>
public static class ItemOrder
{
    /// <summary>属性の値で並べる。付けていない商品は後ろ。</summary>
    public static IEnumerable<ItemRecord> ByAttribute(IEnumerable<ItemRecord> items, string attributeName, bool descending)
    {
        var list = items.ToList();
        var rated = list.Where(item => item.Local.Attributes.ContainsKey(attributeName)).ToList();
        var unrated = list.Where(item => !item.Local.Attributes.ContainsKey(attributeName)).OrderBy(item => item.DisplayName, StringComparer.CurrentCulture);

        var ordered = descending
            ? rated.OrderByDescending(item => item.Local.Attributes[attributeName])
            : rated.OrderBy(item => item.Local.Attributes[attributeName]);

        return ordered.Concat(unrated);
    }

    /// <summary>「最近」の足跡の時刻で並べる。足跡が無い商品は後ろ。</summary>
    public static IEnumerable<ItemRecord> ByTime(
        IEnumerable<ItemRecord> items,
        IReadOnlyDictionary<string, DateTimeOffset> times,
        bool descending)
    {
        var list = items.ToList();
        var stamped = list.Where(item => times.ContainsKey(item.Id)).ToList();
        var untouched = list.Where(item => !times.ContainsKey(item.Id)).OrderBy(item => item.DisplayName, StringComparer.CurrentCulture);

        var byTime = descending
            ? stamped.OrderByDescending(item => times[item.Id])
            : stamped.OrderBy(item => times[item.Id]);

        return byTime.Concat(untouched);
    }

    /// <summary>入手日で並べる（既定の並び）。入手日が無い商品は後ろ、同じ日は名前順。</summary>
    public static IEnumerable<ItemRecord> ByAcquired(IEnumerable<ItemRecord> items, bool descending)
        => descending
            ? items.OrderByDescending(item => item.Local.AcquiredAt ?? DateOnly.MinValue)
                .ThenBy(item => item.DisplayName, StringComparer.CurrentCulture)
            : items.OrderBy(item => item.Local.AcquiredAt ?? DateOnly.MaxValue)
                .ThenBy(item => item.DisplayName, StringComparer.CurrentCulture);
}
