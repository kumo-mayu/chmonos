using System.IO;
using System.Windows.Controls;
using Chmonos.App.Controls;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// ショップの一覧の「更新があるものだけ」と、ショップの中の1本のスクロール（一番上・名前の段が半分・流れ去った後・一番下を、カードとリストで）。
/// 2026-10-02 のメモ7-③ を直すときに足し、2026-10-03 のメモ24（上の段と一覧を1つのスクロールにする）で作り直した。
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

        // ショップの中は上の段と商品の一覧が1つのスクロール（メモ24）。流した位置を変えた絵を、カードとリストの両方で撮る
        new Scene("shop-header", "ショップの中：一番上（カード。バナー・見出しと集計・メモ・「このショップの商品」の行）", async context =>
        {
            var (shop, root) = await OpenShopAsync(context, count: 24);
            await ScrollShopAsync(context, shop, root, ShopScrollPlace.Top, list: false);
            return new Shot(root);
        }),

        new Scene("shop-scroll-half", "ショップの中：カードで、名前の段が半分流れた所（1行の見出しはまだ出ない）", async context =>
        {
            var (shop, root) = await OpenShopAsync(context, count: 24);
            await ScrollShopAsync(context, shop, root, ShopScrollPlace.NameHalf, list: false);
            return new Shot(root);
        }),

        new Scene("shop-scroll-past", "ショップの中：カードで、名前の段が流れ去った後（1行の見出しが重なり、「商品」の行がそのすぐ下で止まる）", async context =>
        {
            var (shop, root) = await OpenShopAsync(context, count: 24);
            await ScrollShopAsync(context, shop, root, ShopScrollPlace.Past, list: false);
            return new Shot(root);
        }),

        // 1行の見出しのメモ（メモ28）：無いときは何も出さず、長いときは「…」で切る
        new Scene("shop-compact-memo-none", "ショップの中：名前の段が流れ去った後、メモが無い（1行の見出しにメモの行は出ない）", async context =>
        {
            var (shop, root) = await OpenShopAsync(context, count: 24, memo: null);
            await ScrollShopAsync(context, shop, root, ShopScrollPlace.Past, list: false);
            return new Shot(root);
        }),

        new Scene("shop-compact-memo-long", "ショップの中：名前の段が流れ去った後、メモが長い（先頭の1行が「…」で切れる。2行目以降は出ない）", async context =>
        {
            var (shop, root) = await OpenShopAsync(context, count: 24, memo:
                "利用規約：改変可・再配布不可・商用利用は作者への連絡が必要で、連絡先は作り物の窓口の問い合わせフォームのみ。作り物の長い長い注意書きが続く。\n2行目は出ない。");
            await ScrollShopAsync(context, shop, root, ShopScrollPlace.Past, list: false);
            return new Shot(root);
        }),

        new Scene("shop-scroll-bottom", "ショップの中：カードで、一番下まで流した所", async context =>
        {
            var (shop, root) = await OpenShopAsync(context, count: 24);
            await ScrollShopAsync(context, shop, root, ShopScrollPlace.Bottom, list: false);
            return new Shot(root);
        }),

        new Scene("shop-list-top", "ショップの中：リストで、一番上", async context =>
        {
            var (shop, root) = await OpenShopAsync(context, count: 24);
            await ScrollShopAsync(context, shop, root, ShopScrollPlace.Top, list: true);
            return new Shot(root);
        }),

        new Scene("shop-list-half", "ショップの中：リストで、名前の段が半分流れた所", async context =>
        {
            var (shop, root) = await OpenShopAsync(context, count: 24);
            await ScrollShopAsync(context, shop, root, ShopScrollPlace.NameHalf, list: true);
            return new Shot(root);
        }),

        new Scene("shop-list-past", "ショップの中：リストで、名前の段が流れ去った後", async context =>
        {
            var (shop, root) = await OpenShopAsync(context, count: 24);
            await ScrollShopAsync(context, shop, root, ShopScrollPlace.Past, list: true);
            return new Shot(root);
        }),

        new Scene("shop-list-bottom", "ショップの中：リストで、一番下まで流した所", async context =>
        {
            var (shop, root) = await OpenShopAsync(context, count: 24);
            await ScrollShopAsync(context, shop, root, ShopScrollPlace.Bottom, list: true);
            return new Shot(root);
        }),

        new Scene("shop-switch-keeps", "ショップの中：カードで流した後にリストへ切り替えても、見ていた商品が同じ高さに残る（前後の位置を書き出す）", async context =>
        {
            var (shop, root) = await OpenShopAsync(context, count: 40);
            await ScrollShopAsync(context, shop, root, ShopScrollPlace.Past, list: false);
            var view = Look.View<ShopView>(root) ?? throw new InvalidOperationException("ショップ画面が見つかりません。");
            var scroll = Look.Named<InsetScrollViewer>(view, "ShopScroll") ?? throw new InvalidOperationException("ShopScroll が見つかりません。");
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 300);
            await context.SettleAsync();
            Console.WriteLine($"  切り替える前：流れの位置 {scroll.VerticalOffset:F1}・先頭の商品 {TopItem(view, shop)}");
            shop.IsListMode = true;
            await context.SettleAsync();
            Console.WriteLine($"  リストへ：流れの位置 {scroll.VerticalOffset:F1}・先頭の商品 {TopItem(view, shop)}");
            shop.OwnedOnly = true;
            await context.SettleAsync();
            Console.WriteLine($"  所持のみ：流れの位置 {scroll.VerticalOffset:F1}・先頭の商品 {TopItem(view, shop)}");
            shop.OwnedOnly = false;
            shop.IsListMode = false;
            await context.SettleAsync();
            Console.WriteLine($"  カードへ戻す：流れの位置 {scroll.VerticalOffset:F1}・先頭の商品 {TopItem(view, shop)}");
            return new Shot(root);
        }),

        new Scene("shop-focus-reveal", "ショップの中：重なった帯の下に隠れたカードへ移ると、帯の下に出るまで流れる（位置を書き出す）", async context =>
        {
            var (shop, root) = await OpenShopAsync(context, count: 40);
            await ScrollShopAsync(context, shop, root, ShopScrollPlace.Past, list: false);
            var view = Look.View<ShopView>(root) ?? throw new InvalidOperationException("ショップ画面が見つかりません。");
            var scroll = Look.Named<InsetScrollViewer>(view, "ShopScroll")!;
            var cards = new List<ItemCardBorder>();
            CollectCards(view, cards);

            // 帯（1行の見出し52＋止めた行）の下へ、先頭のカードを50だけ潜り込ませてから、そのカードへ移る
            var inset = scroll.TopInset;
            var target = cards.Where(card => card.TranslatePoint(new System.Windows.Point(0, 0), scroll).Y > inset)
                .OrderBy(card => card.TranslatePoint(new System.Windows.Point(0, 0), scroll).Y).First();
            var y0 = target.TranslatePoint(new System.Windows.Point(0, 0), scroll).Y;
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + y0 - (inset - 50));
            await context.SettleAsync();            var before = target.TranslatePoint(new System.Windows.Point(0, 0), scroll).Y;
            target.BringIntoView();
            await context.SettleAsync();
            var after = target.TranslatePoint(new System.Windows.Point(0, 0), scroll).Y;
            Console.WriteLine($"  帯の高さ {inset:F0}・流す前のカードの上端（窓の上から）{before:F0}・流した後 {after:F0}（帯の下＝{inset:F0} 以上なら出ている）");
            return new Shot(root);
        }),
        new Scene("shop-300", "ショップの中：300件（開く時間を書き出す）", async context =>
        {
            var (shop, root) = await OpenShopAsync(context, count: 300);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Console.WriteLine($"  メモリ（プライベート）{System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64 / 1048576} MB");
            await ScrollShopAsync(context, shop, root, ShopScrollPlace.Past, list: false);
            var view = Look.View<ShopView>(root) ?? throw new InvalidOperationException("ショップ画面が見つかりません。");
            var scroll = Look.Named<InsetScrollViewer>(view, "ShopScroll") ?? throw new InvalidOperationException("ShopScroll が見つかりません。");
            // 流した後の並べ直し（配置）だけを測る。描く時間は含まない
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (var step = 0; step < 20; step++)
            {
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 400);
                scroll.UpdateLayout();
            }

            Console.WriteLine($"  400px ずつ20回流す：配置は1回あたり {timer.Elapsed.TotalMilliseconds / 20.0:F1} ms（流れの位置 {scroll.VerticalOffset:F0} / {scroll.ScrollableHeight:F0}）");
            await context.SettleAsync();
            timer.Restart();
            shop.IsListMode = true;
            await context.SettleAsync();
            Console.WriteLine($"  カード→リスト：{timer.ElapsedMilliseconds} ms");
            timer.Restart();
            shop.IsListMode = false;
            await context.SettleAsync();
            Console.WriteLine($"  リスト→カード：{timer.ElapsedMilliseconds} ms");
            return new Shot(root);
        }),    ];

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

    /// <summary>16件（既定。流せる量が短い一覧）または多めの件数とバナーとメモを置き、作り物の店のショップ画面を開く。</summary>
    private static async Task<(ShopViewModel Shop, System.Windows.FrameworkElement Root)> OpenShopAsync(
        SceneContext context, int count = 16, string? memo = "利用規約：改変可・再配布不可。問い合わせは作り物の窓口へ。")
    {
        await SeedLibraryAsync(context, count: count);
        var banner = context.Seed.Paths.ShopBannerFile(FakeShopSubdomain);
        Fake.Image(Path.GetDirectoryName(banner)!, Path.GetFileName(banner), seed: "banner", width: 960, height: 320);
        await context.Seed.ShopNotes.SaveAsync(
        [
            new ShopNoteRecord { Subdomain = FakeShopSubdomain, NameHint = "作り物ショップ", IsFavorite = true, Memo = memo },
        ]);

        var main = await context.StartAsync();
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await SceneContext.UntilAsync(() => main.Search.TotalCount == count, "商品を読み終える");

        var open = System.Diagnostics.Stopwatch.StartNew();
        await main.ShowShopAsync(FakeShopSubdomain);
        var shop = context.Screen<ShopViewModel>();
        await SceneContext.UntilAsync(() => shop.IsListReady && shop.HasBanner, "商品とバナーが並ぶ");
        await context.SettleAsync();
        if (count >= 100)
        {
            Console.WriteLine($"  {count}件：ショップ画面を開いてから、商品とバナーが並んで落ち着くまで {open.ElapsedMilliseconds} ms");
        }

        return (shop, root);
    }

    private static void CollectCards(System.Windows.DependencyObject parent, List<ItemCardBorder> into)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is ItemCardBorder card)
            {
                into.Add(card);
            }

            CollectCards(child, into);
        }
    }

    private enum ShopScrollPlace { Top, NameHalf, Past, Bottom }

    /// <summary>
    /// 全体のスクロールを流す。一番上・名前の段が半分・名前の段が流れ去った後・一番下。
    /// 流した後の1行の見出し・止めた行の状態も書き出す
    /// </summary>
    private static async Task ScrollShopAsync(
        SceneContext context, ShopViewModel shop, System.Windows.FrameworkElement root, ShopScrollPlace place, bool list)
    {
        var view = Look.View<ShopView>(root) ?? throw new InvalidOperationException("ショップ画面が見つかりません。");
        shop.IsListMode = list;
        await context.SettleAsync();
        var scroll = Look.Named<InsetScrollViewer>(view, "ShopScroll") ?? throw new InvalidOperationException("ShopScroll が見つかりません。");
        var name = Look.Named<System.Windows.Controls.Border>(view, "NameBorder") ?? throw new InvalidOperationException("NameBorder が見つかりません。");
        var body = Look.Named<System.Windows.Controls.StackPanel>(view, "ScrollBody") ?? throw new InvalidOperationException("ScrollBody が見つかりません。");
        var nameBottom = name.TransformToAncestor(body).Transform(new System.Windows.Point(0, name.ActualHeight)).Y;
        var offset = place switch
        {
            ShopScrollPlace.Top => 0,
            ShopScrollPlace.NameHalf => nameBottom - (name.ActualHeight / 2),
            ShopScrollPlace.Past => nameBottom + 200,
            _ => scroll.ScrollableHeight,
        };
        scroll.ScrollToVerticalOffset(offset);
        await context.SettleAsync();
        Console.WriteLine($"  流れの位置 {scroll.VerticalOffset:F1} / 流せる量 {scroll.ScrollableHeight:F1}・名前の段の下端 {nameBottom:F1}・1行の見出し {shop.IsHeaderCompact}");
    }

    /// <summary>先頭に見えている商品の ID（行の先頭のカードか、リストの行）。窓の外の入れ物は数えない。</summary>
    private static string TopItem(ShopView view, ShopViewModel shop)
    {
        var scroll = Look.Named<InsetScrollViewer>(view, "ShopScroll")!;
        System.Windows.Controls.ItemsControl list = shop.IsListMode
            ? Look.Named<ItemListView>(view, "ItemList")!
            : Look.Named<CardRowsListBox>(view, "CardList")!;
        var best = double.MaxValue;
        var key = "?";
        var inset = 0;
        for (var index = 0; index < list.Items.Count; index++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(index) is not System.Windows.FrameworkElement container)
            {
                continue;
            }

            var y = container.TranslatePoint(new System.Windows.Point(0, 0), scroll).Y;
            if (y + container.ActualHeight > inset && y < best)
            {
                best = y;
                key = list.Items[index] switch
                {
                    CardRow row => row.Cards.OfType<ItemCardViewModel>().FirstOrDefault()?.Item.Id ?? "?",
                    ItemCardViewModel card => card.Item.Id,
                    _ => "?",
                } + $"（上から {y:F0}）";
            }
        }

        return key;
    }
}