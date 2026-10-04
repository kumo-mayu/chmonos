using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 未読の「商品の更新」を検索・フォルダのカードに出す・条件「更新あり」・右クリックの「既読にする」（ユーザ判断・指示 2026-10-02）。
/// 数え方はショップの画面の「更新あり」と同じ（通知に未読で、片付けていない商品の更新）。
/// </summary>
public class SearchUpdatedTests
{
    private static NotificationRecord Updated(string id, string itemId, bool isRead = false, bool isResolved = false) => new()
    {
        Id = id,
        Kind = NotificationKind.ItemUpdated,
        Title = "作り物の更新",
        Detail = string.Empty,
        ItemId = itemId,
        IsRead = isRead,
        IsResolved = isResolved,
        CreatedAt = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.FromHours(9)),
    };

    private static async Task<MainViewModel> StartAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        await app.AddItemAsync(Make.Item("1000002", "作り物の髪型"));
        await app.AddItemAsync(Make.Item("1000003", "作り物の靴"));
        await app.AddItemAsync(Make.Item("1000004", "作り物の小物"));
        await app.Store.Notifications.SaveAsync(
        [
            Updated("n1", "1000001"),
            Updated("n2", "1000001"),
            Updated("n3", "1000002", isRead: true),
            Updated("n4", "1000003", isResolved: true),
        ]);
        return await app.StartAsync();
    }

    private static ItemCardViewModel Card(SearchViewModel search, string id) => search.ListItems.Single(card => card.Item.Id == id);

    [Fact]
    public Task 検索のカードは_未読で片付けていない更新がある商品にだけ札を出す() => TestApp.Run(async app =>
    {
        var search = (await StartAsync(app)).Search;

        Assert.True(Card(search, "1000001").HasUpdate);
        Assert.True(Card(search, "1000001").CanMarkUpdateRead);
        Assert.NotNull(Card(search, "1000001").ShowUpdateCommand);
        Assert.False(Card(search, "1000002").HasUpdate);
        Assert.False(Card(search, "1000003").HasUpdate);
        Assert.False(Card(search, "1000004").CanMarkUpdateRead);
    });

    [Fact]
    public Task フォルダの右の欄のカードにも札を出し_ほかの画面へ渡すカードには出さない() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        var item = main.Search.FindItem("1000001")!;

        Assert.True(main.Search.CreateCardWithUpdates(item).HasUpdate);
        Assert.False(main.Search.CreateCard(item).HasUpdate);
        Assert.False(main.Search.CardFor("1000001")!.HasUpdate);
    });

    [Fact]
    public Task 既読にすると_知らせが既読になり_札が下りてナビの通知の数も減る() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        var search = main.Search;
        var folderCard = search.CreateCardWithUpdates(search.FindItem("1000001")!);
        await UiThread.Until(() => main.UnreadCount == 2, "ナビの通知の数を読む");

        Card(search, "1000001").MarkUpdateReadCommand!.Execute(null);
        await UiThread.Until(() => !Card(search, "1000001").HasUpdate, "札が下りる");
        await app.SettleAsync();

        Assert.All(app.Store.Notifications.Load().Where(record => record.ItemId == "1000001"), record => Assert.True(record.IsRead));
        Assert.False(folderCard.HasUpdate);
        Assert.False(Card(search, "1000001").CanMarkUpdateRead);
        await UiThread.Until(() => main.UnreadCount == 0, "ナビの通知の数が減る");
    });

    [Fact]
    public Task 条件の更新ありで絞れ_既読にすると外れる() => TestApp.Run(async app =>
    {
        var search = (await StartAsync(app)).Search;
        var module = (ChoiceModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Updated);

        Assert.Equal("updated", module.Selected.Key);
        Assert.Equal(["1000001"], search.ListItems.Select(card => card.Item.Id));
        Assert.Equal([1, 3, 4], module.Options.Select(option => option.Count));

        await search.MarkUpdatesReadAsync(Card(search, "1000001"));

        Assert.Empty(search.ListItems);
        Assert.Equal([0, 4, 4], module.Options.Select(option => option.Count));
    });

    [Fact]
    public Task ほかの画面で既読にしても_ナビの数え直しで検索の札が下りる() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        var card = Card(main.Search, "1000001");

        // 通知・商品ページの「既読にする」と同じ命令で、検索を通さずに既読にする
        await app.Services.Commands.ExecuteAsync(new Core.Commands.UiCommand.MarkNotificationsRead(["n1", "n2"]));
        main.RefreshBadges();

        await UiThread.Until(() => !card.HasUpdate, "検索の札が下りる");
    });

    [Fact]
    public void 更新ありはBOOTHの情報の見出しで販売終了のすぐ後に出る()
    {
        var booth = SearchModuleCatalog.Menu.Single(layout => layout.Title == SearchModuleCatalog.BoothInfo);
        var group = booth.Groups.Single(kinds => kinds.Contains(SearchModuleKind.Updated));

        Assert.Equal(group.ToList().IndexOf(SearchModuleKind.EndOfSale) + 1, group.ToList().IndexOf(SearchModuleKind.Updated));
        Assert.False(SearchModuleCatalog.Of(SearchModuleKind.Updated).AllowsMany);
    }
}
