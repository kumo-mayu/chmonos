using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 価格の「すべての価格が範囲内の商品のみ」（ユーザ判断 2026-10-06・メモ82-6）と、除くときとの組み合わせ。
/// 除くの意味は他の条件と同じ「除かないときに当たる物、以外」で、数の分からない商品は除くときも外す。
/// 表（範囲 500〜1,000円・外れ値を無視は入で境は 5,000円）：
///
/// | 商品 | 価格 | どれか | どれか・除く | 全て | 全て・除く |
/// |---|---|---|---|---|---|
/// | A | 800 | 当たる | — | 当たる | — |
/// | B | 300・800 | 当たる | — | — | 当たる |
/// | C | 300 | — | 当たる | — | 当たる |
/// | D | なし | — | — | — | — |
/// | E | 800・2,000 | 当たる | — | — | 当たる |
/// | F | 800・99,999（外れ値） | 当たる | — | 当たる | — |
/// | G | 99,999（外れ値だけ） | — | — | — | — |
/// </summary>
public class SearchPriceMatchAllTests
{
    private static readonly SearchModuleContext Context = new(
        () => throw new InvalidOperationException("対応アバターの索引は使わない"),
        ModificationUsage.Empty,
        RecentTimes.Empty,
        null,
        new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(9)));

    private static readonly Dictionary<string, int[]> Prices = new()
    {
        ["9900501"] = [800],
        ["9900502"] = [300, 800],
        ["9900503"] = [300],
        ["9900504"] = [],
        ["9900505"] = [800, 2000],
        ["9900506"] = [800, 99999],
        ["9900507"] = [99999],
    };

    private static readonly ItemRecord[] Items = [.. Prices.Keys.Select(id => Make.Item(id, "作り物 " + id))];

    private static RangeModule Price(bool matchAll, bool exclude)
    {
        var module = new RangeModule(
            SearchModuleKind.Price,
            (item, _) => Prices[item.Id],
            "円",
            [new ChoiceOption("paid", "購入額"), new ChoiceOption("booth", "BOOTHの価格")])
        {
            // 外れ値の境は手元の価格の全部から決める：1,000円が20個なら 5,000円
            AllValuesOf = _ => Enumerable.Repeat(1000, 20).Append(99999),
            Floor = 100,
            SupportsOutliers = true,
            SupportsMatchAll = true,
        };
        module.RefreshBounds();
        module.MinText = "500";
        module.MaxText = "1000";
        module.MatchAll = matchAll;
        module.IsExcluded = exclude;
        return module;
    }

    private static string[] Passing(SearchModule module)
    {
        module.Prepare(Context);
        return Items.Where(item => module.Passes(item, Context)).Select(item => item.Id[^1..]).ToArray();
    }

    [Theory]
    [InlineData(false, false, "1,2,5,6")]
    [InlineData(false, true, "3")]
    [InlineData(true, false, "1,6")]
    [InlineData(true, true, "2,3,5")]
    public void 全てか_どれかか_と除くの組み合わせで当たる商品(bool matchAll, bool exclude, string expected)
    {
        Assert.Equal(expected.Split(','), Passing(Price(matchAll, exclude)));
    }

    [Fact]
    public void 既定はどれか1つが範囲に入れば当たる()
    {
        var module = new RangeModule(SearchModuleKind.Price, (_, _) => [], "円") { SupportsMatchAll = true };

        Assert.False(module.MatchAll);
    }

    [Fact]
    public void 全てを選んだことは状態に残り_読み直しても効く()
    {
        var state = Price(matchAll: true, exclude: false).Save();
        Assert.True(state.MatchAll);

        var restored = Price(matchAll: false, exclude: false);
        restored.Load(state);

        Assert.True(restored.MatchAll);
        Assert.Equal(["1", "6"], Passing(restored));
        Assert.Contains("すべての価格が範囲内", restored.SummaryText);
    }

    [Fact]
    public void 切り替えを持たない条件では_状態に全てと書いてあっても読まない()
    {
        var likes = new RangeModule(SearchModuleKind.WishList, (_, _) => [10], string.Empty) { AllValuesOf = _ => [10] };
        likes.Load(new SearchModuleState { Kind = "WishList", MatchAll = true, Min = "0", Max = "10" });

        Assert.False(likes.MatchAll);
        Assert.False(likes.Save().MatchAll);
    }

    [Fact]
    public void 条件をクリアするとどれかに戻る()
    {
        var module = Price(matchAll: true, exclude: false);

        module.Clear();

        Assert.False(module.MatchAll);
    }
}
