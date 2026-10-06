using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 払った額の合計が 32bit（2,147,483,647）を超えても、例外にも負の額にもならないこと（外部の点検 2026-10-06）。
/// 前は <c>int</c> の <c>Sum</c> が OverflowException を投げ（統計の画面が開けない）、
/// 自分で足す <see cref="Purchases.SelfPaidOrNull"/> は黙って負の額に回り込んでいた（カード・絞り込み・並べ替えが狂う）。
/// 1件の額は int のまま（欄に打てるのは int まで）。合計は最初の加算から long で持つ。
/// </summary>
public sealed class PurchaseTotalOverflowTests
{
    private static ItemRecord ItemWith(string id, PurchaseKind kind, params int[] prices) => new()
    {
        Id = id,
        Booth = new BoothBlock { Name = "作り物 " + id },
        Local = new LocalBlock
        {
            LocalFiles = [new LocalFileRecord { Hash = "H" + id, Paths = [$@"D:\{id}.zip"], SizeBytes = 3 }],
            Purchases = [.. prices.Select(price => new Purchase { Price = price, Kind = kind })],
        },
    };

    // 上限の直前・ちょうど・超え
    public static TheoryData<int[], long> Totals => new()
    {
        { [int.MaxValue - 1], int.MaxValue - 1L },
        { [int.MaxValue - 1, 1], int.MaxValue },
        { [int.MaxValue, 1], int.MaxValue + 1L },
        { [int.MaxValue, int.MaxValue, int.MaxValue], 3L * int.MaxValue },
    };

    [Theory]
    [MemberData(nameof(Totals))]
    public void 自分用の合計は32bitを超えても正しく足す(int[] prices, long expected)
    {
        var item = ItemWith("9900001", PurchaseKind.ForSelf, prices);

        Assert.Equal(expected, Purchases.SelfSpendOf(item));
        Assert.Equal(expected, Purchases.SelfPaidOrNull(item));
    }

    [Theory]
    [MemberData(nameof(Totals))]
    public void 贈った額の合計も32bitを超えて足す(int[] prices, long expected)
        => Assert.Equal(expected, Purchases.GivenSpendOf(ItemWith("9900001", PurchaseKind.Given, prices)));

    [Fact]
    public void 統計の支出と贈った額は32bitを超えても投げない()
    {
        var self = ItemWith("9900001", PurchaseKind.ForSelf, int.MaxValue, int.MaxValue);
        var given = ItemWith("9900002", PurchaseKind.Given, int.MaxValue, 1);

        var snapshot = StatsService.Build([self, given], new AvatarRegistry(), unresolvedCount: 0);

        Assert.Equal(2L * int.MaxValue, snapshot.SpentYen);
        Assert.Equal(int.MaxValue + 1L, snapshot.GivenSpentYen);
    }

    [Fact]
    public void 払った額の並べ替えは32bitを超えた合計を上に置く()
    {
        var huge = ItemWith("9900001", PurchaseKind.ForSelf, int.MaxValue, 1);
        var large = ItemWith("9900002", PurchaseKind.ForSelf, int.MaxValue);
        var small = ItemWith("9900003", PurchaseKind.ForSelf, 500);

        var order = ItemOrder.BySelfPaid([small, huge, large], descending: true).Select(item => item.Id).ToList();

        Assert.Equal(["9900001", "9900002", "9900003"], order);
    }
}
