using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// よその画面から条件付きで検索へ移る入口（<c>ShowOnly*</c>）は、入れ替える前の条件を検索の履歴に積む（ユーザ判断 2026-11-18）。
/// 入口は今の条件を全部戻してから絞るので、積まないと、組んでいた条件へ検索の画面の中から戻る手が無い。
/// </summary>
public class SearchShowOnlyHistoryTests
{
    [Fact]
    public Task 条件付きで入ると_前の条件が履歴の先頭に積まれる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var main = await app.StartAsync();
        var search = main.Search;
        search.QueryText = "衣装";

        search.ShowOnlyCategory("衣装");

        await UiThread.Until(() => search.History.Count == 1, "前の条件が積まれる");
        Assert.Equal("衣装", search.History[0].Entry.Text);
        Assert.Equal(string.Empty, search.QueryText);
    });

    [Fact]
    public Task 同じ条件のまま2回入っても_履歴は1件() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var main = await app.StartAsync();
        var search = main.Search;
        search.QueryText = "衣装";
        search.ShowOnlyBrokenZip();
        await UiThread.Until(() => search.History.Count == 1, "前の条件が積まれる");

        // 2回目は「壊れたzip」が前の条件。もう一度同じ入口から来ても、同じ条件は積まない
        search.ShowOnlyBrokenZip();
        await UiThread.Until(() => search.History.Count == 2, "壊れたzipの条件が積まれる");
        search.ShowOnlyBrokenZip();
        await UiThread.Settle();

        Assert.Equal(2, search.History.Count);
    });

    [Fact]
    public Task 何も絞っていない時に入っても_履歴は積まない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var main = await app.StartAsync();
        var search = main.Search;

        search.ShowRecentlyAddedFirst();
        await UiThread.Settle();

        Assert.Empty(search.History);
    });
}
