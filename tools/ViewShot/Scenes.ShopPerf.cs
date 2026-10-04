using System.IO;
using System.Windows;
using System.Windows.Controls;
using Chmonos.App.Controls;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// ショップの中を開く・流す・切り替える速さとメモリを書き出す（担当SHP3 が作り、2026-10-05 の測り直しで台に残した。
/// 1本のスクロールで行を全部作っていた頃は 300件で落ち着くまで約2.4秒・260MB だった。崩れていないかをこれで見る。
/// 比べるときは前の版にもこのファイルを入れ、同じ場面を交互に回す）。
/// 版によって流す部品が違う（旧は一覧の中の ScrollViewer、今は全体を流す ShopScroll）ので、名前で探し、無ければ見えている一覧の中を探す。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> ShopPerfScenes =>
    [
        new Scene("perf-shop-300", "計測：300件のショップ（カード）", context => ShopPerfAsync(context, 300)),
        new Scene("perf-shop-16", "計測：16件のショップ（カード）", context => ShopPerfAsync(context, 16)),
        new Scene("perf-shop-300-list", "計測：300件のショップ（リストで開く）", context => ShopPerfAsync(context, 300, list: true)),
        new Scene("perf-shop-16-list", "計測：16件のショップ（リストで開く）", context => ShopPerfAsync(context, 16, list: true)),
    ];

    private static async Task<Shot> ShopPerfAsync(SceneContext context, int count, bool list = false)
    {
        var subdomain = "viewshot-" + Fake.Hex("作り物ショップ")[..6];
        await SeedLibraryAsync(context, count: count);
        var banner = context.Seed.Paths.ShopBannerFile(subdomain);
        Fake.Image(Path.GetDirectoryName(banner)!, Path.GetFileName(banner), seed: "banner", width: 960, height: 320);
        await context.Seed.ShopNotes.SaveAsync(
        [
            new ShopNoteRecord { Subdomain = subdomain, NameHint = "作り物ショップ", IsFavorite = true, Memo = "利用規約：改変可・再配布不可。問い合わせは作り物の窓口へ。" },
        ]);

        var main = await context.StartAsync();
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await SceneContext.UntilAsync(() => main.Search.TotalCount == count, "商品を読み終える");
        if (list)
        {
            await context.Services.Commands.ExecuteAsync(new Chmonos.Core.Commands.UiCommand.ChangeUiState(state => state with { ItemListScreens = ["shop"] }));
        }

        // 検索の画面の分を片付けてから、開く前のメモリを控える
        await context.SettleAsync();
        var before = PrivateMb();

        var open = System.Diagnostics.Stopwatch.StartNew();
        await main.ShowShopAsync(subdomain);
        var shop = context.Screen<ShopViewModel>();
        await SceneContext.UntilAsync(() => shop.IsListReady && shop.HasBanner, "商品とバナーが並ぶ");
        var ready = open.ElapsedMilliseconds;
        await context.SettleAsync();
        var settled = open.ElapsedMilliseconds;
        var view = Look.View<ShopView>(root) ?? throw new InvalidOperationException("ショップ画面が見つかりません。");
        Console.WriteLine($"PERF open count={count} list={list} ready_ms={ready} settled_ms={settled} cards={Look.All<ItemCardBorder>(view).Count()} listitems={Look.All<ListViewItem>(view).Count()}");
        Console.WriteLine($"PERF mem before_mb={before} after_open_mb={PrivateMb()}");

        var scroll = MainScroll(view, shop);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var steps = 0;
        while (steps < 20 && scroll.VerticalOffset < scroll.ScrollableHeight - 0.5)
        {
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 400);
            scroll.UpdateLayout();
            steps++;
        }

        var perStep = steps == 0 ? 0 : timer.Elapsed.TotalMilliseconds / steps;
        timer.Restart();
        await context.SettleAsync();
        Console.WriteLine($"PERF scrolled rows_with_cells={Look.All<GridViewRowPresenter>(view).Count(p => p.IsVisible)} listitems={Look.All<ListViewItem>(view).Count()}");
        Console.WriteLine($"PERF scroll steps={steps} layout_ms_per_step={perStep:F1} settle_after_ms={timer.ElapsedMilliseconds} offset={scroll.VerticalOffset:F0}/{scroll.ScrollableHeight:F0} cards={Look.All<ItemCardBorder>(view).Count()}");
        Console.WriteLine($"PERF mem after_scroll_mb={PrivateMb()} pid={Environment.ProcessId}");
        if (Environment.GetEnvironmentVariable("PERF_PAUSE") == "1")
        {
            Console.Out.Flush();
            await Task.Delay(40000);
        }

        scroll.ScrollToVerticalOffset(0);
        await context.SettleAsync();

        timer.Restart();
        shop.IsListMode = !list;
        await Stage.IdleAsync();
        var toListIdle = timer.ElapsedMilliseconds;
        await context.SettleAsync();
        var toList = timer.ElapsedMilliseconds;
        var rows = Look.All<ListViewItem>(view).Count();
        var cells = Look.All<GridViewRowPresenter>(view).Count(presenter => presenter.IsVisible);
        Console.WriteLine($"PERF mem switched_mb={PrivateMb()} rows={rows} rows_with_cells={cells}");
        timer.Restart();
        shop.IsListMode = list;
        await Stage.IdleAsync();
        var toCardIdle = timer.ElapsedMilliseconds;
        await context.SettleAsync();
        var toCard = timer.ElapsedMilliseconds;
        Console.WriteLine($"PERF switch (first=to_{(list ? "card" : "list")}) first_ms={toList} (idle {toListIdle}) back_ms={toCard} (idle {toCardIdle})");
        Console.WriteLine($"PERF mem end_mb={PrivateMb()}");
        return new Shot(root);
    }

    private static string PrivateMb()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return $"{System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64 / 1048576}(managed {GC.GetTotalMemory(true) / 1048576})";
    }

    private static ScrollViewer MainScroll(ShopView view, ShopViewModel shop)
    {
        if (Look.Named<ScrollViewer>(view, "ShopScroll") is { } outer)
        {
            return outer;
        }

        ItemsControl list = shop.IsListMode ? Look.Named<ItemListView>(view, "ItemList")! : Look.Named<CardRowsListBox>(view, "CardList")!;
        return Look.All<ScrollViewer>(list).First();
    }
}
