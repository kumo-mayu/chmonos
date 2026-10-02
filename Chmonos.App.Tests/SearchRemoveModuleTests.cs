using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 条件を消したときのもたつき（メモ2-① 2026-10-02・`docs/research/search-modules-2026-10-01.md` §13）。
/// 重かったのは照らし直しではなく、結果が変わらないのにリストへ新しい並びを渡して行を組み直していたこと、
/// カードで出している間も隠れたリストへ渡していたこと。画面へ渡す並びが同じ入れ物のままかを確かめる。
/// </summary>
public class SearchRemoveModuleTests
{
    private static ItemRecord Tagged(string id, params string[] tags)
        => Make.Item(id, "作り物") with { Booth = Make.Item(id, "作り物").Booth with { Tags = tags } };

    private static async Task<SearchViewModel> StartAsync(TestApp app)
    {
        await app.AddItemAsync(Tagged("1000001", "夏"));
        await app.AddItemAsync(Tagged("1000002", "冬"));
        await app.AddItemAsync(Tagged("1000003", "夏", "冬"));
        return (await app.StartAsync()).Search;
    }

    [Fact]
    public Task カードで出している間はリストに空を渡し_リストに切り替えると結果の並びを渡す() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        search.IsListMode = false;

        Assert.Empty(search.ListViewItems);

        search.IsListMode = true;

        Assert.Same(search.DisplayItems, search.ListViewItems);
        Assert.Equal(3, search.ListViewItems.Count);
    });

    [Fact]
    public Task 何も絞っていない条件を消しても_画面に渡す並びは同じ入れ物のまま() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        search.IsListMode = true;
        var tags = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.BoothTag);
        tags.AddKey("夏");
        var empty = SearchModuleMenuTests.Add(search, SearchModuleKind.Category);
        var before = search.DisplayItems;

        empty.RemoveCommand!.Execute(null);

        Assert.Same(before, search.DisplayItems);
        Assert.Equal(2, search.DisplayItems.Count);
    });

    [Fact]
    public Task 絞っている条件を消すと_広がった並びを渡す() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        search.IsListMode = true;
        var tags = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.BoothTag);
        tags.AddKey("夏");
        var before = search.DisplayItems;

        tags.RemoveCommand!.Execute(null);

        Assert.NotSame(before, search.DisplayItems);
        Assert.Equal(3, search.DisplayItems.Count);
    });
}
