using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

/// <summary>
/// 保存した条件の形から判定を作る入口（<see cref="SearchModuleConditions"/>）の試験（2026-10-12・外から使う API が使う）。
/// 画面の条件と同じ値を読むことは App の <c>SearchModuleConditionParityTests</c> が確かめる。ここは種類ごとの照らし方そのものと、
/// 画面を通らずに使うときの決まり（準備・商品の全体から決まる値）を見る。
/// </summary>
public class SearchModuleConditionTests
{
    private static ItemRecord Item(string id, Func<BoothBlock, BoothBlock>? booth = null, Func<LocalBlock, LocalBlock>? local = null)
    {
        var record = new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = "作り物の商品" },
            Local = new LocalBlock
            {
                LocalFiles = [new LocalFileRecord { Hash = id.PadLeft(64, '0'), Paths = [$@"D:\files\{id}.zip"], SizeBytes = 1 }],
            },
        };
        return record with { Booth = booth?.Invoke(record.Booth) ?? record.Booth, Local = local?.Invoke(record.Local) ?? record.Local };
    }

    private static SearchModuleContext Context(IReadOnlyList<ItemRecord> items, DateTimeOffset? now = null) => new(
        () => AvatarCompatibilityIndex.Build(new AvatarRegistry()),
        ModificationUsage.Empty,
        RecentTimes.Empty,
        null,
        now ?? new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.FromHours(9)))
    {
        Items = items,
    };

    private static string[] Hits(SearchModuleState state, IReadOnlyList<ItemRecord> items, SearchModuleContext? context = null)
    {
        context ??= Context(items);
        var condition = SearchModuleConditions.Build(state, context)!;
        return items.Where(item => state.Exclude ? condition.MatchesExcluded(item, context) : condition.Matches(item, context))
            .Select(item => item.Id).ToArray();
    }

    [Fact]
    public void 選ぶ条件は_選んだ鍵どおりに照らし_何も絞らない鍵は全部を通す()
    {
        var items = new[]
        {
            Item("1", local: local => local with { IsFavorite = true }),
            Item("2", booth => booth with { IsAdult = true }, local => local with { IsHidden = true }),
            Item("3"),
        };

        Assert.Equal(["1"], Hits(new() { Kind = "Favorite", Choice = "favorite" }, items));
        Assert.Equal(["2", "3"], Hits(new() { Kind = "Favorite", Choice = "other" }, items));
        Assert.Equal(["2"], Hits(new() { Kind = "Hidden", Choice = "hidden" }, items));
        Assert.Equal(["1", "3"], Hits(new() { Kind = "Adult", Choice = "general" }, items));
        Assert.False(SearchModuleConditions.From(new() { Kind = "Favorite", Choice = "both" })!.IsActive);

        // 知らない鍵は先頭の選択肢（足したときの既定）として読む
        Assert.Equal(["1"], Hits(new() { Kind = "Favorite", Choice = "無い鍵" }, items));
    }

    [Fact]
    public void 選ぶ条件の補助の切り替えは名前で読み_欠けていれば既定()
    {
        var items = new[]
        {
            Item("1", local: local => local with { Purchases = [new Purchase { Kind = PurchaseKind.Given, Price = 100 }] }),
            Item("2"),
        };

        Assert.Equal(["1"], Hits(new() { Kind = "Gift", Choice = "given" }, items));
        Assert.Equal(["1", "2"], Hits(new() { Kind = "Gift", Choice = "given", Toggles = new Dictionary<string, bool> { ["includeUnrecorded"] = true } }, items));

        // 使い回しの Flag は読まない（前の版が書いた true を、後から足した切り替えとして読まない）
        Assert.Equal(["1"], Hits(new() { Kind = "Gift", Choice = "given", Flag = true }, items));
    }

    [Fact]
    public void 一覧の条件は_ORとANDと除くで照らす()
    {
        var items = new[]
        {
            Item("1", booth => booth with { Tags = ["夏", "水着"] }),
            Item("2", booth => booth with { Tags = ["冬"] }),
            Item("3"),
        };

        Assert.Equal(["1", "2"], Hits(new() { Kind = "BoothTag", Items = ["夏", "冬"] }, items));

        // BOOTHタグは AND を持たない（書かれていても OR で照らす）
        Assert.Equal(["1", "2"], Hits(new() { Kind = "BoothTag", Items = ["夏", "冬"], MatchAll = true }, items));
        Assert.Equal(["2", "3"], Hits(new() { Kind = "BoothTag", Items = ["夏"], Exclude = true }, items));
        Assert.False(SearchModuleConditions.From(new() { Kind = "BoothTag" })!.IsActive);
    }

    [Fact]
    public void 範囲は_数の分からない商品を除くときも外す()
    {
        var items = new[]
        {
            Item("1", local: local => local with { Purchases = [new Purchase { Kind = PurchaseKind.ForSelf, Price = 500 }] }),
            Item("2", local: local => local with { Purchases = [new Purchase { Kind = PurchaseKind.ForSelf, Price = 3000 }] }),
            Item("3"),
        };

        Assert.Equal(["1"], Hits(new() { Kind = "Price", Min = "100", Max = "１，０００" }, items));
        Assert.Equal(["2"], Hits(new() { Kind = "Price", Min = "100", Max = "1000", Exclude = true }, items));

        // 値の分からない商品も足す切り替えは、除くときも足す
        Assert.Equal(["2", "3"], Hits(new() { Kind = "Price", Min = "100", Max = "1000", Flag = true, Exclude = true }, items));

        // 空欄の境は制限しないが、数の分かる商品だけに絞る（画面で足した直後と違い、手元に数が無くても「効いて0件」になる）
        Assert.Equal(["1", "2"], Hits(new() { Kind = "Price", Min = string.Empty, Max = string.Empty }, items));
        Assert.Empty(Hits(new() { Kind = "Price" }, [Item("3")]));
    }

    [Fact]
    public void 価格の外れ値は_商品の全体から求めた境で外す()
    {
        var items = Enumerable.Range(1, 20)
            .Select(index => Item($"{index}", booth => booth with { Variations = [new BoothVariation { Id = index, Price = 1000 }] }))
            .Append(Item("99", booth => booth with { Variations = [new BoothVariation { Id = 99, Price = 9_999_999 }] }))
            .ToList();

        var state = new SearchModuleState { Kind = "Price", Choice = SearchModuleConditions.BoothSource, Min = "5000", Max = string.Empty };
        Assert.Empty(Hits(state, items));
        Assert.Equal(["99"], Hits(state with { IgnoreOutliers = false }, items));
    }

    [Fact]
    public void 日付の空欄の境は_準備で渡した商品の端を指す()
    {
        var items = new[]
        {
            Item("1", local: local => local with { Purchases = [new Purchase { Kind = PurchaseKind.ForSelf, PurchasedAt = new DateOnly(2025, 1, 10) }] }),
            Item("2", local: local => local with { Purchases = [new Purchase { Kind = PurchaseKind.ForSelf, PurchasedAt = new DateOnly(2025, 8, 1) }] }),
            Item("3"),
        };

        var state = new SearchModuleState { Kind = "AcquiredAt", Min = string.Empty, Max = "2025-03-31" };
        Assert.Equal(["1"], Hits(state, items));

        // 準備の前に効いているかを読んでも、後の照らし方は渡した材料の端で求め直す
        var context = Context(items);
        var condition = SearchModuleConditions.From(state)!;
        Assert.True(condition.IsActive);
        Assert.Equal(["1"], items.Where(item => condition.Matches(item, context)).Select(item => item.Id));

        // 日付の分からない商品は、除くときも外す
        Assert.Equal(["2"], Hits(state with { Exclude = true }, items));
    }

    [Fact]
    public void 年の無い日付は_材料の今日から読む()
    {
        var items = new[]
        {
            Item("1", booth => booth with { PublishedAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(9)) }),
            Item("2", booth => booth with { PublishedAt = new DateTimeOffset(2025, 9, 1, 12, 0, 0, TimeSpan.FromHours(9)) }),
        };

        // 2026-10-12 から見た「9/1」は今年の9月1日
        Assert.Equal(["1"], Hits(new() { Kind = "PublishedAt", Min = "9/1", Max = "9/1" }, items));
    }

    [Fact]
    public void 知らない種類は作らない()
    {
        Assert.Null(SearchModuleConditions.From(new() { Kind = "無い種類" }));
        Assert.Null(SearchModuleConditions.From(new() { Kind = "999" }));
    }
}
