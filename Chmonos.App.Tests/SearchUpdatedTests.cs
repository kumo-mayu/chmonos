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
    public Task 条件の更新通知ありで絞れ_既読にすると外れる() => TestApp.Run(async app =>
    {
        var search = (await StartAsync(app)).Search;
        var module = (UpdateNoticeModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Updated);

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

    private static NotificationRecord WithFields(NotificationRecord record, params string[] fields)
        => record with { Diffs = fields.Select(field => new NotificationDiff { Field = field }).ToList() };

    [Fact]
    public Task 更新通知ありは_変わった所の種類で絞れ_種類は編集状況と同じ形で並ぶ() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900081", "価格が変わった"));
        await app.AddItemAsync(Make.Item("9900082", "更新履歴が変わった"));
        await app.AddItemAsync(Make.Item("9900083", "名前と説明文が変わった"));
        await app.AddItemAsync(Make.Item("9900084", "変わっていない"));
        await app.AddItemAsync(Make.Item("9900085", "既読の販売の状態"));
        await app.Store.Notifications.SaveAsync(
        [
            WithFields(Updated("a", "9900081"), Core.Services.BoothChanges.PriceField),
            WithFields(Updated("b", "9900082"), "更新履歴"),
            WithFields(Updated("c", "9900083"), Core.Services.BoothChanges.NameField, "使い方"),
            WithFields(Updated("d", "9900085", isRead: true), Core.Services.BoothChanges.SaleField),
        ]);
        var search = (await app.StartAsync()).Search;
        var module = (UpdateNoticeModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Updated);
        List<string> Shown() => [.. search.ListItems.Select(card => card.Item.Id).Order(StringComparer.Ordinal)];

        Assert.Equal("更新通知あり", module.Label);
        Assert.Equal(["更新通知ありのみ", "更新通知なしのみ", "両方"], module.Options.Select(option => option.Label));
        Assert.Equal(["中身の更新", "バリエーション", "価格", "販売の状態", "ページ内容の変更"], module.Kinds.Select(kind => kind.Label));
        Assert.All(module.Kinds, kind => Assert.True(kind.IsOn));
        Assert.Equal(["9900081", "9900082", "9900083"], Shown());

        // 既読の知らせは見ない。種類の横の件数は未読の知らせだけで数える
        Assert.Equal([1, 0, 1, 0, 1], module.Kinds.Select(kind => kind.Count));

        foreach (var kind in module.Kinds.Where(kind => kind.Label != "中身の更新"))
        {
            kind.IsOn = false;
        }

        Assert.Equal(["9900082"], Shown());
        Assert.Equal("更新通知あり：中身の更新の更新通知あり", module.SummaryText);

        // 最後の1つは外せない
        module.Kinds[0].IsOn = false;
        Assert.True(module.Kinds[0].IsOn);

        module.Kinds.Single(kind => kind.Label == "ページ内容の変更").IsOn = true;
        module.Selected = module.Options[1];
        Assert.Equal(["9900081", "9900084", "9900085"], Shown());

        // 両方の間は種類を押せない
        module.Selected = module.Options[2];
        Assert.False(module.CanEditKinds);
    });

    [Fact]
    public Task 更新通知ありの種類は保存され_読み直しても同じ() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        var module = (UpdateNoticeModule)SearchModuleMenuTests.Add(main.Search, SearchModuleKind.Updated);
        module.Kinds.Single(kind => kind.Label == "価格").IsOn = false;
        await main.FlushPendingWritesAsync();

        var saved = await app.Services.SettingsStore.UpdateUiStateAsync(state => state);
        var state = Assert.Single(saved.SearchModules!, state => state.Kind == nameof(SearchModuleKind.Updated));
        Assert.Equal(["content", "variations", "sale", "page"], state.Fields);

        var back = Assert.IsType<UpdateNoticeModule>(Assert.Single((await app.StartAsync()).Search.Modules, m => m.Kind == SearchModuleKind.Updated));
        Assert.False(back.Kinds.Single(kind => kind.Label == "価格").IsOn);
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
