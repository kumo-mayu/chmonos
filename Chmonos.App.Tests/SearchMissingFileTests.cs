using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 条件「見つからないファイル」（ユーザ判断 2026-10-04）。記録の上では持っているが、置き場がどこにも無いファイルがある商品。
/// 数え方はカードとリストの印（<see cref="ItemCardViewModel.HasMissingFile"/>）と同じ式（<see cref="ItemRecord.HasMissingFile"/>）。
/// </summary>
public class SearchMissingFileTests
{
    private static LocalFileRecord Gone(string path, bool detached = false)
        => Make.File(path, detached: detached) with { Paths = [] };

    private static async Task<MainViewModel> StartAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("1000001", "置き場の無い商品").WithFiles(Gone(@"D:\files\a.zip")));
        await app.AddItemAsync(Make.Item("1000002", "一部だけ無い商品").WithFiles(
            Make.File(@"D:\files\b.zip"), Gone(@"D:\files\b2.zip")));
        await app.AddItemAsync(Make.Item("1000003", "置き場のある商品").WithFiles(Make.File(@"D:\files\c.zip")));
        await app.AddItemAsync(Make.Item("1000004", "外したファイルだけ無い商品").WithFiles(
            Make.File(@"D:\files\d.zip"), Gone(@"D:\files\d2.zip", detached: true)));
        await app.AddItemAsync(Make.Item("1000005", "ファイルの無い商品").WithFiles());
        return await app.StartAsync();
    }

    // 人が選ぶのと同じ口（Selected の set が絞り直しを起こす。Select は他の画面から渡す口で、絞り直しは呼ぶ側）
    private static void Pick(ChoiceModule module, string key) => module.Selected = module.Options.Single(option => option.Key == key);

    private static IEnumerable<string> ShownIds(SearchViewModel search) => search.ListItems.Select(card => card.Item.Id).Order().ToList();

    [Fact]
    public Task ある_を選ぶと_置き場の無いファイルを持つ商品だけが並ぶ() => TestApp.Run(async app =>
    {
        var search = (await StartAsync(app)).Search;
        var module = (ChoiceModule)SearchModuleMenuTests.Add(search, SearchModuleKind.MissingFile);

        Pick(module, "missing");
        Assert.Equal(["1000001", "1000002"], ShownIds(search));

        Pick(module, "none");
        Assert.Equal(["1000003", "1000004", "1000005"], ShownIds(search));

        Pick(module, "both");
        Assert.Equal(5, search.ListItems.Count);
    });

    [Fact]
    public Task カードの印と条件は_同じ商品を指す() => TestApp.Run(async app =>
    {
        var search = (await StartAsync(app)).Search;
        var module = (ChoiceModule)SearchModuleMenuTests.Add(search, SearchModuleKind.MissingFile);
        Pick(module, "both");

        var marked = search.ListItems.Where(card => card.HasMissingFile).Select(card => card.Item.Id).Order().ToList();
        Pick(module, "missing");

        Assert.Equal(marked, ShownIds(search));
    });

    [Fact]
    public Task 件数と要約が出る() => TestApp.Run(async app =>
    {
        var search = (await StartAsync(app)).Search;
        var module = (ChoiceModule)SearchModuleMenuTests.Add(search, SearchModuleKind.MissingFile);

        Pick(module, "missing");

        Assert.Equal([2, 3, 5], module.Options.Select(option => option.Count));
        Assert.Equal("見つからないファイル：見つからないファイルがある", module.SummaryText);
    });

    [Fact]
    public Task 条件は保存され_読み直しても同じ絞り込みになる() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        var module = (ChoiceModule)SearchModuleMenuTests.Add(main.Search, SearchModuleKind.MissingFile);
        Pick(module, "missing");
        await main.FlushPendingWritesAsync();

        var saved = await app.Services.SettingsStore.UpdateUiStateAsync(state => state);
        var state = Assert.Single(saved.SearchModules!, state => state.Kind == nameof(SearchModuleKind.MissingFile));
        Assert.Equal("missing", state.Choice);

        // 保存した並びを読み込む側（起動）で同じ条件が戻る
        var restored = (await app.StartAsync()).Search;
        var back = Assert.IsType<ChoiceModule>(Assert.Single(restored.Modules, m => m.Kind == SearchModuleKind.MissingFile));
        Assert.Equal("missing", back.SelectedKey);
        Assert.Equal(["1000001", "1000002"], ShownIds(restored));
    });

    [Fact]
    public Task 置き場が付いた後に読み直すと_条件の結果から外れる() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        var module = (ChoiceModule)SearchModuleMenuTests.Add(main.Search, SearchModuleKind.MissingFile);
        Pick(module, "missing");
        Assert.Equal(["1000001", "1000002"], ShownIds(main.Search));

        // 「見つからないファイルを探す」や取り込みが置き場を結び直した後と同じ記録の変わり方（パスが入る）
        await app.Store.Items.ChangeLocalAsync(
            "1000001",
            current => current with { LocalFiles = [.. current.LocalFiles.Select(file => file with { Paths = [@"D:\files\moved.zip"] })] },
            LocalOwners.Import);
        await main.ReloadLibraryAsync();
        await app.SettleAsync();

        Assert.Equal(["1000002"], ShownIds(main.Search));
    });

    [Fact]
    public void 条件は壊れたzipのすぐ後に出る()
    {
        var group = SearchModuleCatalog.Menu
            .Single(layout => layout.Title == SearchModuleCatalog.ItemInfo)
            .Groups.Single(kinds => kinds.Contains(SearchModuleKind.MissingFile))
            .ToList();

        Assert.Equal(group.IndexOf(SearchModuleKind.BrokenZip) + 1, group.IndexOf(SearchModuleKind.MissingFile));
        Assert.False(SearchModuleCatalog.Of(SearchModuleKind.MissingFile).AllowsMany);
    }
}
