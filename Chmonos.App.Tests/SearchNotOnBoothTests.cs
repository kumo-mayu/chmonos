using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 条件「BOOTHに無い商品」（ユーザ指示 2026-10-04 メモ31）。仮のID（local-…）で登録した商品を絞る。
/// 判定は <see cref="ItemRecord.IsLocalOnly"/>（BOOTHへ問い合わせない・「BOOTHで開く」を出さない判断と同じ式）。
/// </summary>
public class SearchNotOnBoothTests
{
    private static async Task<MainViewModel> StartAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("1000001", "BOOTHの商品A"));
        await app.AddItemAsync(Make.Item(LocalItemId.For("abcdef0123456789"), "BOOTHに無い商品A"));
        await app.AddItemAsync(Make.Item("1000003", "BOOTHの商品B"));
        await app.AddItemAsync(Make.Item(LocalItemId.For("0123456789abcdef"), "BOOTHに無い商品B"));
        await app.AddItemAsync(Make.Item("1000005", "BOOTHの商品C"));
        return await app.StartAsync();
    }

    private static void Pick(ChoiceModule module, string key) => module.Selected = module.Options.Single(option => option.Key == key);

    private static List<string> ShownNames(SearchViewModel search) => [.. search.ListItems.Select(card => card.Item.Booth.Name ?? string.Empty).Order()];

    [Fact]
    public Task 三つの選択肢で_仮のIDの商品だけ_BOOTHの商品だけ_両方が並ぶ() => TestApp.Run(async app =>
    {
        var search = (await StartAsync(app)).Search;
        var module = (ChoiceModule)SearchModuleMenuTests.Add(search, SearchModuleKind.NotOnBooth);

        Pick(module, "local");
        Assert.Equal(["BOOTHに無い商品A", "BOOTHに無い商品B"], ShownNames(search));

        Pick(module, "booth");
        Assert.Equal(["BOOTHの商品A", "BOOTHの商品B", "BOOTHの商品C"], ShownNames(search));

        Pick(module, "both");
        Assert.Equal(5, search.ListItems.Count);

        Assert.Equal(["BOOTHに無い商品だけ", "BOOTHの商品だけ", "両方"], module.Options.Select(option => option.Label));
    });

    [Fact]
    public Task 件数と要約が出る() => TestApp.Run(async app =>
    {
        var search = (await StartAsync(app)).Search;
        var module = (ChoiceModule)SearchModuleMenuTests.Add(search, SearchModuleKind.NotOnBooth);

        Pick(module, "local");

        Assert.Equal([2, 3, 5], module.Options.Select(option => option.Count));
        Assert.Equal("BOOTHに無い商品：BOOTHに無い商品だけ", module.SummaryText);
    });

    [Fact]
    public Task 条件は保存され_読み直しても同じ絞り込みになる() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        var module = (ChoiceModule)SearchModuleMenuTests.Add(main.Search, SearchModuleKind.NotOnBooth);
        Pick(module, "local");
        await main.FlushPendingWritesAsync();

        var saved = await app.Services.SettingsStore.UpdateUiStateAsync(state => state);
        var state = Assert.Single(saved.SearchModules!, state => state.Kind == nameof(SearchModuleKind.NotOnBooth));
        Assert.Equal("local", state.Choice);

        var restored = (await app.StartAsync()).Search;
        var back = Assert.IsType<ChoiceModule>(Assert.Single(restored.Modules, m => m.Kind == SearchModuleKind.NotOnBooth));
        Assert.Equal("local", back.SelectedKey);
        Assert.Equal(["BOOTHに無い商品A", "BOOTHに無い商品B"], ShownNames(restored));
    });

    [Fact]
    public void メニューでは_商品の情報の群で_ギフトのすぐ後に並ぶ()
    {
        var info = SearchModuleCatalog.Menu.Single(layout => layout.Title == SearchModuleCatalog.ItemInfo);
        var group = info.Groups.Single(kinds => kinds.Contains(SearchModuleKind.NotOnBooth));

        Assert.Equal(group.ToList().IndexOf(SearchModuleKind.Gift) + 1, group.ToList().IndexOf(SearchModuleKind.NotOnBooth));
        Assert.False(SearchModuleCatalog.Of(SearchModuleKind.NotOnBooth).AllowsMany);
    }
}
