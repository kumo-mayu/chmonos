using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Query.Search;

/// <summary>
/// 並べる欄の値。並べ替えの鍵（<see cref="SearchEngine"/>）と、一覧の要約に添える値（<see cref="ItemSummary"/>）の両方がここを呼ぶ。
/// 別々に書くと、カードに出した値と並びが食い違う（入手日は商品の入手日ではなく購入1件の日付のうち最も早い物、など。Web の API-GAPS G8）
/// </summary>
internal static class SortFields
{
    public static DateTimeOffset? PublishedAt(ItemRecord item) => item.Booth.PublishedAt;

    /// <summary>画面の「入手日」の並べと同じ代表の日付（<see cref="PurchaseDates.EnteredEarliest"/>）。</summary>
    public static DateOnly? AcquiredAt(ItemRecord item) => PurchaseDates.EnteredEarliest(item);

    public static int WishCount(ItemRecord item) => item.Booth.WishListsCount;

    /// <summary>自分用に払った額（<see cref="Purchases.SelfPaidOrNull"/>。額を1つも入れていなければ null、無料は 0）。</summary>
    public static long? Paid(ItemRecord item) => Purchases.SelfPaidOrNull(item);

    public static DateTimeOffset? FetchedAt(ItemRecord item) => item.Booth.FetchedAt;
}
