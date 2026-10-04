using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// ショップの一覧の「更新があるものだけ」（2026-10-02 のメモ7「ショップ一覧でも変更のあったものだけを使えるべき」）。
/// 数え方はカードの「更新のあった商品が n 件」と同じ：通知に未読・未解消の「商品の更新」がある商品。
/// </summary>
public class ShopsUpdatedOnlyTests
{
    private static NotificationRecord Updated(string id, string itemId, bool isRead = false) => new()
    {
        Id = id,
        Kind = NotificationKind.ItemUpdated,
        Title = "作り物の更新",
        Detail = string.Empty,
        ItemId = itemId,
        IsRead = isRead,
        CreatedAt = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.FromHours(9)),
    };

    private static async Task<ShopsViewModel> OpenShopsAsync(TestApp app)
    {
        var main = await app.StartAsync();
        main.ShowShopsCommand.Execute(null);
        var shops = Assert.IsType<ShopsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => shops.IsListReady && !shops.IsLoading, "ショップ一覧の読み込みが済む");
        return shops;
    }

    private static IEnumerable<string> ShownShops(ShopsViewModel shops)
        => shops.Rows.SelectMany(row => row.Cards).Select(card => card.Shop.Subdomain);

    [Fact]
    public Task 更新があるものだけにすると_未読の更新がある商品を持つショップだけが並ぶ() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装", shop: "updated-shop"));
        await app.AddItemAsync(Make.Item("1000002", "作り物の髪型", shop: "quiet-shop"));
        await app.AddItemAsync(Make.Item("1000003", "作り物の靴", shop: "read-shop"));
        await app.Store.Notifications.SaveAsync([Updated("n1", "1000001"), Updated("n2", "1000003", isRead: true)]);

        var shops = await OpenShopsAsync(app);
        try
        {
            Assert.True(shops.HasUpdatedShops);
            Assert.Equal(3, ShownShops(shops).Count());

            shops.UpdatedOnly = true;

            // 既読にした知らせの店（read-shop）は数えない。カードの札と同じ数え方
            Assert.Equal(["updated-shop"], ShownShops(shops));
            Assert.False(shops.IsEmpty);

            shops.UpdatedOnly = false;
            Assert.Equal(3, ShownShops(shops).Count());
        }
        finally
        {
            // 絞りはアプリを閉じるまで覚える（静的）。次の試験へ持ち越さない
            shops.UpdatedOnly = false;
        }
    });

    [Fact]
    public Task 更新のあるショップが無ければ_チェックを出さず_覚えていた絞りも外す() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装", shop: "updated-shop"));
        await app.Store.Notifications.SaveAsync([Updated("n1", "1000001")]);

        var shops = await OpenShopsAsync(app);
        shops.UpdatedOnly = true;
        Assert.Single(ShownShops(shops));

        // 知らせを読んだ後で開き直す。覚えていた絞りのまま空にしない
        await app.Store.Notifications.SaveAsync([Updated("n1", "1000001", isRead: true)]);
        await shops.ReloadAsync();
        await app.SettleAsync();

        Assert.False(shops.HasUpdatedShops);
        Assert.False(shops.UpdatedOnly);
        Assert.Single(ShownShops(shops));
    });

    [Fact]
    public Task 更新があるものだけで当てはまらなければ_外し方を言う() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装", shop: "updated-shop"));
        await app.AddItemAsync(Make.Item("1000002", "作り物の髪型", shop: "quiet-shop"));
        await app.Store.Notifications.SaveAsync([Updated("n1", "1000001")]);

        var shops = await OpenShopsAsync(app);
        try
        {
            shops.UpdatedOnly = true;
            shops.FilterText = "quiet-shop";

            Assert.True(shops.IsEmpty);
            Assert.Equal("該当するショップがありません。検索語を短くするか、「更新があるものだけ」を外してください。", shops.EmptyText);
        }
        finally
        {
            shops.UpdatedOnly = false;
            shops.FilterText = string.Empty;
        }
    });
}
