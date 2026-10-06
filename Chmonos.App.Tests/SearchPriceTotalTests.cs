using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 価格の絞り込みが、32bit を超えた払った額の合計を扱えること（外部の点検 2026-10-06）。
/// 前は合計が負の額に回り込み、「下限 0円」でも外れた。範囲の欄も int でしか読めず、上限を超える数を打つと空と同じになった。
/// </summary>
public class SearchPriceTotalTests
{
    private static readonly SearchModuleContext Context = new(
        () => throw new InvalidOperationException("対応アバターの索引は使わない"),
        ModificationUsage.Empty,
        RecentTimes.Empty,
        null,
        new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(9)));

    private static ItemRecord Paid(string id, params int[] prices)
        => Make.Item(id, "作り物 " + id) with
        {
            Local = new LocalBlock { Purchases = [.. prices.Select(price => new Purchase { Price = price, Kind = PurchaseKind.ForSelf })] },
        };

    // 上限の直前・ちょうど・超え
    private static readonly ItemRecord[] Items =
    [
        Paid("9900601", int.MaxValue - 1),
        Paid("9900602", int.MaxValue - 1, 1),
        Paid("9900603", int.MaxValue, 1),
    ];

    private static RangeModule Price(string min, string max)
    {
        var module = new RangeModule(
            SearchModuleKind.Price,
            (item, _) => Purchases.SelfPaidOrNull(item) is { } paid ? [paid] : [],
            "円")
        {
            AllValuesOf = _ => Items.Select(Purchases.SelfPaidOrNull).OfType<long>(),
            Floor = 100,
        };
        module.RefreshBounds();
        module.MinText = min;
        module.MaxText = max;
        return module;
    }

    private static string[] Passing(SearchModule module)
    {
        module.Prepare(Context);
        return Items.Where(item => module.Passes(item, Context)).Select(item => item.Id[^1..]).ToArray();
    }

    [Theory]
    [InlineData("0", "", "1,2,3")]
    [InlineData("2147483647", "", "2,3")]
    [InlineData("2147483648", "", "3")]
    [InlineData("0", "2147483647", "1,2")]
    public void 範囲は32bitを超えた合計でも照らせる(string min, string max, string expected)
        => Assert.Equal(expected.Split(','), Passing(Price(min, max)));

    [Fact]
    public void 右端は手元のいちばん大きい合計()
        => Assert.Equal("2,147,483,648円", Price("0", "").MaximumLabel);
}
