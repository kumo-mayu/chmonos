using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace ViewShot;

/// <summary>
/// 商品ページの対応アバター：共通素体経由の点線の札と、共通素体ごとのまとめ（ユーザ指示 2026-11-04）。
/// 名簿は「作り物素体」（ミナト・ユウ・カイ）・「別の素体」（ソラ・ハル）と、素体の無いアバター（リク）。
/// 商品はミナト・ソラ・リクを記録し、「作り物素体」を宣言している（ユウ・カイ・ハルが素体経由）。持っているのはミナトとハル
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> AvatarViaBaseScenes =>
    [
        new Scene("item-page-avatars-via-base", "商品ページ：共通素体ごとにまとめ、素体経由のアバターを点線の札で並べた所（既定）", context => ViaBaseAsync(context, settings => settings)),
        new Scene("item-page-avatars-via-base-flat", "商品ページ：まとめない設定。記録した札の後ろに素体経由の点線の札", context => ViaBaseAsync(context, settings => settings with { GroupAvatarsByBase = false })),
        new Scene("item-page-avatars-grouped-only", "商品ページ：素体経由を出さない設定。記録した札だけを素体ごとにまとめる", context => ViaBaseAsync(context, settings => settings with { ShowAvatarsViaBase = false })),
    ];

    private static async Task<Shot> ViaBaseAsync(SceneContext context, Func<AppSettings, AppSettings> change)
    {
        static AvatarRegistryEntry Avatar(string id, string name, string? baseName, bool owned = false) => new()
        {
            ItemId = id,
            BoothName = $"作り物のアバター「{name}」",
            DisplayName = name,
            Category = "3Dキャラクター",
            BaseName = baseName,
            IsOwnedManually = owned,
        };

        await context.Seed.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                Avatar("9800301", "ミナト", "作り物素体", owned: true),
                Avatar("9800302", "ユウ", "作り物素体"),
                Avatar("9800303", "カイ", "作り物素体"),
                Avatar("9800311", "ソラ", "別の素体"),
                Avatar("9800312", "ハル", "別の素体", owned: true),
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
            Source = AvatarLinkSource.SupportSection,
            Confirmed = true,
        };

        var item = await context.Fake.ItemAsync(
            "9900105",
            "作り物の衣装（共通素体に対応）",
            record => record with
            {
                Local = record.Local with
                {
                    Avatars = [Link("9800301", "ミナト"), Link("9800311", "ソラ"), Link("9800321", "リク")],
                    AvatarBases = [new AvatarBaseLink { BaseName = "作り物素体", Source = AvatarLinkSource.SupportSection, Confirmed = true }],
                    AvatarsDetectedAt = DateTimeOffset.Now,
                },
            },
            images: 1);

        var main = await context.StartAsync(change);
        main.ShowItem(item);
        var root = context.MainWindow();
        await context.PresentAsync(root);
        return new Shot(root) { Focus = () => Look.View<ItemAvatarsPanel>(root), FocusMargin = 8 };
    }
}
