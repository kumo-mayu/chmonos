using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索の条件の「除く」（ユーザ判断 2026-10-01・`docs/research/search-modules-2026-10-01.md`）。
///
/// 除く＝その条件を除かないとき（AND／OR などの設定はそのまま）に当てはまる物、以外。
/// ただし範囲・日付・属性は、値の分からない商品を除くときも外す。条件の部品を直に作り、照らす口（<see cref="SearchModule.Passes"/>）で確かめる。
/// </summary>
public class SearchExcludeTests
{
    private static readonly SearchModuleContext Context = new(
        () => throw new InvalidOperationException("対応アバターの索引は使わない"),
        ModificationUsage.Empty,
        RecentTimes.Empty,
        null,
        new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(9)));

    private static ItemRecord Tagged(string id, params string[] tags)
        => Make.Item(id, "作り物") with { Booth = Make.Item(id, "作り物").Booth with { Tags = tags } };

    private static ListModule Tags(bool matchAll, params string[] keys)
    {
        var module = new ListModule(SearchModuleKind.BoothTag, allowsAnd: true, "BOOTHタグで絞り込む", "なし",
            (item, _, key, _) => item.Booth.Tags.Contains(key));
        module.Load(new SearchModuleState { Kind = "BoothTag", Items = keys, MatchAll = matchAll });
        return module;
    }

    private static string[] Passing(SearchModule module, params ItemRecord[] items)
    {
        module.Prepare(Context);
        return items.Where(item => module.Passes(item, Context)).Select(item => item.Id).ToArray();
    }

    // ---- 候補から積む条件 ----

    [Fact]
    public Task ORで除くと_選んだどれかが付いた商品を外す() => UiThread.Run(() =>
    {
        var module = Tags(matchAll: false, "A", "B");
        module.IsExcluded = true;

        Assert.Equal(["4"], Passing(module, Tagged("1", "A"), Tagged("2", "B"), Tagged("3", "A", "B"), Tagged("4", "C")));
    });

    [Fact]
    public Task ANDで除くと_選んだ全部が付いた商品だけを外す() => UiThread.Run(() =>
    {
        // 前に決めた「除くときは AND／OR にかかわらず OR」は取りやめた（ユーザ判断 2026-10-01 の案1）。AND はそのまま効く
        var module = Tags(matchAll: true, "A", "B");
        module.IsExcluded = true;

        Assert.Equal(["1", "2", "4"], Passing(module, Tagged("1", "A"), Tagged("2", "B"), Tagged("3", "A", "B"), Tagged("4", "C")));
    });

    [Fact]
    public Task 除くのをやめると_元の当てはまる商品に戻る() => UiThread.Run(() =>
    {
        var module = Tags(matchAll: false, "A");
        module.IsExcluded = true;
        module.IsExcluded = false;

        Assert.Equal(["1"], Passing(module, Tagged("1", "A"), Tagged("2", "B")));
    });

    [Fact]
    public Task 対応アバターのチェックは除くときもそのまま効く() => UiThread.Run(() =>
    {
        // 「指定が無い」かたまりを持つ条件（対応アバターと同じ作り）。商品の Tags が空なら指定が無い扱い
        var module = new ListModule(SearchModuleKind.Avatar, allowsAnd: true, "アバターで絞り込む", "なし",
            (item, _, key, _) => item.Booth.Tags.Contains(key),
            isUnspecified: (item, _) => item.Booth.Tags.Count == 0);
        module.Load(new SearchModuleState { Kind = "Avatar", Items = ["A"], ShowMatched = true, ShowUnspecified = false });
        module.IsExcluded = true;

        var items = new[] { Tagged("1", "A"), Tagged("2", "B"), Tagged("3") };

        // ☑対応している ☐指定が無い で A を除く → A に対応していない商品と、指定が無い商品が残る
        Assert.Equal(["2", "3"], Passing(module, items));

        // ☑対応している ☑指定が無い で除く → 指定が無い商品も「当てはまる物」なので外れる
        module.ShowUnspecified = true;
        Assert.Equal(["2"], Passing(module, items));

        // 指定が無いだけを出す形で除く → 指定のある商品だけ
        module.ShowMatched = false;
        Assert.Equal(["1", "2"], Passing(module, items));
    });

    // ---- 範囲・日付・属性：値の分からない商品は除くときも外す ----

    [Fact]
    public Task 範囲を除くと_範囲の外の商品だけが残り_数の分からない商品は外れる() => UiThread.Run(() =>
    {
        var module = new RangeModule(SearchModuleKind.WishList, (item, _) => item.Booth.WishListsCount is > 0 and var count ? [count] : [], string.Empty);
        module.Load(new SearchModuleState { Kind = "WishList", Min = "100", Max = "200" });
        module.IsExcluded = true;

        ItemRecord Liked(string id, int count) => Make.Item(id, "作り物") with { Booth = Make.Item(id, "作り物").Booth with { WishListsCount = count } };

        // 150 は範囲の中、50・300 は外、0 は「分からない」として扱う作り物
        Assert.Equal(["2", "3"], Passing(module, Liked("1", 150), Liked("2", 50), Liked("3", 300), Liked("4", 0)));
    });

    [Fact]
    public Task 日付を除くと_日付の分からない商品は外れる() => UiThread.Run(() =>
    {
        var module = new DateModule(SearchModuleKind.AcquiredAt, item => item.Local.AcquiredAt);
        module.Load(new SearchModuleState { Kind = "AcquiredAt", Min = "2026-01-01", Max = "2026-06-30" });
        module.IsExcluded = true;

        ItemRecord At(string id, DateOnly? date) => Make.Item(id, "作り物") with { Local = Make.Item(id, "作り物").Local with { AcquiredAt = date } };

        Assert.Equal(["2"], Passing(module, At("1", new DateOnly(2026, 3, 1)), At("2", new DateOnly(2025, 3, 1)), At("3", null)));
    });

    [Fact]
    public Task 属性を除くと_選んだ属性が全部評価済みで当てはまらない商品だけが残る() => UiThread.Run(() =>
    {
        var module = new AttributeModule();
        module.AddRow("かわいさ", 50, 100, notify: false);
        module.AddRow("品質", 50, 100, notify: false);
        module.MatchAll = false;
        module.IsExcluded = true;

        ItemRecord Rated(string id, params (string Name, int Value)[] values)
            => Make.Item(id, "作り物") with { Local = Make.Item(id, "作り物").Local with { Attributes = values.ToDictionary(pair => pair.Name, pair => pair.Value) } };

        var items = new[]
        {
            Rated("1", ("かわいさ", 80), ("品質", 10)),  // OR で当たる → 外す
            Rated("2", ("かわいさ", 10), ("品質", 10)),  // 両方評価済みで当たらない → 残す
            Rated("3", ("かわいさ", 10)),               // 品質が未評価 → 除くときも外す
            Rated("4"),
        };

        Assert.Equal(["2"], Passing(module, items));

        // AND にすると、両方当たる物だけが「当てはまる物」になる。1 は片方だけなので残る
        module.MatchAll = true;
        Assert.Equal(["1", "2"], Passing(module, items));
    });

    // ---- ユーザータグ：大分類どうし・枠の中の AND／OR もそのまま効かせる（D3 の案1） ----

    [Fact]
    public Task ユーザータグを除くと_AND_ORの設定どおりに当てはまる物を外す() => UiThread.Run(() =>
    {
        var module = new UserTagModule();
        module.AddTop("衣装", notify: false)!.AddSubQuietly("トップス");
        module.AddTop("小物", notify: false);
        module.IsExcluded = true;

        ItemRecord With(string id, params UserTagAssignment[] tags)
            => Make.Item(id, "作り物") with { Local = Make.Item(id, "作り物").Local with { UserTags = tags } };

        var items = new[]
        {
            With("1", new UserTagAssignment { Top = "衣装", Subs = ["トップス"] }),
            With("2", new UserTagAssignment { Top = "衣装", Subs = ["ボトムス"] }),
            With("3", new UserTagAssignment { Top = "小物" }),
            With("4", new UserTagAssignment { Top = "衣装", Subs = ["トップス"] }, new UserTagAssignment { Top = "小物" }),
            With("5"),
        };

        // 大分類どうし OR：衣装（トップス）か小物のどちらかが付いた商品を外す
        Assert.Equal(["2", "5"], Passing(module, items));

        // 大分類どうし AND：両方付いた商品だけを外す
        module.MatchAll = true;
        Assert.Equal(["1", "2", "3", "5"], Passing(module, items));
    });

    // ---- 要約の文（D1。AND は「のすべて」） ----

    [Fact]
    public Task 要約は除くと頭に除くが付き_ANDはのすべてで言い分ける() => UiThread.Run(() =>
    {
        var or = Tags(matchAll: false, "A", "B");
        var and = Tags(matchAll: true, "A", "B");
        var single = Tags(matchAll: true, "A");

        Assert.Equal("BOOTHタグ：A・B", or.SummaryText);
        Assert.Equal("BOOTHタグ：A・B のすべて", and.SummaryText);
        Assert.Equal("BOOTHタグ：A", single.SummaryText);

        or.IsExcluded = true;
        and.IsExcluded = true;
        Assert.Equal("除く：BOOTHタグ A・B", or.SummaryText);
        Assert.Equal("除く：BOOTHタグ A・B のすべて", and.SummaryText);
    });

    [Fact]
    public Task 範囲と日付の要約は_除くと頭に除くが付くだけ() => UiThread.Run(() =>
    {
        var range = new RangeModule(SearchModuleKind.WishList, (item, _) => [item.Booth.WishListsCount], string.Empty);
        range.Load(new SearchModuleState { Kind = "WishList", Min = "100", MaxEnabled = false });
        var date = new DateModule(SearchModuleKind.PublishedAt, _ => null);
        date.Load(new SearchModuleState { Kind = "PublishedAt", Min = "2026-01-01", MaxEnabled = false });

        range.IsExcluded = true;
        date.IsExcluded = true;

        Assert.Equal("除く：スキ数 100以上", range.SummaryText);
        Assert.Equal("除く：公開日 2026-01-01から", date.SummaryText);
    });

    // ---- 状態 ----

    [Fact]
    public Task 除くは状態に書かれ_戻すと除いたまま() => UiThread.Run(() =>
    {
        var module = Tags(matchAll: false, "A");
        module.IsExcluded = true;

        var state = module.Save();
        var restored = Tags(matchAll: false);
        restored.Load(state);

        Assert.True(state.Exclude);
        Assert.True(restored.IsExcluded);
        Assert.NotEqual(Tags(matchAll: false, "A").Save().Fingerprint, state.Fingerprint);
    });

    [Fact]
    public Task 三項には除くが無く_状態に書かれていても読まない() => UiThread.Run(() =>
    {
        var module = new ChoiceModule(SearchModuleKind.Favorite,
            [new ChoiceOption("favorite", "お気に入りのみ"), new ChoiceOption("both", "両方")], "both",
            (item, key, _) => key != "favorite" || item.Local.IsFavorite);
        module.Load(new SearchModuleState { Kind = "Favorite", Choice = "favorite", Exclude = true });

        Assert.False(module.SupportsExclude);
        Assert.False(module.IsExcluded);
        module.IsExcluded = true;
        Assert.False(module.IsExcluded);
    });

    [Fact]
    public Task 条件をクリアすると除くも外れる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var search = (await app.StartAsync()).Search;
        search.ShowOnlyFolder(@"D:\files");
        var module = search.Modules.Single(entry => entry.Kind == SearchModuleKind.Path);
        module.IsExcluded = true;
        Assert.Empty(search.ListItems);

        search.ClearFiltersCommand.Execute(null);
        await UiThread.Until(() => !module.IsExcluded, "除くが外れる");
    });
}
