using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// 編集画面。2026-10-01 の点検で、小分類の ✕ と、バリエーションに付けたファイルの ✕ に Tab で止まらず、キーボードからは外せないと分かった。
/// 札の並び（✕ の並び）の Tab の通しと、止まったときの枠を見るために足した
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> Edit =>
    [
        new Scene("edit-chips", "編集画面：小分類の札3つ（✕ 付き）・バリエーション2つ・それぞれに付けたファイル（✕ 付き。バリエーション分けをバリエーションごとで開いた所）", async context =>
        {
            var item = await context.Fake.ItemAsync(
                "9900401",
                "作り物の衣装（編集画面の札の確かめ）",
                record => record with
                {
                    Booth = record.Booth! with
                    {
                        Variations =
                        [
                            new BoothVariation { Id = 1, Name = "本体", Price = 1500, Type = "downloadable" },
                            new BoothVariation { Id = 2, Name = "テクスチャ", Price = 500, Type = "downloadable" },
                        ],
                    },
                    Local = record.Local with
                    {
                        UserTags = [new UserTagAssignment { Top = "作り物の分類", Subs = ["小分類A", "小分類B", "小分類C"] }],
                        Purchases =
                        [
                            new Purchase { VariationId = 1, NameSnapshot = "本体", Price = 1500 },
                            new Purchase { VariationId = 2, NameSnapshot = "テクスチャ", Price = 500 },
                        ],
                        LocalFiles =
                        [
                            Fake.FileRecord(Fake.Zip(@"ライブラリ\edit_body_v1.0.zip", "body.unitypackage"), variation: 1),
                            Fake.FileRecord(Fake.Zip(@"ライブラリ\edit_body_extra.zip"), variation: 1),
                            Fake.FileRecord(Fake.Zip(@"ライブラリ\edit_texture_v1.0.zip"), variation: 2),
                        ],
                    },
                });

            var main = await context.StartAsync();
            await main.ShowEditAsync([item.Id]);
            var edit = context.Screen<EditViewModel>();
            edit.IsPurchasesExpanded = true;
            edit.IsFileSortExpanded = true;
            edit.IsByVariationView = true;
            await SceneContext.UntilAsync(() => edit.Variations.Any(row => row.HasLinkedFiles), "付けたファイルが並ぶ");
            var root = context.MainWindow();
            await context.PresentAsync(root);
            return new Shot(root) { Focus = () => Look.View<EditView>(root), FocusMargin = 0 };
        }),

        // 設定の「ファイルの表示順」をバージョンを考えた名前順にした形（2026-11-11）。記録の順は v10・v2・Base
        new Scene("edit-file-sort-by-version", "編集画面：バリエーション分けの「ファイルごと」を、表示順「バージョンを考えた名前順」で開いた所", async context =>
        {
            var item = await context.Fake.ItemAsync(
                "9900403",
                "作り物の衣装（ファイルの表示順の確かめ）",
                record => record with
                {
                    Booth = record.Booth! with
                    {
                        Variations =
                        [
                            new BoothVariation { Id = 1, Name = "本体", Price = 1500, Type = "downloadable" },
                            new BoothVariation { Id = 2, Name = "テクスチャ", Price = 500, Type = "downloadable" },
                        ],
                    },
                    Local = record.Local with
                    {
                        Purchases =
                        [
                            new Purchase { VariationId = 1, NameSnapshot = "本体", Price = 1500 },
                            new Purchase { VariationId = 2, NameSnapshot = "テクスチャ", Price = 500 },
                        ],
                        LocalFiles =
                        [
                            Fake.FileRecord(Fake.Zip(@"ライブラリ\Sample_v10.zip")),
                            Fake.FileRecord(Fake.Zip(@"ライブラリ\Sample_v2.zip")),
                            Fake.FileRecord(Fake.Zip(@"ライブラリ\Base.zip")),
                        ],
                    },
                });

            var main = await context.StartAsync(settings => settings with { FileOrder = FileOrder.NameWithVersion });
            await main.ShowEditAsync([item.Id]);
            var edit = context.Screen<EditViewModel>();
            edit.IsPurchasesExpanded = true;
            edit.IsFileSortExpanded = true;
            await SceneContext.UntilAsync(() => edit.FileSortRows.Count == 3, "ファイルごとの行が並ぶ");
            var root = context.MainWindow();
            await context.PresentAsync(root);
            return new Shot(root) { Focus = () => Look.View<EditView>(root), FocusMargin = 0 };
        }),

        new Scene("edit-purchase-dates", "編集画面：購入記録の行の日付の欄（メモ45。1件目に日付あり・2件目の贈った行・空欄の行と入手日と同じの薄い字）", async context =>
        {
            var item = await context.Fake.ItemAsync(
                "9900402",
                "作り物の衣装（購入の日付の確かめ）",
                record => record with
                {
                    Booth = record.Booth! with
                    {
                        Variations =
                        [
                            new BoothVariation { Id = 1, Name = "本体", Price = 1500, Type = "downloadable" },
                            new BoothVariation { Id = 2, Name = "テクスチャ差分セット", Price = 500, Type = "downloadable" },
                            new BoothVariation { Id = 3, Name = "おまけ", Price = 0, Type = "downloadable" },
                        ],
                    },
                    Local = record.Local with
                    {
                        AcquiredAt = new DateOnly(2024, 6, 1),
                        Purchases =
                        [
                            new Purchase { VariationId = 1, NameSnapshot = "本体", Price = 1500 },
                            new Purchase { VariationId = 1, NameSnapshot = "本体", Price = 1500, Kind = PurchaseKind.Given, PurchasedAt = new DateOnly(2025, 12, 24) },
                            new Purchase { VariationId = 2, NameSnapshot = "テクスチャ差分セット", Price = 500, PurchasedAt = new DateOnly(2025, 3, 10) },
                        ],
                    },
                });

            var main = await context.StartAsync();
            await main.ShowEditAsync([item.Id]);
            var edit = context.Screen<EditViewModel>();
            edit.IsPurchasesExpanded = true;
            await SceneContext.UntilAsync(() => edit.Variations.Any(row => row.HasExtras), "2件目の行が並ぶ");
            var root = context.MainWindow();
            await context.PresentAsync(root);
            return new Shot(root) { Focus = () => Look.View<EditView>(root), FocusMargin = 0 };
        }),

        // 左の商品名の段が流れ去ったら、左の列の上に1行の見出しを重ねる（ユーザ指示 2026-11-11。商品ページと同じ）
        new Scene("edit-scrolled", "編集画面：左の列を流して、商品名の段が隠れた所（左の上に1行の見出しが重なる。星はお気に入り）", async context =>
        {
            var item = await context.Fake.ItemAsync(
                "9900404",
                "作り物の衣装（編集画面の1行の見出しの確かめ・名前の長い商品）",
                record => record with
                {
                    Booth = record.Booth! with { Description = LongBody("9900404") },
                    Local = record.Local with { IsFavorite = true },
                },
                images: 3);

            var main = await context.StartAsync();
            await main.ShowEditAsync([item.Id]);
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await context.SettleAsync();

            var left = Look.View<EditView>(root) is { } view ? Look.Named<System.Windows.Controls.ScrollViewer>(view, "LeftScroll") : null;
            left?.ScrollToVerticalOffset(900);
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<EditView>(root), FocusMargin = 0 };
        }),

        // 編集画面で入れた購入の日付が、商品ページの購入の札にどう出るか（メモ45）。場面の置き場を増やさないよう編集画面の並びに置く
        new Scene("item-page-purchase-dates", "商品ページ：バリエーションの欄だけ（購入の日付を入れた札は頭に日付・空の札はそのまま）", async context =>
        {
            var item = await context.Fake.ItemAsync(
                "9900403",
                "作り物の衣装（購入の札の日付の確かめ）",
                record => record with
                {
                    Booth = record.Booth! with
                    {
                        Variations =
                        [
                            new BoothVariation { Id = 1, Name = "フルセット", Price = 1500, Type = "downloadable" },
                            new BoothVariation { Id = 2, Name = "テクスチャのみ", Price = 500, Type = "downloadable" },
                        ],
                    },
                    Local = record.Local with
                    {
                        AcquiredAt = new DateOnly(2024, 6, 1),
                        LocalFiles = [Fake.FileRecord(Fake.Zip(@"ライブラリ\purchase_dates_v1.0.zip"))],
                        Purchases =
                        [
                            new Purchase { VariationId = 1, NameSnapshot = "フルセット", Price = 1200 },
                            new Purchase { VariationId = 1, NameSnapshot = "フルセット", Price = 1500, Kind = PurchaseKind.Given, PurchasedAt = new DateOnly(2025, 12, 24) },
                            new Purchase { VariationId = 1, NameSnapshot = "フルセット", Kind = PurchaseKind.Given, PurchasedAt = new DateOnly(2026, 2, 14) },
                            new Purchase { VariationId = 2, NameSnapshot = "テクスチャのみ", Kind = PurchaseKind.Received, PurchasedAt = new DateOnly(2025, 3, 10) },
                        ],
                    },
                });

            var root = await OpenItemAsync(context, item);
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<ItemView>(root)?.FindName("VariationsAnchor") as System.Windows.FrameworkElement, FocusMargin = 8 };
        })
        {
            Height = 1600,
        },
    ];
}
