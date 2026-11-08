using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// 商品ページの対応アバター：変換経由の段（docs/spec/conversion.md）。衣装はアオイに対応。アオイ用の逆方向と、モモ・キキ用の順方向のプロファイルを
/// 持っているので、モモとキキが変換経由で並ぶ（キキは持っている）。名前・ID は作り物
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> AvatarViaConversionScenes =>
    [
        new Scene("item-page-avatars-via-conversion", "商品ページ：変換経由の段（持っているプロファイルで変換できる見込みのアバター）", ViaConversionAsync),
    ];

    private static async Task<Shot> ViaConversionAsync(SceneContext context)
    {
        static AvatarRegistryEntry Avatar(string id, string name, bool owned = false) => new()
        {
            ItemId = id,
            BoothName = $"作り物のアバター「{name}」",
            DisplayName = name,
            Category = "3Dキャラクター",
            IsOwnedManually = owned,
        };

        await context.Seed.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [Avatar("9800401", "アオイ"), Avatar("9800402", "モモ"), Avatar("9800403", "キキ", owned: true)],
        });

        Task<ItemRecord> ProfileAsync(string id, string name, params ConversionTarget[] targets)
            => context.Fake.ItemAsync(id, name, record => record with
            {
                Local = record.Local with { Conversion = new ConversionSetting { Mode = ConversionMode.Profile, Targets = targets } },
            });

        await ProfileAsync("9900202", "作り物のプロファイル（アオイ）", new ConversionTarget { AvatarItemId = "9800401", Forward = true, Reverse = true, Confirmed = true });
        await ProfileAsync("9900203", "作り物のプロファイル（モモ・キキ）",
            new ConversionTarget { AvatarItemId = "9800402", Forward = true, Confirmed = true },
            new ConversionTarget { AvatarItemId = "9800403", Forward = true, Confirmed = true });

        var item = await context.Fake.ItemAsync(
            "9900201",
            "作り物の衣装（アオイに対応）",
            record => record with
            {
                Local = record.Local with
                {
                    Avatars = [new AvatarLink { AvatarItemId = "9800401", Name = "アオイ", Source = AvatarLinkSource.SupportSection, Confirmed = true }],
                    AvatarsDetectedAt = DateTimeOffset.Now,
                },
            },
            images: 1);

        var main = await context.StartAsync();

        // 変換経由は、検索が読んだ手元の全商品からプロファイルを拾う。読み終える前に開くと、まだ空で段が出ない（アプリでは検索を読んだ後に開く）
        await SceneContext.UntilAsync(() => !main.Search.IsLoading && main.Search.TotalCount == 3, "商品を読み終える");
        main.ShowItem(item);
        var root = context.MainWindow();
        await context.PresentAsync(root);
        return new Shot(root) { Focus = () => Look.View<ItemAvatarsPanel>(root), FocusMargin = 8 };
    }
}
