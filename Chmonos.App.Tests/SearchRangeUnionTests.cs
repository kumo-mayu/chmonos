using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// スキ数・価格・公開日・入手日を複数置き、同じ種類どうしを和集合でつなぐ（ユーザ判断 2026-10-02・メモ2-②）。
/// 「除く」を付けた物は、除かない物の和集合からそれぞれ引く。ほかの種類との間は AND のまま。
/// </summary>
public class SearchRangeUnionTests
{
    private static readonly SearchModuleContext Context = new(
        () => throw new InvalidOperationException("対応アバターの索引は使わない"),
        ModificationUsage.Empty,
        RecentTimes.Empty,
        null,
        new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.FromHours(9)));

    private static ItemRecord Likes(int index, int likes, bool favorite = false, DateOnly? acquired = null)
    {
        var item = Make.Item((1000000 + index).ToString(System.Globalization.CultureInfo.InvariantCulture), "作り物");
        return item with
        {
            Booth = item.Booth with { WishListsCount = likes },
            Local = item.Local with { IsFavorite = favorite, AcquiredAt = acquired },
        };
    }

    private static RangeModule Range(string min, string max, bool exclude = false)
    {
        var module = new RangeModule(SearchModuleKind.WishList, (item, _) => [item.Booth.WishListsCount], string.Empty);
        module.Load(new SearchModuleState { Kind = "WishList", Min = min, Max = max, Exclude = exclude });
        return module;
    }

    private static DateModule Acquired(string since, string till, bool exclude = false)
    {
        var module = new DateModule(SearchModuleKind.AcquiredAt, item => item.Local.AcquiredAt);
        module.Load(new SearchModuleState { Kind = "AcquiredAt", Min = since, Max = till, Exclude = exclude });
        return module;
    }

    private static ChoiceModule Favorite()
    {
        var module = new ChoiceModule(SearchModuleKind.Favorite,
            [new ChoiceOption("favorite", "お気に入りのみ"), new ChoiceOption("other", "お気に入り以外のみ"), new ChoiceOption("both", "両方")], "both",
            (item, key, _) => key switch { "favorite" => item.Local.IsFavorite, "other" => !item.Local.IsFavorite, _ => true });
        module.Load(new SearchModuleState { Kind = "Favorite", Choice = "favorite" });
        return module;
    }

    private static readonly List<ItemRecord> Items =
    [
        Likes(1, 50), Likes(2, 250), Likes(3, 1200), Likes(4, 1800), Likes(5, 3000),
        Likes(6, 380, favorite: true), Likes(7, 1500, favorite: true), Likes(8, 900, favorite: true),
    ];

    private static string[] Shown(IEnumerable<SearchModule> modules)
        => SearchFilterPass.Run(Items, modules, _ => true, Context).Matches.Select(item => item.Id[^1..]).ToArray();

    [Fact]
    public Task 同じ種類の範囲を2つ置くと_どちらかに当てはまる物を出す() => UiThread.Run(() =>
    {
        // 「0-400 と 1000-2000 なら、それぞれに当てはまるものの和集合」（ユーザの例）
        Assert.Equal(["1", "2", "3", "4", "6", "7"], Shown([Range("0", "400"), Range("1000", "2000")]));
    });

    [Fact]
    public Task 除く範囲は_除かない範囲の和集合から引く() => UiThread.Run(() =>
    {
        Assert.Equal(["1", "3", "4", "7"], Shown([Range("0", "400"), Range("1000", "2000"), Range("200", "400", exclude: true)]));
    });

    [Fact]
    public Task 除く範囲だけなら_どれにも当てはまらない物を出す() => UiThread.Run(() =>
    {
        Assert.Equal(["2", "5", "6", "8"], Shown([Range("0", "100", exclude: true), Range("1000", "2000", exclude: true)]));
    });

    [Fact]
    public Task ほかの種類との間は今までどおり全部に当てはまる物() => UiThread.Run(() =>
    {
        Assert.Equal(["6", "7"], Shown([Range("0", "400"), Favorite(), Range("1000", "2000")]));
    });

    [Fact]
    public Task 日付も和集合で_日付の分からない商品は除くだけでも出ない() => UiThread.Run(() =>
    {
        List<ItemRecord> items =
        [
            Likes(1, 0, acquired: new DateOnly(2025, 1, 10)),
            Likes(2, 0, acquired: new DateOnly(2025, 6, 10)),
            Likes(3, 0, acquired: new DateOnly(2026, 1, 10)),
            Likes(4, 0),
        ];

        string[] Pass(params SearchModule[] modules)
            => SearchFilterPass.Run(items, modules, _ => true, Context).Matches.Select(item => item.Id[^1..]).ToArray();

        Assert.Equal(["1", "3"], Pass(Acquired("2025-01-01", "2025-01-31"), Acquired("2026-01-01", "2026-01-31")));
        Assert.Equal(["2"], Pass(Acquired("2025-01-01", "2025-01-31", exclude: true), Acquired("2026-01-01", "2026-01-31", exclude: true)));
    });

    [Fact]
    public Task ほかの条件の件数の相手は_和集合のまとまりを当てた後の商品() => UiThread.Run(() =>
    {
        var low = Range("0", "400");
        var high = Range("1000", "2000");
        var favorite = Favorite();
        var pass = SearchFilterPass.Run(Items, [low, favorite, high], _ => true, Context);

        // お気に入りの選択肢の件数は、どちらかの範囲に入る商品で数える（前の作りでは2つの範囲を両方満たす商品＝0件だった）
        Assert.Equal(["1", "2", "3", "4", "6", "7"], pass.OthersFor(favorite).Select(item => item.Id[^1..]).Order());

        // 範囲の件数の相手は、まとまりごと除いた他の全部（お気に入り）
        Assert.Equal(["6", "7", "8"], pass.OthersFor(low).Select(item => item.Id[^1..]).Order());
        Assert.Equal(["6", "7", "8"], pass.OthersFor(high).Select(item => item.Id[^1..]).Order());
    });

    // ---- 画面（メニュー・要約） ----

    [Theory]
    [InlineData(SearchModuleKind.WishList)]
    [InlineData(SearchModuleKind.Price)]
    [InlineData(SearchModuleKind.PublishedAt)]
    [InlineData(SearchModuleKind.AcquiredAt)]
    public Task 範囲と日付は2つ目を足せる(SearchModuleKind kind) => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Likes(1, 10, acquired: new DateOnly(2026, 1, 1)));
        var search = (await app.StartAsync()).Search;

        SearchModuleMenuTests.Add(search, kind);
        var entry = search.ModuleMenu.SelectMany(heading => heading.Entries).OfType<SearchModuleMenuEntry>().First(menu => menu.Kind == kind);

        Assert.True(entry.IsAvailable);
        SearchModuleMenuTests.Add(search, kind);
        Assert.Equal(2, search.Modules.Count(module => module.Kind == kind));
    });

    [Fact]
    public Task 和集合の要約は除かない物を中黒でまとめ_除く物は続けて書く() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Likes(1, 50));
        await app.AddItemAsync(Likes(2, 1500));
        await app.AddItemAsync(Likes(3, 3000));
        var search = (await app.StartAsync()).Search;

        var low = (RangeModule)SearchModuleMenuTests.Add(search, SearchModuleKind.WishList);
        low.MinText = "0";
        low.MaxText = "400";
        var high = (RangeModule)SearchModuleMenuTests.Add(search, SearchModuleKind.WishList);
        high.MinText = "1000";
        high.MaxText = "2000";
        var cut = (RangeModule)SearchModuleMenuTests.Add(search, SearchModuleKind.WishList);
        cut.MinText = "1400";
        cut.MaxText = "1600";
        cut.IsExcluded = true;

        // 打った数で絞り直すのは止まってから1回（150ms）。名指しで待つ
        await UiThread.Until(() => search.ResultSummary == "1 件", "3つの範囲で絞り直す");

        Assert.Equal(3, search.ActiveFilterCount);
        Assert.Equal("スキ数 0以上 400以下・スキ数 1,000以上 2,000以下 / 除く：スキ数 1,400以上 1,600以下", search.FilterSummary);
    });
}
