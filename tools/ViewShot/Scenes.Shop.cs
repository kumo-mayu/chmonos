using System.IO;
using System.Windows.Controls;
using Chmonos.App.Controls;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// ショップの一覧の「更新があるものだけ」と、ショップの中の上の段（元の段・流して縮めた行・縮めた行でメモを開いた所）。
/// 2026-10-02 のメモ7-③・⑤ を直すときに足した。
///
/// ショップの中は、バナーを手元に置いておく（置いていないと開いたときに BOOTH へ取りに行き、台は通信を止めてあるので失敗が続いて落ち着かない）。
/// </summary>
internal static partial class Scenes
{
    /// <summary>作り物の商品の店（<see cref="Fake.ItemAsync"/> の既定の店）のサブドメイン。</summary>
    private static string FakeShopSubdomain => "viewshot-" + Fake.Hex("作り物ショップ")[..6];

    private static IEnumerable<Scene> ShopScenes =>
    [
        new Scene("shops-cards-updated", "ショップ一覧：更新のある店があり、「更新があるものだけ」のチェックが出た所", async context =>
        {
            await SeedLibraryAsync(context, count: 8, change: (index, record) => index >= 4 ? record with
            {
                Booth = record.Booth with { Shop = new BoothShop { Name = "作り物の別のショップ", Subdomain = "viewshot-other" } },
            } : record);
            await SeedUpdateAsync(context, "9900301");
            var main = await context.StartAsync();
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => main.Search.TotalCount == 8, "商品を読み終える");

            main.ShowShops();
            var shops = context.Screen<ShopsViewModel>();
            await SceneContext.UntilAsync(() => shops.IsListReady && shops.HasUpdatedShops, "ショップのカードが並ぶ");
            await context.SettleAsync();
            return new Shot(root);
        }),

        new Scene("shops-cards-updated-only", "ショップ一覧：「更新があるものだけ」で、更新のある店だけに絞った所", async context =>
        {
            await SeedLibraryAsync(context, count: 8, change: (index, record) => index >= 4 ? record with
            {
                Booth = record.Booth with { Shop = new BoothShop { Name = "作り物の別のショップ", Subdomain = "viewshot-other" } },
            } : record);
            await SeedUpdateAsync(context, "9900301");
            var main = await context.StartAsync();
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => main.Search.TotalCount == 8, "商品を読み終える");

            main.ShowShops();
            var shops = context.Screen<ShopsViewModel>();
            await SceneContext.UntilAsync(() => shops.IsListReady && shops.HasUpdatedShops, "ショップのカードが並ぶ");
            shops.UpdatedOnly = true;
            await SceneContext.UntilAsync(() => shops.Rows.Sum(row => row.Cards.Count) == 1, "更新のある店だけになる");
            await context.SettleAsync();
            return new Shot(root);
        }),

        new Scene("shop-header", "ショップの中：一番上（バナー・見出しと集計・メモの元の段）", async context =>
        {
            var (_, root) = await OpenShopAsync(context);
            return new Shot(root);
        }),

        new Scene("shop-header-compact", "ショップの中：商品の一覧を流して、上の段を1行に縮めた所", async context =>
        {
            var (shop, root) = await OpenShopAsync(context);
            await ScrollShopListAsync(context, shop, root);
            return new Shot(root);
        }),

        new Scene("shop-header-compact-memo", "ショップの中：縮めた行の「メモ」でメモの欄を開いた所", async context =>
        {
            var (shop, root) = await OpenShopAsync(context);
            await ScrollShopListAsync(context, shop, root);
            shop.ToggleMemoCommand.Execute(null);
            await context.SettleAsync();
            return new Shot(root);
        }),
    ];

    /// <summary>未読の「商品の更新」の知らせを1件置く（カードに「更新あり」、ショップ一覧に「更新のあった商品が 1 件」が出る）。</summary>
    private static Task SeedUpdateAsync(SceneContext context, string itemId) => context.Seed.Notifications.SaveAsync(
    [
        new NotificationRecord
        {
            Id = "viewshot-update-" + itemId,
            Kind = NotificationKind.ItemUpdated,
            Title = "作り物の更新",
            Detail = string.Empty,
            ItemId = itemId,
            CreatedAt = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.FromHours(9)),
        },
    ]);

    /// <summary>16件（流せる量が、縮めた後も残る数）とバナーとメモを置き、作り物の店のショップ画面を開く。</summary>
    private static async Task<(ShopViewModel Shop, System.Windows.FrameworkElement Root)> OpenShopAsync(SceneContext context)
    {
        await SeedLibraryAsync(context, count: 16);
        var banner = context.Seed.Paths.ShopBannerFile(FakeShopSubdomain);
        Fake.Image(Path.GetDirectoryName(banner)!, Path.GetFileName(banner), seed: "banner", width: 960, height: 320);
        await context.Seed.ShopNotes.SaveAsync(
        [
            new ShopNoteRecord { Subdomain = FakeShopSubdomain, NameHint = "作り物ショップ", IsFavorite = true, Memo = "利用規約：改変可・再配布不可。問い合わせは作り物の窓口へ。" },
        ]);

        var main = await context.StartAsync();
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await SceneContext.UntilAsync(() => main.Search.TotalCount == 16, "商品を読み終える");

        await main.ShowShopAsync(FakeShopSubdomain);
        var shop = context.Screen<ShopViewModel>();
        await SceneContext.UntilAsync(() => shop.IsListReady && shop.HasBanner, "商品とバナーが並ぶ");
        await context.SettleAsync();
        return (shop, root);
    }

    /// <summary>商品のカードの一覧を少し流す（人がホイールを1回回したくらい）。</summary>
    private static async Task ScrollShopListAsync(SceneContext context, ShopViewModel shop, System.Windows.FrameworkElement root)
    {
        var view = Look.View<ShopView>(root) ?? throw new InvalidOperationException("ショップ画面が見つかりません。");
        var list = Look.Named<CardRowsListBox>(view, "CardList") ?? throw new InvalidOperationException("商品のカードの一覧が見つかりません。");
        var scroll = ContentItemsControl.FindScrollViewer(list) ?? throw new InvalidOperationException("一覧の ScrollViewer が見つかりません。");
        scroll.ScrollToVerticalOffset(48);
        await SceneContext.UntilAsync(() => shop.IsHeaderCompact, "上の段が縮む");
        await context.SettleAsync();

        // 縮めて一覧が広がっても、流れの位置は飛ばない（一番上へ押し戻されて元の段に戻る、を繰り返さない）
        if (!shop.IsHeaderCompact || Math.Abs(scroll.VerticalOffset - 48) > 0.5)
        {
            throw new InvalidOperationException($"縮めた後の流れの位置が {scroll.VerticalOffset}（縮めたか：{shop.IsHeaderCompact}）。48 のままのはず。");
        }
    }
}
