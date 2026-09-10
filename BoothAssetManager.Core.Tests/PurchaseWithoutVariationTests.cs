using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// どのバリエーションか分からない購入を記録できること。
///
/// BOOTHから取れない商品にはバリエーションが1件も無いので、これが無いと
/// 買った金額を記録する場所が存在しない（統計の支出から丸ごと落ちる）。
/// バリエーション単位の販売終了でも同じことが起きる。
/// </summary>
public class PurchaseWithoutVariationTests
{
    private static ItemRecord ItemWith(params Purchase[] purchases) => new()
    {
        Id = "111",
        Booth = new BoothBlock { Name = "テスト商品" },
        Local = new LocalBlock { Purchases = purchases },
    };

    [Fact]
    public void CountsSpendingWithoutAVariation()
    {
        var item = ItemWith(new Purchase { Price = 3000, Kind = PurchaseKind.ForSelf });

        Assert.Equal(3000, Purchases.SelfSpendOf(item));
    }

    /// <summary>自分用とギフトは、どちらもバリエーションを指していなくても区別される。</summary>
    [Fact]
    public void SeparatesGiftsFromOwnPurchases()
    {
        var item = ItemWith(
            new Purchase { Price = 3000, Kind = PurchaseKind.ForSelf },
            new Purchase { Price = 3000, Kind = PurchaseKind.Given, Note = "友人へ" },
            new Purchase { Price = 3000, Kind = PurchaseKind.Received });

        Assert.Equal(3000, Purchases.SelfSpendOf(item));
        Assert.Equal(3000, Purchases.GivenSpendOf(item));
        Assert.Equal(1, Purchases.GivenCountOf(item));
        Assert.Equal(1, Purchases.ReceivedCountOf(item));
    }

    /// <summary>同じものを2回買った記録も持てる（版の行が無くても）。</summary>
    [Fact]
    public void KeepsTwoPurchasesOfTheSameThing()
    {
        var item = ItemWith(
            new Purchase { Price = 500, Kind = PurchaseKind.ForSelf },
            new Purchase { Price = 500, Kind = PurchaseKind.ForSelf });

        Assert.Equal(1000, Purchases.SelfSpendOf(item));
    }

    /// <summary>
    /// 指していない記録は照合しない。指す先が無いものが「消えた」ことにはならないので、
    /// BOOTHのバリエーション一覧が何であれ現存扱いのまま。
    /// </summary>
    [Fact]
    public void DoesNotMarkAnUnlinkedPurchaseAsGone()
    {
        var purchases = new[] { new Purchase { Price = 3000 } };

        var reconciled = Purchase.Reconcile(purchases, []);

        Assert.True(reconciled[0].ExistsOnBooth);
    }

    /// <summary>バリエーションを指している記録は、これまで通り照合される。</summary>
    [Fact]
    public void StillMarksALinkedPurchaseAsGone()
    {
        var purchases = new[] { new Purchase { VariationId = 7, Price = 3000 } };

        var reconciled = Purchase.Reconcile(purchases, []);

        Assert.False(reconciled[0].ExistsOnBooth);
    }
}
