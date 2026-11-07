using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// バリエーションとアバターの結び付き（ユーザ判断 2026-11-06・docs/spec/conversion.md）。
/// アバターごとにバリエーションで売っている衣装：「ミナト用」「ソラ用」「リク用」「セット」。名簿は <see cref="AvatarViaBaseScenes"/> と同じ。
/// 持っているのは「ミナト用」だけ。ソラ用・リク用は名前から見つけた向き先、ミナト用は人が「作り物素体」と決めた。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> VariationTargetScenes =>
    [
        new Scene("item-page-avatars-other-variation", "商品ページ：持っていないバリエーション向けの対応アバターを薄く出した所（ミナト用だけ持っている）", async context =>
        {
            var item = await SeedVariationOutfitAsync(context);
            var main = await context.StartAsync();
            main.ShowItem(item);
            var root = context.MainWindow();
            await context.PresentAsync(root);
            return new Shot(root) { Focus = () => Look.View<ItemAvatarsPanel>(root), FocusMargin = 8 };
        }),

        new Scene("edit-variation-targets", "編集画面：購入したバリエーションの行ごとの対応（人が決めた札・名前から見つけた対応・買っていない行は薄い）", async context =>
        {
            var item = await SeedVariationOutfitAsync(context, editStates: true);
            var main = await context.StartAsync();
            await main.ShowEditAsync([item.Id]);
            var edit = context.Screen<EditViewModel>();
            edit.IsPurchasesExpanded = true;
            await SceneContext.UntilAsync(() => edit.Variations.Any(row => row.HasTargets), "対応の欄が並ぶ");
            var root = context.MainWindow();
            await context.PresentAsync(root);
            return new Shot(root) { Focus = () => Look.View<EditView>(root), FocusMargin = 0 };
        }),
    ];

    private static async Task<ItemRecord> SeedVariationOutfitAsync(SceneContext context, bool editStates = false)
    {
        static AvatarRegistryEntry Avatar(string id, string name, string? baseName) => new()
        {
            ItemId = id,
            BoothName = $"作り物のアバター「{name}」",
            DisplayName = name,
            Category = "3Dキャラクター",
            BaseName = baseName,
        };

        await context.Seed.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                Avatar("9800301", "ミナト", "作り物素体"),
                Avatar("9800302", "ユウ", "作り物素体"),
                Avatar("9800311", "ソラ", "別の素体"),
                Avatar("9800312", "ハル", "別の素体"),
                Avatar("9800321", "リク", null),
            ],
            BaseGroups =
            [
                new AvatarBaseGroup { Name = "作り物素体" },
                new AvatarBaseGroup { Name = "別の素体" },
            ],
        });

        static AvatarLink Link(string id, string name) => new()
        {
            AvatarItemId = id,
            Name = name,
            Source = AvatarLinkSource.Variation,
            Confirmed = true,
        };

        return await context.Fake.ItemAsync(
            "9900106",
            "作り物の衣装（アバターごとのバリエーション）",
            record => record with
            {
                Booth = record.Booth! with
                {
                    Variations =
                    [
                        new BoothVariation { Id = 1, Name = "ミナト用", Price = 1500, Type = "downloadable" },
                        new BoothVariation { Id = 2, Name = "ソラ用", Price = 1500, Type = "downloadable" },
                        new BoothVariation { Id = 3, Name = "リク用", Price = 1500, Type = "downloadable" },
                        new BoothVariation { Id = 4, Name = "セット", Price = 3500, Type = "downloadable" },
                    ],
                },
                Local = record.Local with
                {
                    Avatars = [Link("9800301", "ミナト"), Link("9800311", "ソラ"), Link("9800321", "リク")],
                    AvatarsDetectedAt = DateTimeOffset.Now,
                    // 編集の画面では、決めていない印の2つ（ソラ用＝確認待ちだけで「未確認」・セット＝何も無く「未選択」）も見せるため、買った行を足す
                    Purchases = editStates
                        ? [
                            new Purchase { VariationId = 1, NameSnapshot = "ミナト用", Price = 1500 },
                            new Purchase { VariationId = 2, NameSnapshot = "ソラ用", Price = 1500 },
                            new Purchase { VariationId = 4, NameSnapshot = "セット", Price = 3500 },
                        ]
                        : [new Purchase { VariationId = 1, NameSnapshot = "ミナト用", Price = 1500 }],
                    VariationTargets = [new VariationTarget { VariationId = 1, Bases = ["作り物素体"] }],
                    DetectedVariationTargets =
                    [
                        new VariationTarget { VariationId = 1, Avatars = ["9800301"] },
                        new VariationTarget { VariationId = 2, Avatars = ["9800311"] },
                        new VariationTarget { VariationId = 3, Avatars = ["9800321"] },
                    ],
                },
            },
            images: 1);
    }
}
