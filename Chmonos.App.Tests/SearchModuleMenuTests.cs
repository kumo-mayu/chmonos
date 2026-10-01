using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索の条件の見出しのメニュー（「…」・右クリック・Shift+F10 で開く物・2026-10-01）と、札「除く」の出し分け。
/// メニューの行は ViewModel のコマンドと値に結んであるので、押したときに何が起きるかをここで確かめる。
/// </summary>
public class SearchModuleMenuTests
{
    /// <summary>「＋ 条件を追加」から足す（人が押すのと同じ口）。</summary>
    internal static SearchModule Add(SearchViewModel search, SearchModuleKind kind)
    {
        var before = search.Modules.ToList();
        search.ModuleMenu.SelectMany(heading => heading.Entries).OfType<SearchModuleMenuEntry>()
            .First(entry => entry.Kind == kind).AddCommand.Execute(null);
        return search.Modules.Except(before).Single();
    }

    private static async Task<SearchViewModel> StartAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        await app.AddItemAsync(Make.Item("1000002", "作り物の髪型").WithFiles());
        return (await app.StartAsync()).Search;
    }

    [Fact]
    public Task 除くを持つのは候補から積む条件と範囲と日付と属性とユーザータグで_三項と最近は持たない() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);

        foreach (var kind in new[] { SearchModuleKind.Category, SearchModuleKind.Price, SearchModuleKind.PublishedAt, SearchModuleKind.Attribute, SearchModuleKind.UserTag, SearchModuleKind.Shop })
        {
            Assert.True(Add(search, kind).SupportsExclude, kind.ToString());
        }

        foreach (var kind in new[] { SearchModuleKind.Owned, SearchModuleKind.Favorite, SearchModuleKind.Recent, SearchModuleKind.Gift })
        {
            Assert.False(Add(search, kind).SupportsExclude, kind.ToString());
        }
    });

    [Fact]
    public Task メニューの除くを押すと_絞り直して当てはまる商品が外れる() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        var path = (ListModule)Add(search, SearchModuleKind.Path);
        path.AddKey(@"D:\files", text: @"D:\files");
        Assert.Equal(["1000001"], search.ListItems.Select(card => card.Item.Id));

        path.ToggleExcludeCommand.Execute(null);

        Assert.True(path.IsExcluded);
        Assert.Equal(["1000002"], search.ListItems.Select(card => card.Item.Id));
        Assert.Equal(@"除く：ファイルの場所 D:\files", search.FilterSummary);

        // 札を押すのも同じコマンド（除くのをやめる）
        path.ToggleExcludeCommand.Execute(null);
        Assert.False(path.IsExcluded);
        Assert.Equal(["1000001"], search.ListItems.Select(card => card.Item.Id));
    });

    [Fact]
    public Task 折りたたむの行は_畳んでいると開くになる() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        var module = Add(search, SearchModuleKind.Category);

        Assert.Equal("折りたたむ", module.CollapseMenuText);
        module.ToggleCollapseCommand.Execute(null);
        Assert.Equal("開く", module.CollapseMenuText);
    });

    [Fact]
    public Task 上へ移動と下へ移動で並びが変わり_端では押せない() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        var category = Add(search, SearchModuleKind.Category);
        var tag = Add(search, SearchModuleKind.BoothTag);
        var price = Add(search, SearchModuleKind.Price);
        SearchModule? focused = null;
        search.ModuleFocusRequested += module => focused = module;

        Assert.False(category.MoveUpCommand!.CanExecute(null));
        Assert.False(price.MoveDownCommand!.CanExecute(null));
        Assert.True(tag.MoveUpCommand!.CanExecute(null));

        tag.MoveUpCommand.Execute(null);

        Assert.Equal([SearchModuleKind.BoothTag, SearchModuleKind.Category, SearchModuleKind.Price], search.Modules.Select(module => module.Kind));
        Assert.Same(tag, focused);
        Assert.False(tag.MoveUpCommand.CanExecute(null));

        category.MoveDownCommand!.Execute(null);
        Assert.Equal([SearchModuleKind.BoothTag, SearchModuleKind.Price, SearchModuleKind.Category], search.Modules.Select(module => module.Kind));
    });

    [Fact]
    public Task 条件を外すと_次の条件に止まり直してもらい_最後なら前の条件() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        var category = Add(search, SearchModuleKind.Category);
        var tag = Add(search, SearchModuleKind.BoothTag);
        var price = Add(search, SearchModuleKind.Price);
        var requests = new List<SearchModule?>();
        search.ModuleFocusRequested += requests.Add;

        tag.RemoveCommand!.Execute(null);
        price.RemoveCommand!.Execute(null);
        category.RemoveCommand!.Execute(null);

        Assert.Equal([price, category, null], requests);
    });

    [Fact]
    public Task 札の吹き出しは_値の分からない商品も外す条件ではそれも言う() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);

        Assert.Equal("当てはまる商品を除いています。押すと除くのをやめます。", Add(search, SearchModuleKind.Category).ExcludedHint);
        Assert.Contains("数の分からない商品", Add(search, SearchModuleKind.Price).ExcludedHint);
        Assert.Contains("日付の分からない商品", Add(search, SearchModuleKind.AcquiredAt).ExcludedHint);
        Assert.Contains("評価していない商品", Add(search, SearchModuleKind.Attribute).ExcludedHint);
    });

    [Fact]
    public Task 除くは前回の状態に残り_開き直しても除いたまま() => TestApp.Run(async app =>
    {
        await app.Services.SettingsStore.UpdateUiStateAsync(state => state with
        {
            SearchModules = [new SearchModuleState { Kind = "Path", Items = [@"D:\files"], Exclude = true }],
        });
        var search = await StartAsync(app);

        var path = Assert.Single(search.Modules);
        Assert.True(path.IsExcluded);
        Assert.Equal(["1000002"], search.ListItems.Select(card => card.Item.Id));
    });
}
