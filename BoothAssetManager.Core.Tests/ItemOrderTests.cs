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

    // ---- 2026-09-24 に足した並べ替え（商品名・ショップ名はすべて作り物） ----

    private static ItemRecord Rich(
        string id,
        DateOnly? acquired = null,
        string? shop = null,
        string? category = null,
        long? size = null,
        int? wishes = null,
        DateTimeOffset? published = null,
        int[]? prices = null,
        Purchase[]? purchases = null,
        string? displayName = null,
        string? userCategory = null) => new()
        {
            Id = id,
            Booth = new BoothBlock
            {
                Name = id,
                FetchedAt = wishes is null ? null : DateTimeOffset.UnixEpoch,
                WishListsCount = wishes ?? 0,
                PublishedAt = published,
                Variations = (prices ?? []).Select((price, index) => new BoothVariation { Id = index + 1, Price = price }).ToList(),
                Shop = shop is null ? null : new BoothShop { Name = shop, Subdomain = shop },
                Category = category is null ? null : new BoothCategory { Id = 1, Name = category },
            },
            Local = new LocalBlock
            {
                DisplayName = displayName,
                Category = userCategory,
                AcquiredAt = acquired,
                Purchases = purchases ?? [],
                LocalFiles = size is null ? [] : [new LocalFileRecord { Hash = id, Paths = [id + ".zip"], SizeBytes = size.Value }],
            },
        };

    private static Purchase Paid(int? price, PurchaseKind kind = PurchaseKind.ForSelf) => new() { Price = price, Kind = kind };

    /// <summary>前は 0 バイトとして混ぜていて、小さい順でファイルの無い商品が先頭に来ていた。</summary>
    [Theory]
    [InlineData(false, new[] { "small", "big", "a-none", "b-none" })]
    [InlineData(true, new[] { "big", "small", "a-none", "b-none" })]
    public void ファイルを持っていない商品は容量の向きによらず後ろ(bool descending, string[] expected)
    {
        var items = new[] { Rich("b-none"), Rich("big", size: 900), Rich("a-none"), Rich("small", size: 10) };

        Assert.Equal(expected, Ids(ItemOrder.BySize(items, descending)));
    }

    /// <summary>前は BOOTH に無い商品を 0 として混ぜていて、少ない順で先頭に来ていた。取れた商品の 0 は値として扱う。</summary>
    [Theory]
    [InlineData(false, new[] { "zero", "many", "local" })]
    [InlineData(true, new[] { "many", "zero", "local" })]
    public void BOOTHに無い商品はスキ数の向きによらず後ろ(bool descending, string[] expected)
    {
        var items = new[] { Rich("local"), Rich("many", wishes: 50), Rich("zero", wishes: 0) };

        Assert.Equal(expected, Ids(ItemOrder.ByWishList(items, descending)));
    }

    /// <summary>払った額は自分用の合計。贈った・貰ったは数えない。額が1つも無い商品は後ろ、無料は 0。</summary>
    [Theory]
    [InlineData(false, new[] { "free", "one", "two", "gift-only", "none" })]
    [InlineData(true, new[] { "two", "one", "free", "gift-only", "none" })]
    public void 払った額は自分用の合計で_額の無い商品は後ろ(bool descending, string[] expected)
    {
        var items = new[]
        {
            Rich("none", purchases: [Paid(null)]),
            Rich("one", purchases: [Paid(1500), Paid(9000, PurchaseKind.Given)]),
            Rich("free", purchases: [Paid(0)]),
            Rich("two", purchases: [Paid(1000), Paid(1000)]),
            Rich("gift-only", purchases: [Paid(3000, PurchaseKind.Given), Paid(500, PurchaseKind.Received)]),
        };

        Assert.Equal(expected, Ids(ItemOrder.BySelfPaid(items, descending)));
    }

    [Theory]
    [InlineData(false, new[] { "old", "new", "none" })]
    [InlineData(true, new[] { "new", "old", "none" })]
    public void 公開日が無い商品は向きによらず後ろ(bool descending, string[] expected)
    {
        var items = new[]
        {
            Rich("none"),
            Rich("new", published: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)),
            Rich("old", published: new DateTimeOffset(2021, 5, 1, 0, 0, 0, TimeSpan.Zero)),
        };

        Assert.Equal(expected, Ids(ItemOrder.ByPublished(items, descending)));
    }

    /// <summary>BOOTH の価格はいちばん安いバリエーション（商品ページの「¥300〜」と同じ読み方）。</summary>
    [Theory]
    [InlineData(false, new[] { "from300", "only800", "none" })]
    [InlineData(true, new[] { "only800", "from300", "none" })]
    public void BOOTHの価格はいちばん安いバリエーションで_無い商品は後ろ(bool descending, string[] expected)
    {
        var items = new[] { Rich("none"), Rich("only800", prices: [800]), Rich("from300", prices: [2000, 300]) };

        Assert.Equal(expected, Ids(ItemOrder.ByBoothPrice(items, descending)));
    }

    [Theory]
    [InlineData(false, new[] { "b", "a" })]
    [InlineData(true, new[] { "a", "b" })]
    public void 名前は読みの順(bool descending, string[] expected)
    {
        var items = new[] { Rich("a", displayName: "【新作】ワンピース"), Rich("b", displayName: "あおいリボン") };

        Assert.Equal(expected, Ids(ItemOrder.ByName(items, descending)));
    }

    /// <summary>ショップ名の読みの順。同じショップの中は入手日の新しい順。ショップの無い商品は向きによらず後ろ。</summary>
    [Theory]
    [InlineData(false, new[] { "a-new", "a-old", "a-undated", "k", "n-new", "n-old" })]
    [InlineData(true, new[] { "k", "a-new", "a-old", "a-undated", "n-new", "n-old" })]
    public void ショップの読みの順で_同じショップは入手日の新しい順(bool descending, string[] expected)
    {
        var items = new[]
        {
            Rich("n-old", acquired: new DateOnly(2025, 1, 1)),
            Rich("a-old", acquired: new DateOnly(2025, 1, 1), shop: "アトリエ"),
            Rich("k", acquired: new DateOnly(2024, 1, 1), shop: "きのこ工房"),
            Rich("a-undated", shop: "アトリエ"),
            Rich("n-new", acquired: new DateOnly(2026, 1, 1)),
            Rich("a-new", acquired: new DateOnly(2026, 1, 1), shop: "アトリエ"),
        };

        Assert.Equal(expected, Ids(ItemOrder.ByShop(items, descending)));
    }

    /// <summary>カテゴリは表の順（3Dモデルの子を先に）。同じカテゴリの中は入手日の新しい順。表に無い → 無い の順に後ろ。</summary>
    [Theory]
    [InlineData(false, new[] { "chara", "dress-new", "dress-old", "comic", "unknown", "none" })]
    [InlineData(true, new[] { "comic", "dress-new", "dress-old", "chara", "unknown", "none" })]
    public void カテゴリは表の順で_同じカテゴリは入手日の新しい順(bool descending, string[] expected)
    {
        var path = Path.Combine(Path.GetTempPath(), $"categories-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            { "Parents": [
              { "Name": "漫画", "Children": ["漫画・マンガ"] },
              { "Name": "3Dモデル", "Children": ["3Dキャラクター", "3D衣装"] }
            ] }
            """);

        try
        {
            var table = new CategoryTable(path);
            var items = new[]
            {
                Rich("none", acquired: new DateOnly(2026, 9, 1)),
                Rich("comic", category: "漫画・マンガ"),
                Rich("dress-old", acquired: new DateOnly(2024, 1, 1), category: "3D衣装"),
                Rich("unknown", category: "どこにも無い分け方"),
                Rich("chara", category: "3Dキャラクター"),

                // 人が入れたカテゴリ（BOOTH に無い商品）も同じ表で引く
                Rich("dress-new", acquired: new DateOnly(2026, 1, 1), userCategory: "3D衣装"),
            };

            Assert.Equal(expected, Ids(ItemOrder.ByCategory(items, table, descending)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>同じ値の中の2つ目の鍵は名前の読みの順（先頭の括弧を飛ばし、カタカナとひらがなが混ざって並ぶ）。</summary>
    [Fact]
    public void 同じ値の中は名前の読みの順()
    {
        var day = new DateOnly(2026, 9, 1);
        var items = new[]
        {
            Rich("c", acquired: day, displayName: "きつね"),
            Rich("a", acquired: day, displayName: "【限定】いぬ"),
            Rich("b", acquired: day, displayName: "カメ"),
        };

        Assert.Equal(["a", "b", "c"], Ids(ItemOrder.ByAcquired(items, descending: true)));
    }

    // ---- 検索の写しの並びと、未確定で登録した1件を足す位置（ユーザ判断 2026-09-29） ----

    /// <summary>写しの並び：入手日の新しい順、入手日が無い物は後ろ、同じ日は名前順。</summary>
    [Fact]
    public void 写しは入手日の新しい順で無い物は後ろ()
    {
        var items = new[]
        {
            Item("none"),
            Item("old", acquired: new DateOnly(2025, 1, 1)),
            Item("new-b", acquired: new DateOnly(2026, 9, 1)),
            Item("new-a", acquired: new DateOnly(2026, 9, 1)),
        };

        Assert.Equal(["new-a", "new-b", "old", "none"], Ids(ItemOrder.LibraryOrder(items)));
    }

    /// <summary>
    /// 1件を足す位置は、足した後で全件を並べ直したのと同じ並びになる（読み直す前と後で並びが変わらない）。
    /// 入手日の無い物（未確定で登録した直後の商品）・間の日付・先頭・同じ日の名前の間のどれでも。
    /// </summary>
    [Theory]
    [InlineData("added", null)]
    [InlineData("added", "2025-06-01")]
    [InlineData("added", "2027-01-01")]
    [InlineData("new-aa", "2026-09-01")]
    [InlineData("zzz", "2025-01-01")]
    public void 足す位置は並べ直したのと同じ(string id, string? acquired)
    {
        var sorted = ItemOrder.LibraryOrder(
        [
            Item("none"),
            Item("old", acquired: new DateOnly(2025, 1, 1)),
            Item("new-b", acquired: new DateOnly(2026, 9, 1)),
            Item("new-a", acquired: new DateOnly(2026, 9, 1)),
        ]);
        var added = Item(id, acquired: acquired is null ? null : DateOnly.Parse(acquired, System.Globalization.CultureInfo.InvariantCulture));

        var inserted = sorted.ToList();
        inserted.Insert(ItemOrder.LibraryInsertIndex(sorted, added), added);

        Assert.Equal(Ids(ItemOrder.LibraryOrder([.. sorted, added])), Ids(inserted));
    }

    [Fact]
    public void 空の写しには先頭に足す()
        => Assert.Equal(0, ItemOrder.LibraryInsertIndex([], Item("a")));
}
