using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 選んだ商品をまとめて既読にする（ユーザ判断 2026-10-02「4は入れましょう」）。帯の「既読にする」と、選んでいる最中の右クリックの
/// 「選んだ商品を既読にする」は同じ命令（<c>MarkSelectionReadCommand</c>）で、選んだ物のうち未読の更新がある商品の知らせを1回の命令で既読にする。
/// </summary>
public class SearchMarkSelectionReadTests
{
    private static NotificationRecord Updated(string id, string itemId) => new()
    {
        Id = id,
        Kind = NotificationKind.ItemUpdated,
        Title = "作り物の更新",
        Detail = string.Empty,
        ItemId = itemId,
        CreatedAt = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.FromHours(9)),
    };

    // 1000001 に未読2件、1000002 に未読1件、1000003 と 1000004 は更新なし
    private static async Task<MainViewModel> StartAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装", shop: "sample-shop"));
        await app.AddItemAsync(Make.Item("1000002", "作り物の髪型", shop: "sample-shop"));
        await app.AddItemAsync(Make.Item("1000003", "作り物の靴", shop: "sample-shop"));
        await app.AddItemAsync(Make.Item("1000004", "作り物の小物", shop: "sample-shop"));
        await app.Store.Notifications.SaveAsync([Updated("n1", "1000001"), Updated("n2", "1000001"), Updated("n3", "1000002")]);
        return await app.StartAsync();
    }

    private static ItemCardViewModel Card(SearchViewModel search, string id) => search.ListItems.Single(card => card.Item.Id == id);

    [Fact]
    public Task 選んだ物に未読の更新が無ければ出さず_1件でもあれば出す() => TestApp.Run(async app =>
    {
        var search = (await StartAsync(app)).Search;

        Assert.False(search.HasSelectedUpdates);
        Card(search, "1000003").IsSelected = true;
        Assert.False(search.HasSelectedUpdates);
        Assert.False(search.MarkSelectionReadCommand.CanExecute(null));

        Card(search, "1000002").IsSelected = true;
        Assert.True(search.HasSelectedUpdates);
        Assert.True(search.MarkSelectionReadCommand.CanExecute(null));
    });

    [Fact]
    public Task まとめて既読にすると_選んだ物の知らせだけを1回の命令で既読にし_札と条件とナビの数が合う() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        var search = main.Search;
        SearchModuleMenuTests.Add(search, SearchModuleKind.Updated);
        Assert.Equal(["1000001", "1000002"], search.ListItems.Select(card => card.Item.Id).Order());
        await UiThread.Until(() => main.UnreadCount == 3, "ナビの要確認の数を読む");

        Card(search, "1000001").IsSelected = true;
        Card(search, "1000002").IsSelected = true;
        var first = Card(search, "1000001");
        var second = Card(search, "1000002");
        var before = search.MarkReadCommandCount;

        search.MarkSelectionReadCommand.Execute(null);
        await UiThread.Until(() => !first.HasUpdate && !second.HasUpdate, "札が下りる");
        await app.SettleAsync();

        Assert.Equal(1, search.MarkReadCommandCount - before);
        Assert.All(app.Store.Notifications.Load(), record => Assert.True(record.IsRead));
        Assert.False(search.HasSelectedUpdates);
        Assert.Empty(search.ListItems);
        await UiThread.Until(() => main.UnreadCount == 0, "ナビの要確認の数が減る");
    });

    [Fact]
    public Task 選んでいない商品の知らせは既読にしない() => TestApp.Run(async app =>
    {
        var search = (await StartAsync(app)).Search;

        Card(search, "1000001").IsSelected = true;
        Card(search, "1000004").IsSelected = true;
        search.MarkSelectionReadCommand.Execute(null);
        await UiThread.Until(() => !Card(search, "1000001").HasUpdate, "札が下りる");
        await app.SettleAsync();

        var records = app.Store.Notifications.Load().ToDictionary(record => record.Id);
        Assert.True(records["n1"].IsRead);
        Assert.True(records["n2"].IsRead);
        Assert.False(records["n3"].IsRead);
        Assert.True(Card(search, "1000002").HasUpdate);
    });

    [Fact]
    public Task 選んだカードの札がほかの所で下りると_まとめの既読も引っ込む() => TestApp.Run(async app =>
    {
        var search = (await StartAsync(app)).Search;
        var card = Card(search, "1000002");
        card.IsSelected = true;
        Assert.True(search.HasSelectedUpdates);
        var told = 0;
        search.PropertyChanged += (_, e) => told += e.PropertyName == nameof(SearchViewModel.HasSelectedUpdates) ? 1 : 0;

        // 右クリックの「既読にする」（1件）で、選んだ物に未読の更新が無くなった。帯のボタンが消えるには、変わったと知らせる必要がある
        await search.MarkUpdatesReadAsync(card);

        Assert.False(search.HasSelectedUpdates);
        Assert.True(told > 0);
    });

    [Fact]
    public Task ショップの中でも選んだ物をまとめて既読にできる() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        await main.ShowShopAsync("sample-shop");
        var shop = Assert.IsType<ShopViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => shop.IsListReady, "ショップの商品を読む");
        var cards = shop.ListItems.OfType<ItemCardViewModel>().ToList();

        foreach (var card in cards)
        {
            card.IsSelected = true;
        }

        Assert.True(shop.HasSelectedUpdates);
        var before = main.Search.MarkReadCommandCount;
        shop.MarkSelectionReadCommand.Execute(null);
        await UiThread.Until(() => cards.All(card => !card.HasUpdate), "札が下りる");
        await app.SettleAsync();

        Assert.Equal(1, main.Search.MarkReadCommandCount - before);
        Assert.All(app.Store.Notifications.Load(), record => Assert.True(record.IsRead));
        Assert.False(shop.HasSelectedUpdates);
    });
}
