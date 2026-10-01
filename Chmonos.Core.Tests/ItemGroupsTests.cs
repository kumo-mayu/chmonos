using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>並べ替えた一覧を、まとまり（ショップ・カテゴリ・月）ごとに切る（検索の並べ替えの区切りの札。ユーザ判断 2026-10-01）。</summary>
public sealed class ItemGroupsTests
{
    private static ItemRecord Item(
        string id,
        string? shop = null,
        string? category = null,
        string? categoryParent = null,
        string? userCategory = null,
        string? userShop = null) => new()
        {
            Id = id,
            Booth = new BoothBlock
            {
                Name = id,
                Shop = shop is null ? null : new BoothShop { Name = shop, Subdomain = shop },
                Category = category is null ? null : new BoothCategory { Id = 1, Name = category, ParentName = categoryParent },
            },
            Local = new LocalBlock
            {
                Category = userCategory,
                Shop = userShop is null ? null : new LocalShop { Name = userShop, Subdomain = LocalShopKey.For(userShop) },
            },
        };

    [Fact]
    public void 隣どうしで鍵が変わった所で切り_件数と始まりを数える()
    {
        string[] keys = ["a", "a", "b", "c", "c", "c"];

        var groups = ItemGroups.Split(keys, key => (key, key.ToUpperInvariant(), null));

        Assert.Equal(
            [("a", "A", 0, 2), ("b", "B", 2, 1), ("c", "C", 3, 3)],
            groups.Select(group => (group.Key, group.Label, group.Start, group.Count)));
    }

    [Fact]
    public void 空の一覧はまとまりも無い()
        => Assert.Empty(ItemGroups.Split(Array.Empty<string>(), key => (key, key, null)));

    [Fact]
    public void 並びの中で離れた同じ鍵は_別のまとまりになる()
    {
        // 並べ替えと切り方が食い違っても、商品の順は変えない（札が2枚出るだけ）
        string[] keys = ["a", "b", "a"];

        Assert.Equal(3, ItemGroups.Split(keys, key => (key, key, null)).Count);
    }

    [Fact]
    public void ショップは画面に出す名前で分け_ショップの無い商品は1つにまとめる()
    {
        Assert.Equal(("shop:作り物の店", "作り物の店", (string?)null), ItemGroups.ShopOf(Item("1", shop: "作り物の店")));

        // 人が入れたショップ名を先に見る（カードと商品ページに出る名前）
        Assert.Equal("自分で入れた店", ItemGroups.ShopOf(Item("2", shop: "作り物の店", userShop: "自分で入れた店")).Label);

        Assert.Equal(ItemGroups.NoShop, ItemGroups.ShopOf(Item("3")).Label);
        Assert.Equal(ItemGroups.ShopOf(Item("3")).Key, ItemGroups.ShopOf(Item("4")).Key);
    }

    [Fact]
    public void カテゴリは子の名前で分け_親は表から_表に無ければBOOTHから取れた親()
    {
        var path = Path.Combine(Path.GetTempPath(), $"categories-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            { "Parents": [ { "Name": "3Dモデル", "Children": ["3D衣装"] } ] }
            """);

        try
        {
            var table = new CategoryTable(path);

            Assert.Equal(("category:3D衣装", "3D衣装", "3Dモデル"), ItemGroups.CategoryOf(Item("1", category: "3D衣装"), table));

            // 人が入れたカテゴリも同じ表で親を引く
            Assert.Equal("3Dモデル", ItemGroups.CategoryOf(Item("2", userCategory: " 3D衣装 "), table).Parent);

            // 表に無いカテゴリは BOOTH から取れた親
            Assert.Equal("作り物の親", ItemGroups.CategoryOf(Item("3", category: "作り物の子", categoryParent: "作り物の親"), table).Parent);

            // 人が入れた表に無いカテゴリには、BOOTH の親を付けない（BOOTH のカテゴリとは別の物）
            Assert.Null(ItemGroups.CategoryOf(Item("4", category: "作り物の子", categoryParent: "作り物の親", userCategory: "自分の分け方"), table).Parent);

            Assert.Equal(ItemGroups.NoCategory, ItemGroups.CategoryOf(Item("5"), table).Label);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 月は年と月で分け_値の無い商品は1つにまとめる()
    {
        Assert.Equal(("month:2026-09", "2026年9月", (string?)null), ItemGroups.MonthOf(2026, 9, "公開日なし"));
        Assert.Equal(("month-none", "公開日なし", (string?)null), ItemGroups.MonthOf(null, null, "公開日なし"));
    }

    [Theory]
    [InlineData(true, "2026年9月 2|2026年8月 1|2025年9月 1|入手日なし 2")]
    [InlineData(false, "2025年9月 1|2026年8月 1|2026年9月 2|入手日なし 2")]
    public void 入手日順は並べ替えと同じ入手日で年月に切り_入手日の無い商品は向きによらず最後の1つ(bool descending, string expected)
    {
        static ItemRecord On(string id, int? year, int month = 1, int day = 1)
        {
            var item = Item(id);
            return item with { Local = item.Local with { AcquiredAt = year is { } y ? new DateOnly(y, month, day) : null } };
        }

        ItemRecord[] items =
        [
            On("a", 2026, 9, 3), On("b", null), On("c", 2026, 8, 31), On("d", 2025, 9, 15), On("e", 2026, 9, 1), On("f", null),
        ];

        var groups = ItemGroups.Split(ItemOrder.ByAcquired(items, descending).ToList(), ItemGroups.AcquiredOf);

        Assert.Equal(expected, string.Join("|", groups.Select(group => $"{group.Label} {group.Count}")));
        Assert.Equal(ItemGroups.NoAcquired, groups[^1].Label);
    }
}
