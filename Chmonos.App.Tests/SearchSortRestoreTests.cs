using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Xunit;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索の表示順を次の起動に引き継ぐ（ユーザ指示 2026-11-03。前は起動のたびに入手日が新しい順へ戻った）。
/// </summary>
public class SearchSortRestoreTests
{
    private static async Task<SearchViewModel> StartAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物のい"));
        await app.AddItemAsync(Make.Item("1000002", "作り物のあ"));
        await app.AddItemAsync(Make.Item("1000003", "作り物のう"));
        return (await app.StartAsync()).Search;
    }

    private static string[] Names(SearchViewModel search) => search.ListItems.Select(card => card.Item.DisplayName).ToArray();

    [Fact]
    public Task 表示順を変えると_項目と向きを覚える() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);

        search.SortField = search.SortFields.First(field => field.Kind == SortKind.Name);
        search.SortsDescending = true;

        await UiThread.Until(
            () => app.Services.SettingsStore.UiState.SearchSort is { Kind: "Name", Descending: true, AttributeName: null },
            "書く");
    });

    [Fact]
    public Task 前回の表示順で始まる() => TestApp.Run(async app =>
    {
        await app.Services.SettingsStore.UpdateUiStateAsync(state => state with
        {
            SearchSort = new SearchSortState { Kind = "Name", Descending = false },
        });

        var search = await StartAsync(app);

        Assert.Equal(SortKind.Name, search.SortField.Kind);
        Assert.True(search.SortsAscending);
        Assert.Equal(["作り物のあ", "作り物のい", "作り物のう"], Names(search));
    });

    [Fact]
    public Task 消した属性で並べていたら_既定のカテゴリのBOOTHの並びに戻る() => TestApp.Run(async app =>
    {
        await app.Services.SettingsStore.UpdateUiStateAsync(state => state with
        {
            SearchSort = new SearchSortState { Kind = "Attribute", AttributeName = "消した属性", Descending = true },
        });

        var search = await StartAsync(app);

        // 向きは既定に戻す。消した属性の「高い順」を引き継ぐと「BOOTHの逆順」になる
        Assert.Equal(SortKind.Category, search.SortField.Kind);
        Assert.True(search.SortsAscending);
    });

    [Fact]
    public Task 知らない項目なら_既定で始まる() => TestApp.Run(async app =>
    {
        await app.Services.SettingsStore.UpdateUiStateAsync(state => state with
        {
            SearchSort = new SearchSortState { Kind = "手で書いた知らない項目" },
        });

        var search = await StartAsync(app);

        Assert.Equal(SortKind.Category, search.SortField.Kind);
        Assert.True(search.SortsAscending);
    });

    [Fact]
    public Task 初めては_カテゴリのBOOTHの並びで始まる() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);

        Assert.Equal(SortKind.Category, search.SortField.Kind);
        Assert.True(search.SortsAscending);
        Assert.Equal(SearchViewModel.DefaultSort.Label, search.Sort.Label);
    });

    /// <summary>前の既定（入手日の新しい順）を選んでも、既定でなくなったので書く（書かないと次の起動でカテゴリへ戻る）</summary>
    [Fact]
    public Task 入手日の新しい順を選ぶと_書く() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);

        search.SortField = search.SortFields.First(field => field.Kind == SortKind.AcquiredAt);
        search.SortsDescending = true;

        await UiThread.Until(
            () => app.Services.SettingsStore.UiState.SearchSort is { Kind: "AcquiredAt", Descending: true, AttributeName: null },
            "書く");
    });

    [Fact]
    public Task 起動しただけでは_書かない() => TestApp.Run(async app =>
    {
        await StartAsync(app);

        await app.SettleAsync();

        Assert.Null(app.Services.SettingsStore.UiState.SearchSort);
    });
}
