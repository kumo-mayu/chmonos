using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// 検索の並べ替えの区切りの札（ユーザ判断 2026-10-01：図書館やビデオショップの分類の札）。
/// カテゴリ順・ショップ順・入手日順（年月）で、まとまりの最初の商品の前に、カードと同じ升（リストでは同じ高さの行）で札が入る。
/// 幅を変えて（--width 900,1280）札が段のどこに来ても崩れないかを見る
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> SortDividers =>
    [
        SortScene("search-sort-category", "検索：カテゴリ順のカード（親付きの札・カテゴリなしの札）", SortKind.Category, list: false),
        SortScene("search-sort-shop", "検索：ショップ順のカード（ショップごとの札・ショップなしの札）", SortKind.Shop, list: false),
        SortScene("search-sort-category-list", "検索：カテゴリ順のリスト（札は列をまたぐ1行）", SortKind.Category, list: true),
        SortScene("search-sort-shop-list", "検索：ショップ順のリスト（札は列をまたぐ1行）", SortKind.Shop, list: true),
        // 比べる相手：札を出さない並べ替え（名前順）のリスト。札の行が列の幅や横の流しを変えていないかを見る
        SortScene("search-sort-name-list", "検索：名前順のリスト（札なし。札の入ったリストと比べる）", SortKind.Name, list: true),
        // メモ16-④：ショップの札のアイコンが、カードの大きさのスライダーの両端でも収まるか
        SortScene("search-sort-shop-small", "検索：ショップ順のカード、カードの大きさが最小（160）", SortKind.Shop, list: false, cardWidth: Chmonos.App.Services.CardMetrics.MinWidth),
        SortScene("search-sort-shop-large", "検索：ショップ順のカード、カードの大きさが最大（360）", SortKind.Shop, list: false, cardWidth: Chmonos.App.Services.CardMetrics.MaxWidth),
        // メモ16-①：Tab で入った先（先頭のカード）の印が、一覧の端で切れずに見えるか
        SortScene("search-sort-shop-focus", "検索：ショップ順のカード、Tab で入った先（先頭のカード）にフォーカスの枠を足す", SortKind.Shop, list: false, markFocus: true),
        SortScene("search-sort-name-focus", "検索：名前順のカード（札なし）、Tab で入った先にフォーカスの枠を足す", SortKind.Name, list: false, markFocus: true),
        AcquiredScene("search-sort-acquired", "検索：入手日順のカード（年月の札・年の違う同じ月・入手日なしの札）", list: false),
        AcquiredScene("search-sort-acquired-list", "検索：入手日順のリスト（年月の札は列をまたぐ1行）", list: true),
    ];

    /// <summary>
    /// 入手日順（既定の並べ替え）の札。10件：2026年9月4・2026年8月1・2025年12月2・2025年9月1・入手日なし2。
    /// 2026年9月と2025年9月で、同じ月でも年が違えば別の札になることを見る
    /// </summary>
    private static Scene AcquiredScene(string name, string description, bool list) => new(name, description, async context =>
    {
        DateOnly?[] dates =
        [
            new(2026, 9, 28), new(2026, 9, 20), new(2026, 9, 3), new(2026, 9, 1), new(2026, 8, 15),
            new(2025, 12, 24), new(2025, 12, 1), new(2025, 9, 10), null, null,
        ];
        await SeedLibraryAsync(context, count: dates.Length, (index, item) =>
            item with { Local = item.Local with { AcquiredAt = dates[index] } });

        // 入手日の札は既定で切（ユーザ判断 2026-10-01）。この場面は札を見るので入れる
        var main = await context.StartAsync(settings => settings with { ShowAcquiredSortDividers = true });
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await SceneContext.UntilAsync(() => main.Search.TotalCount == dates.Length, "商品を読み終える");

        // 「お気に入りのみ」の条件を外す（SortScene の注記）。外すと「最近追加した順」になるので、入手日順へ戻す
        main.Search.ShowRecentlyAddedFirst();
        main.Search.SortField = main.Search.SortFields.First(field => field.Kind == SortKind.AcquiredAt);
        main.Search.IsListMode = list;
        await SceneContext.UntilAsync(() => main.Search.Rows.Sum(row => row.Cards.OfType<ItemCardViewModel>().Count()) == dates.Length, "商品が並ぶ");
        await context.SettleAsync();
        return new Shot(root);
    });

    /// <summary>
    /// 10件：衣装4・装飾品2・とても長い名前のカテゴリ1（表に無い）・カテゴリなし3。ショップは2つと、ショップの無い商品1件
    /// </summary>
    private static Scene SortScene(string name, string description, SortKind kind, bool list, bool markFocus = false, double? cardWidth = null) => new(name, description, async context =>
    {
        string?[] categories = ["3D衣装", "3D衣装", "3D装飾品", null, null, "3D衣装", "作り物のとても長い名前のカテゴリで札の中で折り返す分け方", "3D装飾品", "3D衣装", null];
        await SeedLibraryAsync(context, count: categories.Length, (index, item) =>
        {
            var booth = item.Booth with
            {
                Category = categories[index] is { } category ? new BoothCategory { Id = 1, Name = category } : null,
                Shop = index == 9 ? null : item.Booth.Shop,
            };
            return item with
            {
                Booth = booth,
                Local = item.Local with { AcquiredAt = new DateOnly(2026, 9, 1 + index) },
            };
        });

        // ショップの札のアイコン（メモ2-⑤）：作り物ショップにだけ置き、ほかのショップの札は何も出さない形を並べて見る
        if (kind == SortKind.Shop)
        {
            var icon = context.Seed.Paths.ShopIconFile(FakeShopSubdomain, "https://example.invalid/shop-icon.png");
            Fake.Image(System.IO.Path.GetDirectoryName(icon)!, System.IO.Path.GetFileName(icon), seed: "shop-icon", width: 128, height: 128);
        }

        var main = await context.StartAsync();
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await SceneContext.UntilAsync(() => main.Search.TotalCount == categories.Length, "商品を読み終える");

        // 保存された条件が無い保存先では、最初の条件「お気に入りのみ」で0件になる（search-cards の注記）。条件を外してから並べ替える
        main.Search.ShowRecentlyAddedFirst();
        main.Search.SortField = main.Search.SortFields.First(field => field.Kind == kind);
        main.Search.IsListMode = list;
        await SceneContext.UntilAsync(() => main.Search.Rows.Sum(row => row.Cards.OfType<ItemCardViewModel>().Count()) == categories.Length, "商品が並ぶ");
        if (cardWidth is { } width)
        {
            // 共通のスライダーの値（場面ごとに別の処理で描くので、元へは戻さない）
            Chmonos.App.Services.CardMetrics.Apply(width);
        }

        await context.SettleAsync();
        if (markFocus)
        {
            // 窓の無い舞台はキーボードのフォーカスを受けられない。WPF の印と同じく装飾の層へ、カードの印の型を載せて見る
            // 先頭と、2段目・右端の列のカードに足す（段や一覧の端で切られる所が違うため）
            var cards = Look.All<Chmonos.App.Controls.ItemCardBorder>(root)
                .Where(card => System.Windows.Automation.AutomationProperties.GetAutomationId(card) == "ItemCard").ToList();
            foreach (var card in cards.Where((_, index) => index is 0 or 1 or 4))
            {
                FocusPreview.Show(card, "CardFocusVisual");
            }
            await context.SettleAsync();
        }

        return new Shot(root);
    });
}
