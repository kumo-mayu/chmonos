using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 同じ種類の条件を複数置く（ユーザ判断 2026-10-01・`docs/research/search-modules-2026-10-01.md` §2・§5・§8）。
/// 足す位置・メニューのグレー・前回の状態と検索の履歴から戻す・ほかの画面からの入口・読み上げの名前と ID・隣に並べる。
/// </summary>
public class SearchModuleManyTests
{
    private static SearchModule Add(SearchViewModel search, SearchModuleKind kind) => SearchModuleMenuTests.Add(search, kind);

    private static SearchModuleKind[] Kinds(SearchViewModel search) => search.Modules.Select(module => module.Kind).ToArray();

    private static ItemRecord Categorized(string id, string category)
        => Make.Item(id, "作り物") with { Local = Make.Item(id, "作り物").Local with { Category = category } };

    private static async Task<SearchViewModel> StartAsync(TestApp app)
    {
        await app.AddItemAsync(Categorized("1000001", "衣装"));
        await app.AddItemAsync(Categorized("1000002", "髪型"));
        await app.AddItemAsync(Categorized("1000003", "小物"));
        return (await app.StartAsync()).Search;
    }

    private static string[] Shown(SearchViewModel search) => search.ListItems.Select(card => card.Item.Id).Order(StringComparer.Ordinal).ToArray();

    // ---- 並びの決まり（純粋な関数） ----

    [Fact]
    public void 足す位置は既定で一番下_設定を入れると同じ種類の一番上の塊の直後()
    {
        SearchModuleKind[] kinds = [SearchModuleKind.BoothTag, SearchModuleKind.Avatar, SearchModuleKind.BoothTag, SearchModuleKind.Category];

        Assert.Equal(4, SearchModuleOrder.InsertIndex(kinds, SearchModuleKind.BoothTag, nearSameKind: false));
        Assert.Equal(1, SearchModuleOrder.InsertIndex(kinds, SearchModuleKind.BoothTag, nearSameKind: true));
        Assert.Equal(2, SearchModuleOrder.InsertIndex(kinds, SearchModuleKind.Avatar, nearSameKind: true));
        Assert.Equal(4, SearchModuleOrder.InsertIndex(kinds, SearchModuleKind.Path, nearSameKind: true));
        Assert.Equal(0, SearchModuleOrder.InsertIndex([], SearchModuleKind.Path, nearSameKind: true));
    }

    [Fact]
    public void 隣に並べると_種類ごとに初めて出た順で_同じ種類は元の順のまま寄る()
    {
        SearchModuleKind[] kinds = [SearchModuleKind.BoothTag, SearchModuleKind.Avatar, SearchModuleKind.BoothTag, SearchModuleKind.Category, SearchModuleKind.Avatar];

        Assert.Equal([0, 2, 1, 4, 3], SearchModuleOrder.GroupByKind(kinds));
        Assert.False(SearchModuleOrder.IsGrouped(kinds));
        Assert.True(SearchModuleOrder.IsGrouped([SearchModuleKind.BoothTag, SearchModuleKind.BoothTag, SearchModuleKind.Category]));
        Assert.True(SearchModuleOrder.IsGrouped([]));
    }

    // ---- 足す ----

    [Fact]
    public Task 候補から積む条件は2つ目を足せ_1つまでの条件はメニューでグレーになる() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        var entries = search.ModuleMenu.SelectMany(heading => heading.Entries).OfType<SearchModuleMenuEntry>().ToList();

        Add(search, SearchModuleKind.Category);
        Add(search, SearchModuleKind.Category);
        Add(search, SearchModuleKind.Shop);

        Assert.Equal([SearchModuleKind.Category, SearchModuleKind.Category, SearchModuleKind.Shop], Kinds(search));
        Assert.True(entries.First(entry => entry.Kind == SearchModuleKind.Category).IsAvailable);
        Assert.False(entries.First(entry => entry.Kind == SearchModuleKind.Shop).IsAvailable);
        Assert.True(entries.First(entry => entry.Kind == SearchModuleKind.UserTag).IsAvailable);
    });

    [Fact]
    public Task 設定を入れると_足した条件は同じ種類のすぐ下に入る() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        Add(search, SearchModuleKind.BoothTag);
        Add(search, SearchModuleKind.Avatar);
        Add(search, SearchModuleKind.BoothTag);
        await app.ChangeSettingsAsync(settings => settings with { PlaceNewConditionNearSameKind = true });

        var added = Add(search, SearchModuleKind.BoothTag);

        Assert.Equal([SearchModuleKind.BoothTag, SearchModuleKind.BoothTag, SearchModuleKind.Avatar, SearchModuleKind.BoothTag], Kinds(search));
        Assert.Same(added, search.Modules[1]);
    });

    [Fact]
    public Task 同じ種類の2つ目から_読み上げの名前とIDに番号が付き_並びで振り直る() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        var first = (ListModule)Add(search, SearchModuleKind.Category);
        var second = (ListModule)Add(search, SearchModuleKind.Category);
        var avatar1 = (ListModule)Add(search, SearchModuleKind.Avatar);
        var avatar2 = (ListModule)Add(search, SearchModuleKind.Avatar);

        Assert.Equal(("Category", "カテゴリ", "カテゴリで絞り込む"), (first.IdKey, first.SpokenLabel, first.InputName));
        Assert.Equal(("Category-2", "カテゴリ（2つ目）", "カテゴリ（2つ目）で絞り込む"), (second.IdKey, second.SpokenLabel, second.InputName));
        Assert.Equal("名前、ID、素体で絞り込む", avatar1.InputName);
        Assert.Equal("名前、ID、素体で絞り込む（2つ目）", avatar2.InputName);

        // 見出しに見える文字は番号を付けない（動かすと替わって紛らわしい）
        Assert.Equal("カテゴリ", second.Label);

        first.RemoveCommand!.Execute(null);
        Assert.Equal(("Category", "カテゴリ"), (second.IdKey, second.SpokenLabel));
    });

    [Fact]
    public Task 同じ種類の2つで_含む条件と除く条件を組める() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        var include = (ListModule)Add(search, SearchModuleKind.Category);
        include.AddKey("衣装");
        include.AddKey("髪型");
        var exclude = (ListModule)Add(search, SearchModuleKind.Category);
        exclude.AddKey("髪型");
        exclude.IsExcluded = true;

        Assert.Equal(["1000001"], Shown(search));
        Assert.Equal("カテゴリ：衣装・髪型 / 除く：カテゴリ 髪型", search.FilterSummary);
    });

    // ---- 戻す ----

    [Fact]
    public Task 前回の状態は同じ種類の2つ目も戻し_1つまでの種類の2つ目は飛ばす() => TestApp.Run(async app =>
    {
        await app.Services.SettingsStore.UpdateUiStateAsync(state => state with
        {
            SearchModules =
            [
                new SearchModuleState { Kind = "Category", Items = ["衣装", "髪型"] },
                new SearchModuleState { Kind = "Favorite", Choice = "both" },
                new SearchModuleState { Kind = "Category", Items = ["髪型"], Exclude = true },
                new SearchModuleState { Kind = "Favorite", Choice = "favorite" },
            ],
        });
        var search = await StartAsync(app);

        Assert.Equal([SearchModuleKind.Category, SearchModuleKind.Favorite, SearchModuleKind.Category], Kinds(search));
        Assert.True(search.Modules[2].IsExcluded);
        Assert.Equal("both", ((ChoiceModule)search.Modules[1]).SelectedKey);
        Assert.Equal(["1000001"], Shown(search));
    });

    [Fact]
    public Task 履歴から戻すと_同じ種類のn番目の状態はn番目の条件へ入り_足りなければ足す() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        var only = (ListModule)Add(search, SearchModuleKind.Category);
        only.AddKey("小物");

        search.RestoreFilters(new SearchHistoryEntry
        {
            Modules =
            [
                new SearchModuleState { Kind = "Category", Items = ["衣装", "髪型"] },
                new SearchModuleState { Kind = "Category", Items = ["髪型"], Exclude = true },
            ],
        });

        Assert.Equal([SearchModuleKind.Category, SearchModuleKind.Category], Kinds(search));
        Assert.Equal(["衣装", "髪型"], ((ListModule)search.Modules[0]).Chips.Select(chip => chip.Key));
        Assert.True(search.Modules[1].IsExcluded);
        Assert.Equal(["1000001"], Shown(search));

        // 控えた絞り込み（画面の履歴の P4）も同じ形なので、取って戻すと同じになる
        var captured = search.CaptureFilters();
        search.ClearFiltersCommand.Execute(null);
        search.RestoreFilters(captured);
        Assert.Equal(["1000001"], Shown(search));
        Assert.Equal(2, search.Modules.Count);
    });

    [Fact]
    public Task ほかの画面からの入口は_いちばん上の同じ種類に入れ_2つ目は空にして残す() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        var first = (ListModule)Add(search, SearchModuleKind.Category);
        var second = (ListModule)Add(search, SearchModuleKind.Category);
        second.AddKey("小物");
        second.IsExcluded = true;

        search.ShowOnlyCategory("衣装");

        Assert.Equal(2, search.Modules.Count);
        Assert.Equal(["衣装"], first.Chips.Select(chip => chip.Key));
        Assert.Empty(second.Chips);
        Assert.False(second.IsExcluded);
        Assert.Equal(["1000001"], Shown(search));
    });

    // ---- 隣に並べる ----

    [Fact]
    public Task 隣に並べると並びが寄り_結果は変わらず_並びは状態に残る() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        var tag1 = Add(search, SearchModuleKind.BoothTag);
        var avatar1 = Add(search, SearchModuleKind.Avatar);
        var tag2 = Add(search, SearchModuleKind.BoothTag);
        var category = (ListModule)Add(search, SearchModuleKind.Category);
        category.AddKey("衣装");
        var avatar2 = Add(search, SearchModuleKind.Avatar);
        var before = Shown(search);
        Assert.True(search.GroupModulesCommand.CanExecute(null));

        search.GroupModulesCommand.Execute(null);

        Assert.Equal([tag1, tag2, avatar1, avatar2, category], search.Modules);
        Assert.Equal(before, Shown(search));
        Assert.False(search.GroupModulesCommand.CanExecute(null));
        Assert.Equal("BoothTag-2", tag2.IdKey);

        await app.Main.FlushPendingWritesAsync();
        var saved = await app.Services.SettingsStore.UpdateUiStateAsync(state => state);
        Assert.Equal(["BoothTag", "BoothTag", "Avatar", "Avatar", "Category"], saved.SearchModules!.Select(state => state.Kind));
    });
}
