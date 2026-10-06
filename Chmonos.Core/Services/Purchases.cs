using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 購入記録の読み方をここに集める。
///
/// 「自分の支出」と「贈答に使った額」を取り違えると集計が静かに狂うので、
/// 各画面で <c>Where(...)</c> を書かず、必ずここを通す。
/// </summary>
public static class Purchases
{
    /// <summary>自分用に買った額。贈答は含めない（別のタイルに出す）。</summary>
    public static long SelfSpendOf(ItemRecord item)
        => item.Local.Purchases
            .Where(purchase => purchase.Kind == PurchaseKind.ForSelf)
            .Sum(purchase => (long)(purchase.Price ?? 0));

    /// <summary>
    /// 自分用に払った額。**額を1つも入れていなければ null**（「0円」ではなく「分からない」。無料は 0）。
    /// 検索の価格の絞り込み・「払った額」の並べ替えが使う。贈った・貰ったは含めない（統計の自分用の支出と同じ）。
    /// </summary>
    public static long? SelfPaidOrNull(ItemRecord item)
    {
        long? total = null;
        foreach (var purchase in item.Local.Purchases)
        {
            if (purchase.Kind == PurchaseKind.ForSelf && purchase.Price is { } price)
            {
                total = (total ?? 0) + price;
            }
        }

        return total;
    }

    /// <summary>人に贈るために払った額。払ってはいるが手元にファイルは来ない。</summary>
    public static long GivenSpendOf(ItemRecord item)
        => item.Local.Purchases
            .Where(purchase => purchase.Kind == PurchaseKind.Given)
            .Sum(purchase => (long)(purchase.Price ?? 0));

    /// <summary>贈った回数。同じ商品を3人に贈れば3。</summary>
    public static int GivenCountOf(ItemRecord item)
        => item.Local.Purchases.Count(purchase => purchase.Kind == PurchaseKind.Given);

    /// <summary>貰った回数。自分は払っていない。</summary>
    public static int ReceivedCountOf(ItemRecord item)
        => item.Local.Purchases.Count(purchase => purchase.Kind == PurchaseKind.Received);

    /// <summary>自分用に0円で手に入れた回数。支出には入るが0なので内訳として別に数える。</summary>
    public static int FreeCountOf(ItemRecord item)
        => item.Local.Purchases.Count(purchase =>
            purchase.Kind == PurchaseKind.ForSelf && purchase.Price == 0);

    /// <summary>有償で自分用に買った記録があるか。無ければ「払ったはずだが記録が無い」側に数える。</summary>
    public static bool HasPricedSelfPurchase(ItemRecord item)
        => item.Local.Purchases.Any(purchase =>
            purchase.Kind == PurchaseKind.ForSelf && purchase.Price is > 0);

    /// <summary>贈った商品か。ファイルが手元に来ないので所持には入らない。</summary>
    public static bool WasGiven(ItemRecord item) => GivenCountOf(item) > 0;

    /// <summary>貰った商品か。</summary>
    public static bool WasReceived(ItemRecord item) => ReceivedCountOf(item) > 0;
}
