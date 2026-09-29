using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 検索の並べ替えの決まり（技術的負債 4-1・5：検索画面のクラスから、画面に依らない所を切り出して試験を付けた）。
///
/// **値が無い商品は、昇順でも降順でも常に後ろにまとめる。**「値が小さい」のではなく「値が無い」ので、
/// 0 や一番古い日時として混ぜると、昇順にしたときに先頭へ来て誤読させる。
/// 同じ値の商品と、後ろにまとめた物は名前の読みの順（<see cref="NameCollation"/>）。
/// ショップとカテゴリだけは、同じショップ・同じカテゴリの中を入手日の新しい順にする
/// （中身は「その店・その種類で最近何を買ったか」を見たい。名前順だと同じ店のシリーズ物が型番順に並ぶだけになる。ユーザ判断 2026-09-24）。
/// </summary>
public static class ItemOrder
{
    /// <summary>
    /// 検索の写し（全件を読み込んだ一覧）の並び：入手日の新しい順、入手日が無い物は後ろ、同じ日は名前順。
    /// 画面に出す並びは絞り込みのたびに選んだ項目で並べ直すので、これは写しの持ち方の決まり。
    /// 読み込みと、未確定で登録した1件をその場で足す所（<see cref="LibraryInsertIndex"/>）が同じ決まりを使うためにここに置く
    /// （ずれると、読み直す前と後で並びが変わる）。
    /// </summary>
    public static IComparer<ItemRecord> Library { get; } = Comparer<ItemRecord>.Create((left, right) =>
    {
        var byDate = (right.Local.AcquiredAt ?? DateOnly.MinValue).CompareTo(left.Local.AcquiredAt ?? DateOnly.MinValue);
        return byDate != 0 ? byDate : StringComparer.CurrentCulture.Compare(left.DisplayName, right.DisplayName);
    });

    /// <summary>検索の写しの並びに並べる（<see cref="Library"/>。同じ順位の物は元の順を保つ）。</summary>
    public static List<ItemRecord> LibraryOrder(IEnumerable<ItemRecord> items)
        => items.OrderBy(item => item, Library).ToList();

    /// <summary>
    /// 写しの並び（<see cref="Library"/>）に並んだ一覧へ1件を足すときの位置。同じ順位の物の後ろ。
    /// 並びを崩さずに足せるので、全件を読み直さずに済む（2000件の読み直しは数秒）。
    /// </summary>
    public static int LibraryInsertIndex(IReadOnlyList<ItemRecord> sorted, ItemRecord item)
    {
        var low = 0;
        var high = sorted.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (Library.Compare(sorted[middle], item) <= 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>名前の読みの順。</summary>
    public static IEnumerable<ItemRecord> ByName(IEnumerable<ItemRecord> items, bool descending, NameCollation? names = null)
    {
        var collation = names ?? NameCollation.Plain;
        return descending
            ? items.OrderByDescending(item => collation.SortKeyOf(item.DisplayName))
            : items.OrderBy(item => collation.SortKeyOf(item.DisplayName));
    }

    /// <summary>属性の値で並べる。付けていない商品は後ろ。</summary>
    public static IEnumerable<ItemRecord> ByAttribute(IEnumerable<ItemRecord> items, string attributeName, bool descending, NameCollation? names = null)
        => ByValue(items, item => item.Local.Attributes.TryGetValue(attributeName, out var value) ? value : (int?)null, descending, names);

    /// <summary>「最近」の足跡の時刻で並べる。足跡が無い商品は後ろ。</summary>
    public static IEnumerable<ItemRecord> ByTime(
        IEnumerable<ItemRecord> items,
        IReadOnlyDictionary<string, DateTimeOffset> times,
        bool descending,
        NameCollation? names = null)
        => ByValue(items, item => times.TryGetValue(item.Id, out var time) ? time : (DateTimeOffset?)null, descending, names);

    /// <summary>入手日で並べる（既定の並び）。入手日が無い商品は後ろ。</summary>
    public static IEnumerable<ItemRecord> ByAcquired(IEnumerable<ItemRecord> items, bool descending, NameCollation? names = null)
        => ByValue(items, item => item.Local.AcquiredAt, descending, names);

    /// <summary>
    /// 容量で並べる。**ファイルを持っていない商品は後ろ**（カードで「未取得」と出る物。0バイトとして混ぜると、小さい順で先頭に来た）。
    /// </summary>
    public static IEnumerable<ItemRecord> BySize(IEnumerable<ItemRecord> items, bool descending, NameCollation? names = null)
        => ByValue(items, item => item.IsDownloaded ? item.LogicalSizeBytes : (long?)null, descending, names);

    /// <summary>
    /// スキ数で並べる。**BOOTH から一度も取れていない商品は後ろ**（BOOTH に無い商品のスキ数は 0 ではなく「無い」。少ない順で先頭に来ていた）。
    /// </summary>
    public static IEnumerable<ItemRecord> ByWishList(IEnumerable<ItemRecord> items, bool descending, NameCollation? names = null)
        => ByValue(items, item => item.Booth.WasEverFetched ? item.Booth.WishListsCount : (int?)null, descending, names);

    /// <summary>自分用に払った額の合計で並べる（<see cref="Purchases.SelfPaidOrNull"/>）。額を入れていない商品は後ろ、無料は 0。</summary>
    public static IEnumerable<ItemRecord> BySelfPaid(IEnumerable<ItemRecord> items, bool descending, NameCollation? names = null)
        => ByValue(items, Purchases.SelfPaidOrNull, descending, names);

    /// <summary>BOOTH の公開日で並べる。公開日が取れていない商品は後ろ。</summary>
    public static IEnumerable<ItemRecord> ByPublished(IEnumerable<ItemRecord> items, bool descending, NameCollation? names = null)
        => ByValue(items, item => item.Booth.PublishedAt, descending, names);

    /// <summary>
    /// BOOTH の今の価格（いちばん安いバリエーション）で並べる。バリエーションが取れていない商品は後ろ。
    /// いちばん安い物にするのは、商品ページの「¥500〜」と同じ読み方にするため。
    /// </summary>
    public static IEnumerable<ItemRecord> ByBoothPrice(IEnumerable<ItemRecord> items, bool descending, NameCollation? names = null)
        => ByValue(items, item => item.Booth.Variations.Count > 0 ? item.Booth.Variations.Min(variation => variation.Price) : (int?)null, descending, names);

    /// <summary>
    /// ショップ名の読みの順。同じショップの中は入手日の新しい順。ショップの無い商品は後ろ（その中も入手日の新しい順）。
    /// </summary>
    public static IEnumerable<ItemRecord> ByShop(IEnumerable<ItemRecord> items, bool descending, NameCollation? names = null)
    {
        var collation = names ?? NameCollation.Plain;
        var list = items.ToList();
        var shopped = list.Where(item => item.ShopName is { Length: > 0 });
        var none = list.Where(item => item.ShopName is not { Length: > 0 });

        var byShop = descending
            ? shopped.OrderByDescending(item => collation.SortKeyOf(item.ShopName!))
            : shopped.OrderBy(item => collation.SortKeyOf(item.ShopName!));

        return NewestFirst(byShop, collation).Concat(NewestFirst(none.OrderBy(_ => 0), collation));
    }

    /// <summary>
    /// カテゴリの表の順（<see cref="CategoryTable.RankOf"/>：3Dモデルの子を先に、その後は BOOTH の表の並び）。逆順もできる。
    /// 同じカテゴリの中は入手日の新しい順。表に無いカテゴリ（その名前の読みの順）→ カテゴリの無い商品、の順に後ろへ置く。
    /// BOOTH に無い商品で人が入れたカテゴリも、同じ表で引く（<see cref="ItemRecord.CategoryName"/> は人が入れた方を先に見る）。
    /// </summary>
    public static IEnumerable<ItemRecord> ByCategory(IEnumerable<ItemRecord> items, CategoryTable table, bool descending, NameCollation? names = null)
    {
        var collation = names ?? NameCollation.Plain;
        var list = items.Select(item => (Item: item, Name: item.CategoryName?.Trim(), Rank: table.RankOf(item.CategoryName))).ToList();

        var ranked = list.Where(entry => entry.Rank is not null);
        var byRank = descending
            ? ranked.OrderByDescending(entry => entry.Rank)
            : ranked.OrderBy(entry => entry.Rank);

        // 表に無いカテゴリは向きによらず後ろ。カテゴリごとにまとめ、名前の読みの順に並べる
        var unknown = list
            .Where(entry => entry.Rank is null && entry.Name is { Length: > 0 })
            .OrderBy(entry => collation.SortKeyOf(entry.Name!));

        var none = list.Where(entry => entry.Rank is null && entry.Name is not { Length: > 0 }).OrderBy(_ => 0);

        return NewestFirst(byRank, collation)
            .Concat(NewestFirst(unknown, collation))
            .Concat(NewestFirst(none, collation))
            .Select(entry => entry.Item);
    }

    private static IEnumerable<(ItemRecord Item, string? Name, int? Rank)> NewestFirst(
        IOrderedEnumerable<(ItemRecord Item, string? Name, int? Rank)> ordered,
        NameCollation collation)
        => ordered
            .ThenByDescending(entry => entry.Item.Local.AcquiredAt ?? DateOnly.MinValue)
            .ThenBy(entry => collation.SortKeyOf(entry.Item.DisplayName));

    /// <summary>同じ値の中を入手日の新しい順（入手日の無い物はその後ろ）、さらに同じなら名前の読みの順。</summary>
    private static IEnumerable<ItemRecord> NewestFirst(IOrderedEnumerable<ItemRecord> ordered, NameCollation collation)
        => ordered
            .ThenByDescending(item => item.Local.AcquiredAt ?? DateOnly.MinValue)
            .ThenBy(item => collation.SortKeyOf(item.DisplayName));

    /// <summary>値で並べ、値の無い商品を向きによらず後ろにまとめる。同じ値と後ろの物は名前の読みの順。</summary>
    private static IEnumerable<ItemRecord> ByValue<T>(
        IEnumerable<ItemRecord> items,
        Func<ItemRecord, T?> valueOf,
        bool descending,
        NameCollation? names)
        where T : struct, IComparable<T>
    {
        var collation = names ?? NameCollation.Plain;
        var valued = new List<(ItemRecord Item, T Value)>();
        var missing = new List<ItemRecord>();

        foreach (var item in items)
        {
            if (valueOf(item) is { } value)
            {
                valued.Add((item, value));
            }
            else
            {
                missing.Add(item);
            }
        }

        var ordered = descending
            ? valued.OrderByDescending(entry => entry.Value)
            : valued.OrderBy(entry => entry.Value);

        return ordered
            .ThenBy(entry => collation.SortKeyOf(entry.Item.DisplayName))
            .Select(entry => entry.Item)
            .Concat(missing.OrderBy(item => collation.SortKeyOf(item.DisplayName)));
    }
}
