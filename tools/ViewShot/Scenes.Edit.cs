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
    ];
}
