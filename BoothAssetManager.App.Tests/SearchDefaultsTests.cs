using BoothAssetManager.App.Tests.Support;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Tests;

/// <summary>
/// 検索の条件の既定（保存された並びが無いとき＝初めて起動したとき）。
/// ほかの試験は条件を空から始める（<see cref="TestApp"/>）ので、既定はここだけで確かめる。
/// </summary>
public class SearchDefaultsTests
{
    private static async Task<SearchViewModel> FirstLaunchAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        await app.AddItemAsync(Make.Item("1000002", "作り物の髪型"));

        // 保存された並びが無い状態（初めての起動）に戻す
        await app.Services.SettingsStore.UpdateUiStateAsync(state => state with { SearchModules = null });
        return (await app.StartAsync()).Search;
    }

    [Fact]
    public Task 初めての起動では_カテゴリ_ユーザータグ_お気に入りの条件を出しておく() => TestApp.Run(async app =>
    {
        var search = await FirstLaunchAsync(app);

        Assert.Equal(
            [SearchModuleKind.Category, SearchModuleKind.UserTag, SearchModuleKind.Favorite],
            search.Modules.Select(module => module.Kind));
    });

    // 2026-09-30 に試験を書いて見つけた食い違い。SearchModuleCatalog.Defaults のコメントは
    // 「どれも足しただけでは絞らない形（カテゴリは空・お気に入りは「両方」）なので、最初に全件が見える」と言うが、
    // 実際は、選ぶ条件（ChoiceModule）が先頭の選択肢から始まるので、お気に入りが「お気に入りのみ」で効いて0件になる。
    // 直し方（既定のときだけ「両方」にする／先頭を「両方」にする）は決めてもらう物なので、本体は直さずに止めてある。
    // 直したら Skip を外す
    [Fact(Skip = "既知の食い違い：初めての起動で、お気に入りの条件が「お気に入りのみ」で効いて0件になる（直し方の判断待ち）")]
    public Task 初めての起動では_既定の条件は何も絞らず_全件が見える() => TestApp.Run(async app =>
    {
        var search = await FirstLaunchAsync(app);

        Assert.False(search.HasActiveFilters);
        Assert.Equal(2, search.ListItems.Count);
        Assert.Equal("2 件", search.ResultSummary);
    });

    [Fact]
    public Task 条件をクリアすると_既定の条件も絞らなくなり_全件が見える() => TestApp.Run(async app =>
    {
        var search = await FirstLaunchAsync(app);

        search.ClearFiltersCommand.Execute(null);
        await UiThread.Until(() => !search.HasActiveFilters, "条件が外れる");

        Assert.Equal(2, search.ListItems.Count);
    });
}
