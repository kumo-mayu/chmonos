using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>検索の並べ替え。値が無い商品は、昇順でも降順でも後ろにまとめる（技術的負債 4-1・5）。</summary>
public sealed class ItemOrderTests
{
    private static ItemRecord Item(string id, Dictionary<string, int>? attributes = null, DateOnly? acquired = null) => new()
    {
        Id = id,
        Booth = new BoothBlock { Name = id, FetchedAt = DateTimeOffset.UnixEpoch },
        Local = new LocalBlock { Attributes = attributes ?? [], AcquiredAt = acquired },
    };

    private static string[] Ids(IEnumerable<ItemRecord> items) => items.Select(item => item.Id).ToArray();

    /// <summary>同じ値の商品は名前順（入手日の並びと揃える・点検 2026-09-23）。前は元の一覧の順のままだった。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 同じ値の商品は名前順(bool descending)
    {
        var items = new[]
        {
            Item("c", new() { ["かわいい"] = 3 }),
            Item("a", new() { ["かわいい"] = 3 }),
            Item("b", new() { ["かわいい"] = 3 }),
        };

        Assert.Equal(["a", "b", "c"], Ids(ItemOrder.ByAttribute(items, "かわいい", descending)));

        var sameTime = items.ToDictionary(item => item.Id, _ => DateTimeOffset.UnixEpoch);
        Assert.Equal(["a", "b", "c"], Ids(ItemOrder.ByTime(items, sameTime, descending)));
    }

    [Theory]
    [InlineData(false, new[] { "low", "high", "a-unrated", "b-unrated" })]
    [InlineData(true, new[] { "high", "low", "a-unrated", "b-unrated" })]
    public void 属性を付けていない商品は向きによらず後ろ(bool descending, string[] expected)
    {
        var items = new[]
        {
            Item("b-unrated"),
            Item("high", new() { ["かわいい"] = 5 }),
            Item("a-unrated"),
            Item("low", new() { ["かわいい"] = 1 }),
        };

        Assert.Equal(expected, Ids(ItemOrder.ByAttribute(items, "かわいい", descending)));
    }

    [Theory]
    [InlineData(false, new[] { "old", "new", "a-none", "b-none" })]
    [InlineData(true, new[] { "new", "old", "a-none", "b-none" })]
    public void 足跡が無い商品は向きによらず後ろ(bool descending, string[] expected)
    {
        var items = new[] { Item("b-none"), Item("new"), Item("a-none"), Item("old") };
        var times = new Dictionary<string, DateTimeOffset>
        {
            ["old"] = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ["new"] = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        };

        Assert.Equal(expected, Ids(ItemOrder.ByTime(items, times, descending)));
    }

    [Theory]
    [InlineData(false, new[] { "old", "new", "none" })]
    [InlineData(true, new[] { "new", "old", "none" })]
    public void 入手日が無い商品は向きによらず後ろ(bool descending, string[] expected)
    {
        var items = new[] { Item("none"), Item("new", acquired: new DateOnly(2026, 9, 1)), Item("old", acquired: new DateOnly(2025, 1, 1)) };

        Assert.Equal(expected, Ids(ItemOrder.ByAcquired(items, descending)));
    }

    [Fact]
    public void 同じ入手日は名前順()
    {
        var day = new DateOnly(2026, 9, 1);
        var items = new[] { Item("b", acquired: day), Item("a", acquired: day) };

        Assert.Equal(["a", "b"], Ids(ItemOrder.ByAcquired(items, descending: true)));
    }
}
