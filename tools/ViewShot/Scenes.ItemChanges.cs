using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace ViewShot;

/// <summary>
/// 商品ページのユーザータグの欄（メモ3-③：小分類まで・畳める・商品名の下）と、BOOTH で変わった所の印（メモ7-①）。
/// 印の色は種類で4つ（変わった・足された・消えた・価格）。明暗とも、札の文字と線が読めるかを見る。
/// </summary>
internal static partial class Scenes
{
    // 右の列と左の説明が流さずに入る高さ（見出し3つ・画像3枚・タグ・バリエーション3つ・記録していること）
    private const double ChangesHeight = 1700;

    private static IEnumerable<Scene> ItemChangeScenes =>
    [
        new Scene("item-page-user-tags", "商品ページ：ユーザータグを小分類まで出した所（大分類だけの物と、小分類2つの物）", async context =>
        {
            var item = await SeedTaggedItemAsync(context, "9900601");
            var root = await OpenItemAsync(context, item);
            return new Shot(root);
        })
        {
            Height = ChangesHeight,
        },

        new Scene("item-page-user-tags-folded", "商品ページ：ユーザータグの欄を畳んだ所（BOOTHのタグは開いたまま）", async context =>
        {
            var item = await SeedTaggedItemAsync(context, "9900602");
            var root = await OpenItemAsync(context, item);
            context.Screen<ItemViewModel>().IsUserTagsExpanded = false;
            await context.SettleAsync();
            return new Shot(root);
        })
        {
            Height = ChangesHeight,
        },

        new Scene("item-page-changes", "商品ページ：BOOTHで変わった所の印（商品名・価格と数・画像・販売終了・見出しの変更と追加・消えた見出し）", async context =>
        {
            // 見出しの本文は複数行にし、変わった行（メモ13-②）を本文の上の地と、見出しの下の消えた行で見る
            var item = await SeedTaggedItemAsync(context, "9900603", delisted: true, sections:
            [
                new H2Section { Heading = "★更新履歴★", Text = "v1.0 公開しました\nv1.1 袖の形を直しました\nv1.2 テクスチャを2色足しました" },
                new H2Section { Heading = "同梱物", Text = "unitypackage\nテクスチャ（PNG）" },
                new H2Section { Heading = "注意事項", Text = "作り物の注意書きです。\n改変は自由です。" },
            ]);
            await context.Seed.Notifications.SaveAsync(
            [
                new NotificationRecord
                {
                    Id = $"item-updated:{item.Id}",
                    Kind = NotificationKind.ItemUpdated,
                    ItemId = item.Id,
                    Title = item.Booth.Name ?? item.Id,
                    Detail = "作り物の知らせ",
                    Diffs =
                    [
                        new NotificationDiff { Field = BoothChanges.SaleField, Before = "販売中", After = "販売終了" },
                        new NotificationDiff { Field = BoothChanges.NameField, Before = "作り物の衣装セット", After = item.Booth.Name },
                        new NotificationDiff { Field = BoothChanges.PriceField, Before = "¥ 1,200", After = "¥ 1,500" },
                        new NotificationDiff { Field = BoothChanges.VariationsField, Before = "2 件", After = "3 件" },
                        new NotificationDiff { Field = BoothChanges.ImagesField, Before = "4 枚", After = "3 枚" },
                        new NotificationDiff
                        {
                            Field = "更新履歴",
                            Before = "v1.0 公開しました v1.1 袖の形を直した",
                            After = "v1.0 公開しました v1.1 袖の形を直しました v1.2 テクスチャを2色足しました",
                            Lines =
                            [
                                Line(false, "v1.1 袖の形を直した"),
                                Line(true, "v1.1 袖の形を直しました"),
                                Line(true, "v1.2 テクスチャを2色足しました"),
                            ],
                        },
                        new NotificationDiff
                        {
                            Field = "同梱物",
                            After = "unitypackage テクスチャ（PNG）",
                            Lines = [Line(true, "unitypackage"), Line(true, "テクスチャ（PNG）")],
                        },
                        new NotificationDiff
                        {
                            Field = "注意事項",
                            Before = "作り物の注意書きです。 再配布は禁止です。",
                            After = "作り物の注意書きです。 改変は自由です。",
                            Lines =
                            [
                                Line(false, "再配布は禁止です。"),
                                Line(false, "旧版の利用は自己責任でお願いします。"),
                                Line(false, "問い合わせはメッセージからどうぞ。"),
                                Line(true, "改変は自由です。"),
                            ],
                            MoreRemoved = 2,
                        },
                        new NotificationDiff { Field = "旧版について", Before = "旧版は配布を終えました", Lines = [Line(false, "旧版は配布を終えました")] },
                    ],
                    CreatedAt = new DateTimeOffset(2026, 10, 1, 21, 0, 0, TimeSpan.FromHours(9)),
                },
            ]);

            var root = await OpenItemAsync(context, item);
            await SceneContext.UntilAsync(() => context.Screen<ItemViewModel>().HasUnreadChanges, "知らせを読んで印が付く");
            await context.SettleAsync();
            return new Shot(root);
        })
        {
            Height = ChangesHeight,
        },
    ];

    private static NotificationLine Line(bool added, string text)
        => new() { Kind = added ? NotificationLineKind.Added : NotificationLineKind.Removed, Text = text };

    /// <summary>ユーザータグ（小分類あり・なし）・BOOTHのタグ・見出し3つ・バリエーション3つの商品。主画面を組む前に呼ぶ。</summary>
    private static Task<ItemRecord> SeedTaggedItemAsync(
        SceneContext context, string id, bool delisted = false, IReadOnlyList<H2Section>? sections = null)
        => context.Fake.ItemAsync(
            id,
            "作り物の衣装セット 改",
            record => record with
            {
                Booth = record.Booth with
                {
                    Description = "場面を描くための作り物の商品です。",
                    H2Sections = sections ??
                    [
                        new H2Section { Heading = "★更新履歴★", Text = "v1.1 袖の形を直しました" },
                        new H2Section { Heading = "同梱物", Text = "unitypackage・テクスチャ" },
                        new H2Section { Heading = "注意事項", Text = "作り物の注意書きです。" },
                    ],
                    Tags = ["VRChat", "3Dモデル", "衣装", "作り物のタグ"],
                    Variations =
                    [
                        new BoothVariation { Id = 1, Name = "フルセット", Price = 1500, Type = "downloadable" },
                        new BoothVariation { Id = 2, Name = "ミナト用", Price = 800, Type = "downloadable" },
                        new BoothVariation { Id = 3, Name = "テクスチャのみ", Price = 300, Type = "downloadable" },
                    ],
                },
                Local = record.Local with
                {
                    LocalFiles = [Fake.FileRecord(Fake.Zip(@"ライブラリ\costume_set_v1.1.zip"))],
                    UserTags =
                    [
                        new UserTagAssignment { Top = "冬服" },
                        new UserTagAssignment { Top = "衣装", Subs = ["コート", "マフラー"] },
                    ],
                    IsDelisted = delisted,
                },
            },
            images: 3);
}
