using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>タグの管理・属性の管理の商品の検索。検索画面と同じ書き方が効くこと。</summary>
public sealed class ItemTextFilterTests
{
    private static ItemRecord Item(string name, string shop = "", string? memo = null) => new()
    {
        Id = name,
        Booth = new BoothBlock
        {
            Name = name,
            Shop = shop.Length == 0 ? null : new BoothShop { Name = shop, Subdomain = "kumo" },
            FetchedAt = DateTimeOffset.UnixEpoch,
        },
        Local = new LocalBlock { Memo = memo },
    };

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    public void 空なら絞らない(string? text) => Assert.Null(ItemTextFilter.Create(text));

    [Fact]
    public void スペースは両方を含む()
    {
        var filter = ItemTextFilter.Create("夏 リボン")!;

        Assert.True(filter.Matches(Item("夏のリボン")));
        Assert.False(filter.Matches(Item("冬のリボン")));
    }

    [Fact]
    public void ハイフンで除く()
    {
        var filter = ItemTextFilter.Create("リボン -黒")!;

        Assert.True(filter.Matches(Item("白いリボン")));
        Assert.False(filter.Matches(Item("黒いリボン")));
    }

    [Fact]
    public void ORでどちらか()
    {
        var filter = ItemTextFilter.Create("夏 OR 冬")!;

        Assert.True(filter.Matches(Item("冬服")));
        Assert.False(filter.Matches(Item("春服")));
    }

    [Fact]
    public void 引用符はひとまとまり()
    {
        var filter = ItemTextFilter.Create("\"夏 セット\"")!;

        Assert.True(filter.Matches(Item("夏 セット A")));
        Assert.False(filter.Matches(Item("夏の小物セット")));
    }

    [Fact]
    public void 検索画面と同じくショップ名も探し前置きで絞れる()
    {
        Assert.True(ItemTextFilter.Create("くもの店")!.Matches(Item("帽子", shop: "くもの店")));
        Assert.False(ItemTextFilter.Create("name:くもの店")!.Matches(Item("帽子", shop: "くもの店")));
    }

    [Fact]
    public void 名前にも同じ書き方が効く()
    {
        var filter = ItemTextFilter.Create("リボン -黒")!;

        Assert.True(filter.MatchesName("リボン"));
        Assert.False(filter.MatchesName("黒リボン"));
        Assert.False(filter.MatchesName("帽子"));
    }

    [Fact]
    public void 全角と半角を気にしない()
        => Assert.True(ItemTextFilter.Create("ＲＩＢＢＯＮ")!.Matches(Item("ribbon")));
}
