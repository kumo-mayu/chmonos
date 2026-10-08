using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索欄の記法（ユーザ判断 2026-10-08）を検索画面で使ったとき：非表示・R-18 の先に外す扱いとの組み合わせ。
/// 照らし方そのものは Core の <c>SearchConditionTests</c>。
/// </summary>
public class SearchSyntaxTests
{
    private static IEnumerable<string> ShownIds(SearchViewModel search) => search.ListItems.Select(card => card.Item.Id);

    private static ItemRecord Hidden(ItemRecord item) => item with { Local = item.Local with { IsHidden = true } };

    private static ItemRecord Adult(ItemRecord item) => item with { Booth = item.Booth with { IsAdult = true } };

    /// <summary>is:hidden と書いたら、非表示の条件を足さなくても非表示の商品を照らす（書いたのに0件にならない）</summary>
    [Fact]
    public Task Isのhiddenを書くと非表示の商品が出る() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "表示している商品"));
        await app.AddItemAsync(Hidden(Make.Item("1000002", "非表示の商品")));
        var search = (await app.StartAsync()).Search;
        Assert.Equal(["1000001"], ShownIds(search));

        search.QueryText = "is:hidden";
        Assert.Equal(["1000002"], ShownIds(search));
        Assert.Equal("1 件", search.ResultSummary);

        // 除く側に書いたときも非表示の商品は照らされ、外れる
        search.QueryText = "-is:hidden";
        Assert.Equal(["1000001"], ShownIds(search));

        // 書くのをやめたら、今までどおり隠す
        search.QueryText = "商品";
        Assert.Equal(["1000001"], ShownIds(search));
    });

    /// <summary>設定で R-18 を隠しているときは、is:r18 と書いても出さない（設定の方が守り）</summary>
    [Fact]
    public Task R18を隠す設定ではIsのr18でも出ない() => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(current => current with { ShowAdult = false });
        await app.AddItemAsync(Adult(Make.Item("1000001", "R-18の商品")));
        await app.AddItemAsync(Make.Item("1000002", "全年齢の商品"));
        var search = (await app.StartAsync()).Search;

        search.QueryText = "is:r18";

        Assert.Empty(ShownIds(search));
    });

    [Fact]
    public Task 状態と文字を組んで絞れる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "夏の衣装") with { Local = new LocalBlock { IsFavorite = true } });
        await app.AddItemAsync(Make.Item("1000002", "夏の小物"));
        await app.AddItemAsync(Make.Item("1000003", "冬の衣装") with { Local = new LocalBlock { IsFavorite = true } });
        var search = (await app.StartAsync()).Search;

        search.QueryText = "夏 is:favorite";

        Assert.Equal(["1000001"], ShownIds(search));
    });
}
