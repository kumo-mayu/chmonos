using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 画面と Core が、同じ保存した検索で、手で数えた同じ商品を返すか（試験の点検 2026-11-28 の高1・高2）。
/// 画面の道は保存した検索を呼び出したとき（SearchFilterPass）、Core の道は条件「保存した検索」（SavedSearchMatcher）。
/// API の道は Query.Tests の同じ名前の試験が、同じ作り物（<see cref="SearchParityFixture"/>）と同じ期待で見る。
/// </summary>
public class SearchThreeLayerParityTests
{
    private static string[] Shown(SearchViewModel search) => search.ListItems.Select(card => card.Item.Id).Order(StringComparer.Ordinal).ToArray();

    private static void ClearAll(SearchViewModel search)
    {
        foreach (var module in search.Modules.ToList())
        {
            module.RemoveCommand!.Execute(null);
        }

        search.QueryText = string.Empty;
    }

    [Fact]
    public Task 保存した検索を呼び出した結果と_条件にした結果は_手で数えた商品と同じ() => TestApp.Run(async app =>
    {
        await app.Store.Avatars.SaveAsync(SearchParityFixture.Registry());
        foreach (var item in SearchParityFixture.Items())
        {
            await app.AddItemAsync(item);
        }

        await app.Store.SavedSearches.SaveAsync(SearchParityFixture.SavedSearches());
        var search = (await app.StartAsync()).Search;
        Assert.Equal(6, Shown(search).Length);

        foreach (var @case in SearchParityFixture.Cases())
        {
            ClearAll(search);
            search.SavedRows.Single(row => row.Name == @case.Name).ApplyCommand.Execute(null);
            Assert.True(@case.Expected.SequenceEqual(Shown(search)), $"画面 {@case.Name}: 期待 [{string.Join(",", @case.Expected)}] 実際 [{string.Join(",", Shown(search))}]");

            ClearAll(search);
            var entry = search.ModuleMenu.Single(heading => heading.Title == "保存した検索").Entries.OfType<SearchModuleMenuEntry>().Single(e => e.Label == @case.Name);
            var before = search.Modules.ToList();
            entry.AddCommand.Execute(null);
            var module = (SavedSearchModule)search.Modules.Except(before).Single();
            Assert.False(module.HasProblem, @case.Name);
            Assert.True(@case.Expected.SequenceEqual(Shown(search)), $"Core {@case.Name}: 期待 [{string.Join(",", @case.Expected)}] 実際 [{string.Join(",", Shown(search))}]");
        }
    });
}
