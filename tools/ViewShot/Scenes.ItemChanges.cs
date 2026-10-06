using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
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
    private const double ChangesHeight = 2100;

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

        new Scene("item-page-attributes", "商品ページ：属性の欄（開いた所。見出しに数）", async context =>
        {
            var item = await SeedAttributedItemAsync(context, "9900611");
            var root = await OpenItemAsync(context, item);
            context.Screen<ItemViewModel>().IsAttributesExpanded = true;
            await context.SettleAsync();
            return new Shot(root);
        })
        {
            Height = 1500,
        },

        new Scene("item-page-attributes-folded", "商品ページ：属性の欄を畳んだ所（item-page-attributes と diff で比べ、下の欄が詰まるだけなのを見る）", async context =>
        {
            var item = await SeedAttributedItemAsync(context, "9900612");
            var root = await OpenItemAsync(context, item);
            context.Screen<ItemViewModel>().IsAttributesExpanded = false;
            await context.SettleAsync();
            return new Shot(root);
        })
        {
            Height = 1500,
        },

        new Scene("item-page-changes","商品ページ：BOOTHで変わった所の帯（商品名・価格・バリエーションの入れ替え・画像・販売終了・見出しの変更と追加・消えた見出し）", async context =>
        {
            var item = await SeedChangedItemAsync(context, "9900603");
            var root = await OpenItemAsync(context, item);
            await SceneContext.UntilAsync(() => context.Screen<ItemViewModel>().HasUnreadChanges, "知らせを読んで印が付く");
            await context.SettleAsync();
            return new Shot(root);
        })
        {
            Height = ChangesHeight,
        },

        new Scene("item-page-changes-variations", "商品ページ：バリエーションの欄だけ（BOOTHの価格は右端・購入は灰色の札で全部並べる・足した行・消えた行・価格の変わった行）", async context =>
        {
            var item = await SeedChangedItemAsync(context, "9900611");
            var root = await OpenItemAsync(context, item);
            await SceneContext.UntilAsync(() => context.Screen<ItemViewModel>().HasUnreadChanges, "知らせを読んで印が付く");
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<ItemView>(root)?.FindName("VariationsAnchor") as FrameworkElement, FocusMargin = 8 };
        })
        {
            Height = ChangesHeight,
        },

        new Scene("item-page-changes-description", "商品ページ：商品説明の欄だけ（見出しの札は「追加 n」「削除 n」に分け、両方あれば線は橙）", async context =>
        {
            var item = await SeedChangedItemAsync(context, "9900612");
            var root = await OpenItemAsync(context, item);
            await SceneContext.UntilAsync(() => context.Screen<ItemViewModel>().HasUnreadChanges, "知らせを読んで印が付く");
            await context.SettleAsync();
            return new Shot(root) { Focus = () => CardOf(Look.View<ItemView>(root)?.FindName("DescriptionAnchor") as FrameworkElement), FocusMargin = 8 };
        })
        {
            Height = ChangesHeight,
        },

        new Scene("item-page-changes-bar", "商品ページ：上の帯だけ（両方あるので線は橙）", async context =>
        {
            var item = await SeedChangedItemAsync(context, "9900613");
            var root = await OpenItemAsync(context, item);
            await SceneContext.UntilAsync(() => context.Screen<ItemViewModel>().HasUnreadChanges, "知らせを読んで印が付く");
            await context.SettleAsync();
            return new Shot(root) { Focus = () => BarOf(root), FocusMargin = 0 };
        })
        {
            Height = ChangesHeight,
        },

        new Scene("item-page-changes-added-only", "商品ページ：足しただけの商品（上の帯の線・見出しの札と線が緑）", async context =>
        {
            var item = await SeedChangedItemAsync(context, "9900614", only: NotificationLineKind.Added);
            var root = await OpenItemAsync(context, item);
            await SceneContext.UntilAsync(() => context.Screen<ItemViewModel>().HasUnreadChanges, "知らせを読んで印が付く");
            await context.SettleAsync();
            return new Shot(root);
        })
        {
            Height = ChangesHeight,
        },

        new Scene("item-page-changes-removed-only", "商品ページ：消しただけの商品（上の帯の線・見出しの札と線が赤）", async context =>
        {
            var item = await SeedChangedItemAsync(context, "9900615", only: NotificationLineKind.Removed);
            var root = await OpenItemAsync(context, item);
            await SceneContext.UntilAsync(() => context.Screen<ItemViewModel>().HasUnreadChanges, "知らせを読んで印が付く");
            await context.SettleAsync();
            return new Shot(root);
        })
        {
            Height = ChangesHeight,
        },

        new Scene("item-page-changes-top", "商品ページ：上の帯（変わった所の並び・既読にする）を窓の高さで。並びは1行で、入り切らない分は「ほか n 件」", async context =>
        {
            var item = await SeedChangedItemAsync(context, "9900604");
            var root = await OpenItemAsync(context, item);
            await SceneContext.UntilAsync(() => context.Screen<ItemViewModel>().HasUnreadChanges, "知らせを読んで印が付く");
            await context.SettleAsync();
            return new Shot(root);
        })
        {
        },

        new Scene("item-page-changes-goto", "商品ページ：商品説明と見出しを畳んだまま、上の帯の「説明文：注意事項」を押した所（開いてから測って、見出しが上端に来る）", async context =>
        {
            var item = await SeedChangedItemAsync(context, "9900606");
            var root = await OpenItemAsync(context, item);
            var page = context.Screen<ItemViewModel>();
            var description = page.IsDescriptionExpanded;
            page.IsDescriptionExpanded = false;
            foreach (var section in page.Sections)
            {
                section.IsOpen = false;
            }

            await context.SettleAsync();
            page.GoToChangeCommand.Execute(page.ChangeTargets.Single(target => target.Label == "説明文：注意事項"));
            await context.SettleAsync();
            Chmonos.App.ViewModels.SectionFolds.DescriptionExpanded = description;
            return BodyShot(root);
        }),

        new Scene("item-page-changes-read-scrolled", "商品ページ：「同梱物」まで流した所で既読にした後（上で消えた行の分だけ戻し、同梱物の見出しが上端に残る）", async context =>
        {
            var item = await SeedChangedItemAsync(context, "9900607");
            var root = await OpenItemAsync(context, item);
            var page = context.Screen<ItemViewModel>();
            page.GoToChangeCommand.Execute(page.ChangeTargets.Single(target => target.Label == "説明文：同梱物"));
            await context.SettleAsync();
            page.MarkChangesReadCommand.Execute(null);
            await SceneContext.UntilAsync(() => page.IsChangesRead, "既読にして帯が「既読にしました」になる");
            await context.SettleAsync();
            return BodyShot(root);
        }),

        new Scene("item-page-changes-read","商品ページ：既読にした後（帯は同じ高さで「既読にしました」、消えた行・見出し・バリエーションは隠れる）", async context =>
        {
            var item = await SeedChangedItemAsync(context, "9900605");
            var root = await OpenItemAsync(context, item);
            var page = context.Screen<ItemViewModel>();
            await SceneContext.UntilAsync(() => page.HasUnreadChanges, "知らせを読んで印が付く");
            page.MarkChangesReadCommand.Execute(null);
            await SceneContext.UntilAsync(() => page.IsChangesRead, "既読にして帯が「既読にしました」になる");
            await context.SettleAsync();
            return new Shot(root);
        })
        {
            Height = ChangesHeight,
        },
    ];

    /// <summary>
    /// 変わった所を一通り持つ商品（メモ17）。見出しの本文は複数行にし、足した行・消えた行（元の位置）を本文の上の帯で見る。
    /// 消えた行・見出し・バリエーションは、知らせを作る側と同じく「今の並びで直前にあった物」を持つ
    /// </summary>
    /// <param name="only">足した行だけ・消した行だけの商品にする（上の帯の線と見出しの札の色が中身で変わるのを見る。メモ53②）。null なら両方</param>
    private static async Task<ItemRecord> SeedChangedItemAsync(SceneContext context, string id, NotificationLineKind? only = null)
    {
        var item = await SeedTaggedItemAsync(context, id, delisted: true, sections:
        [
            new H2Section { Heading = "★更新履歴★", Text = "v1.0 公開しました\nv1.1 袖の形を直しました\nv1.2 テクスチャを2色足しました" },
            new H2Section { Heading = "同梱物", Text = "unitypackage\nテクスチャ（PNG）" },
            new H2Section { Heading = "注意事項", Text = "作り物の注意書きです。\n改変は自由です。" },

            // 足しただけの見出し・消しただけの見出し（見出しの札と線の色が中身で変わるのを見る）
            new H2Section { Heading = "対応アバター", Text = "作り物アバターA\n作り物アバターB\n作り物アバターC" },
            new H2Section { Heading = "利用規約", Text = "規約は作り物です。" },
        ]);
        await context.Seed.Notifications.SaveAsync(
        [
            OnlyKind(new NotificationRecord
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
                    new NotificationDiff
                    {
                        Field = BoothChanges.PriceField,
                        Before = "¥ 300~",
                        After = "¥ 300~",
                        Prices =
                        [
                            new NotificationPrice { Id = 1, Name = "フルセット", Before = 1200, After = 1500 },
                            new NotificationPrice { Id = 2, Name = "ミナト用", Before = 1000, After = 800 },
                        ],
                    },
                    new NotificationDiff
                    {
                        Field = BoothChanges.VariationsField,
                        Before = "3 件",
                        After = "3 件",
                        Lines = [Line(false, "旧色セット", "フルセット"), Line(true, "テクスチャのみ")],
                    },
                    new NotificationDiff { Field = BoothChanges.ImagesField, Before = "4 枚", After = "3 枚" },
                    new NotificationDiff
                    {
                        Field = "更新履歴",
                        Before = "v1.0 公開しました v1.1 袖の形を直した",
                        After = "v1.0 公開しました v1.1 袖の形を直しました v1.2 テクスチャを2色足しました",
                        Lines =
                        [
                            Line(false, "v1.1 袖の形を直した", "v1.0 公開しました"),
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
                            Line(false, "再配布は禁止です。", "作り物の注意書きです。"),
                            Line(false, "旧版の利用は自己責任でお願いします。", "作り物の注意書きです。"),
                            Line(false, "問い合わせはメッセージからどうぞ。", "作り物の注意書きです。"),
                            Line(true, "改変は自由です。"),
                        ],
                        MoreRemoved = 2,
                    },
                    new NotificationDiff
                    {
                        Field = "対応アバター",
                        Before = "作り物アバターA",
                        After = "作り物アバターA 作り物アバターB 作り物アバターC",
                        Lines = [Line(true, "作り物アバターB"), Line(true, "作り物アバターC")],
                    },
                    new NotificationDiff
                    {
                        Field = "利用規約",
                        Before = "規約は作り物です。 商用利用は不可です。",
                        After = "規約は作り物です。",
                        Lines = [Line(false, "商用利用は不可です。", "規約は作り物です。")],
                    },
                    new NotificationDiff
                    {
                        Field = "旧版について",
                        Before = "旧版は配布を終えました",
                        Follows = "同梱物",
                        Lines = [Line(false, "旧版は配布を終えました"), Line(false, "旧版の問い合わせは受け付けていません", "旧版は配布を終えました")],
                    },
                ],
                CreatedAt = new DateTimeOffset(2026, 10, 1, 21, 0, 0, TimeSpan.FromHours(9)),
            }, only),
        ]);
        return item;
    }

    /// <summary>
    /// 知らせを、足した行だけ（または消した行だけ）の変化に絞る。行を持たない変化（商品名・価格・画像・販売状況）と、
    /// 該当する行が残らない見出しは外す。<paramref name="only"/> が null なら何もしない
    /// </summary>
    private static NotificationRecord OnlyKind(NotificationRecord record, NotificationLineKind? only)
    {
        if (only is not { } kind)
        {
            return record;
        }

        var diffs = (record.Diffs ?? [])
            .Where(diff => diff.Lines is { Count: > 0 })
            .Select(diff => new NotificationDiff
            {
                Field = diff.Field,
                Before = diff.Before,
                After = diff.After,
                Follows = diff.Follows,
                Lines = diff.Lines!.Where(line => line.Kind == kind).ToList(),
                MoreAdded = kind == NotificationLineKind.Added ? diff.MoreAdded : null,
                MoreRemoved = kind == NotificationLineKind.Removed ? diff.MoreRemoved : null,
            })
            .Where(diff => diff.Lines is { Count: > 0 })
            .ToList();
        return record with { Diffs = diffs };
    }

    /// <summary>上の帯（BOOTHで変わったところ）の外枠。</summary>
    private static FrameworkElement? BarOf(FrameworkElement root)
        => (Look.View<ItemView>(root)?.FindName("ChangeTargetList") as FrameworkElement)?.Parent is FrameworkElement grid
            && grid.Parent is FrameworkElement inner && inner.Parent is FrameworkElement outer ? outer : null;

    /// <summary>部品を囲むカード（型 Card の Border）。</summary>
    private static FrameworkElement? CardOf(FrameworkElement? element)
    {
        var card = element?.TryFindResource("Card");
        for (var current = element as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is Border border && card is not null && ReferenceEquals(border.Style, card))
            {
                return border;
            }
        }

        return element;
    }

    private static NotificationLine Line(bool added, string text, string? follows = null)
        => new() { Kind = added ? NotificationLineKind.Added : NotificationLineKind.Removed, Text = text, Follows = follows };

    /// <summary>ユーザータグ（小分類あり・なし）・BOOTHのタグ・見出し3つ・バリエーション3つの商品。主画面を組む前に呼ぶ。</summary>
    private static Task<ItemRecord> SeedAttributedItemAsync(SceneContext context, string id)
        => context.Fake.ItemAsync(
            id,
            "作り物の衣装",
            record => record with
            {
                Local = record.Local with
                {
                    Attributes = new Dictionary<string, int> { ["かわいい"] = 70, ["かっこいい"] = 25, ["露出"] = 10 },
                },
            },
            images: 1);

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

                    // 買った行は払った額と BOOTH の今の価格を並べる（メモ39）。フルセットは値上がり（払った額 ¥1,200・今 ¥1,500）、
                    // テクスチャのみは同じ額
                    Purchases =
                    [
                        new Purchase { VariationId = 1, NameSnapshot = "フルセット", Price = 1200 },

                        // 同じバリエーションを何度も買った（自分用1・贈った2）・貰った（価格なし）・BOOTH から消えた版を買った（メモ54）
                        new Purchase { VariationId = 1, NameSnapshot = "フルセット", Price = 1500, Kind = PurchaseKind.Given },
                        new Purchase { VariationId = 1, NameSnapshot = "フルセット", Price = 1500, Kind = PurchaseKind.Given },
                        new Purchase { VariationId = 3, NameSnapshot = "テクスチャのみ", Kind = PurchaseKind.Received },
                        new Purchase { VariationId = 4, NameSnapshot = "旧色セット", Price = 1000 },
                    ],
                },
            },
            images: 3);
}
