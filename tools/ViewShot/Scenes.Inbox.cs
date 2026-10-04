using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace ViewShot;

internal static partial class Scenes
{
    // 既読の丸を Tab で止まれるようにしたときに、見た目が変わっていないことを前後で比べるために足した（2026-09-30）。
    // フォーカスの枠は、この台では描けない（止まった所は experiments/PeerProbe -- focus で見る）
    private static IEnumerable<Scene> Inbox =>
    [
        new Scene("inbox-rows", "通知：束が2つ（更新・参照切れ）。未読と既読の行・重要の印・変わったところの札", async context =>
        {
            var item = await context.Fake.ItemAsync("9900501", "作り物の衣装セット");
            var day = new DateTimeOffset(2026, 9, 20, 21, 0, 0, TimeSpan.FromHours(9));
            await context.Seed.Notifications.SaveAsync(
            [
                new NotificationRecord
                {
                    Id = "viewshot-1",
                    Kind = NotificationKind.ItemUpdated,
                    ItemId = item.Id,
                    Title = "「作り物の衣装セット」が更新されました",
                    Detail = "価格が変わりました。",
                    Diffs = [new NotificationDiff { Field = "価格", Before = "¥ 1,500", After = "¥ 1,800" }],
                    CreatedAt = day,
                },
                new NotificationRecord
                {
                    Id = "viewshot-2",
                    Kind = NotificationKind.ItemUpdated,
                    ItemId = item.Id,
                    Title = "「作り物の衣装セット」が更新されました（読んだ行）",
                    Detail = "説明が変わりました。",
                    CreatedAt = day.AddDays(-1),
                    IsRead = true,
                },
                new NotificationRecord
                {
                    Id = "viewshot-3",
                    Kind = NotificationKind.OrphanTag,
                    Title = "タグ「冬服」がどの一覧にもありません",
                    Detail = "商品に付いていますが、タグの一覧から消えています。",
                    CreatedAt = day.AddDays(-2),
                },
            ]);

            var main = await context.StartAsync();
            main.ShowInboxCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var inbox = context.Screen<InboxViewModel>();

            // 既定は「未読のみ」。読んだ行の薄さも見たいので外す
            inbox.UnreadOnly = false;
            await SceneContext.UntilAsync(() => inbox.Lines.OfType<InboxRowLine>().Count() == 3, "知らせの行が3つ並ぶ");
            await context.SettleAsync();
            return new Shot(root);
        }),

        // 行のボタンの結果を、行の中と一覧の見出しの下へ移した（2026-10-04）。出る前（inbox-rows）と並べて、行が動かないことを見る
        new Scene("inbox-notices", "通知：行のボタンの途中経過・失敗が行の中に、成功が一覧の見出しの下に出た所", async context =>
        {
            var item = await context.Fake.ItemAsync("9900501", "作り物の衣装セット");
            var day = new DateTimeOffset(2026, 9, 20, 21, 0, 0, TimeSpan.FromHours(9));
            await context.Seed.Notifications.SaveAsync(
            [
                new NotificationRecord
                {
                    Id = "viewshot-1",
                    Kind = NotificationKind.ItemBackOnBooth,
                    ItemId = item.Id,
                    Title = "「作り物の衣装セット」がBOOTHに戻りました",
                    Detail = "商品情報を取り直せます。",
                    CreatedAt = day,
                },
                new NotificationRecord
                {
                    Id = "viewshot-2",
                    Kind = NotificationKind.ItemBackOnBooth,
                    ItemId = item.Id,
                    Title = "「作り物の衣装セット」がBOOTHに戻りました（2件目）",
                    Detail = "商品情報を取り直せます。",
                    CreatedAt = day.AddDays(-1),
                },
            ]);

            var main = await context.StartAsync();
            main.ShowInboxCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var inbox = context.Screen<InboxViewModel>();
            await SceneContext.UntilAsync(() => inbox.Lines.OfType<InboxRowLine>().Count() == 2, "知らせの行が2つ並ぶ");
            var rows = inbox.Lines.OfType<InboxRowLine>().Select(line => line.Row).ToList();
            rows[0].ActionNotice.Show("商品情報を取り直しています…");
            rows[1].ActionNotice.Warn("取り直せませんでした。通信を確かめて、少し待ってからもう一度お試しください。");
            inbox.ListNotice.Show("BOOTHの商品ページから情報を取り直しました。");
            await context.SettleAsync();
            return new Shot(root);
        }),

        new Scene("inbox-notices-none", "通知：行の知らせも見出しの下の知らせも出ていない所（inbox-notices の見本）", async context =>
        {
            var item = await context.Fake.ItemAsync("9900501", "作り物の衣装セット");
            var day = new DateTimeOffset(2026, 9, 20, 21, 0, 0, TimeSpan.FromHours(9));
            await context.Seed.Notifications.SaveAsync(
            [
                new NotificationRecord
                {
                    Id = "viewshot-1",
                    Kind = NotificationKind.ItemBackOnBooth,
                    ItemId = item.Id,
                    Title = "「作り物の衣装セット」がBOOTHに戻りました",
                    Detail = "商品情報を取り直せます。",
                    CreatedAt = day,
                },
                new NotificationRecord
                {
                    Id = "viewshot-2",
                    Kind = NotificationKind.ItemBackOnBooth,
                    ItemId = item.Id,
                    Title = "「作り物の衣装セット」がBOOTHに戻りました（2件目）",
                    Detail = "商品情報を取り直せます。",
                    CreatedAt = day.AddDays(-1),
                },
            ]);

            var main = await context.StartAsync();
            main.ShowInboxCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var inbox = context.Screen<InboxViewModel>();
            await SceneContext.UntilAsync(() => inbox.Lines.OfType<InboxRowLine>().Count() == 2, "知らせの行が2つ並ぶ");
            await context.SettleAsync();
            return new Shot(root);
        }),

        // 説明文の見出しの変更を、変わった行だけで出す（メモ13-②）。行の多い見出しは「ほか n 行」、行を持たない前の形の知らせは頭の抜き出しのまま
        new Scene("inbox-lines", "通知：説明文の見出しの変更を、足した行・消した行で出した所（行の多い見出し・前の形の知らせ・価格と並べて）", async context =>
        {
            var item = await context.Fake.ItemAsync("9900502", "作り物の衣装セット");
            static NotificationLine Line(bool added, string text)
                => new() { Kind = added ? NotificationLineKind.Added : NotificationLineKind.Removed, Text = text };
            await context.Seed.Notifications.SaveAsync(
            [
                new NotificationRecord
                {
                    Id = "viewshot-lines-1",
                    Kind = NotificationKind.ItemUpdated,
                    ItemId = item.Id,
                    Title = "「作り物の衣装セット」が更新されました",
                    Detail = "作り物の知らせ",
                    IsStrong = true,
                    Diffs =
                    [
                        new NotificationDiff
                        {
                            Field = "価格",
                            Before = "¥ 1,500~",
                            After = "¥ 1,500~",
                            Prices =
                            [
                                new NotificationPrice { Id = 2, Name = "支援版", Before = 2000, After = 2500 },
                                new NotificationPrice { Id = 3, Name = "テクスチャのみ", Before = 1800, After = 1500 },
                            ],
                        },
                        new NotificationDiff
                        {
                            Field = "更新履歴",
                            Before = "v1.0 公開しました v1.1 袖の形を直しました",
                            After = "v1.0 公開しました v1.1 袖の形を直しました",
                            Lines = [Line(false, "v1.2 予定：テクスチャを足します"), Line(true, "v1.2 テクスチャを2色足しました（2026-10-01）")],
                        },
                        new NotificationDiff
                        {
                            Field = "同梱物",
                            After = "unitypackage",
                            Lines =
                            [
                                Line(true, "unitypackage（本体）"),
                                Line(true, "テクスチャ（PNG・2048px）"),
                                Line(true, "着せ替え用のプレハブ"),
                                Line(true, "説明書（PDF）"),
                                Line(true, "おまけの小物"),
                                Line(true, "差分のテクスチャ"),
                            ],
                            MoreAdded = 3,
                        },
                    ],
                    CreatedAt = new DateTimeOffset(2026, 10, 1, 21, 0, 0, TimeSpan.FromHours(9)),
                },
                new NotificationRecord
                {
                    Id = "viewshot-lines-2",
                    Kind = NotificationKind.ItemUpdated,
                    ItemId = item.Id,
                    Title = "「作り物の衣装セット」が更新されました（前の形の知らせ）",
                    Detail = "作り物の知らせ",
                    Diffs = [new NotificationDiff { Field = "注意事項", Before = "作り物の注意書きです。", After = "作り物の注意書きです。" }],
                    CreatedAt = new DateTimeOffset(2026, 9, 30, 21, 0, 0, TimeSpan.FromHours(9)),
                },
            ]);

            var main = await context.StartAsync();
            main.ShowInboxCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var inbox = context.Screen<InboxViewModel>();
            await SceneContext.UntilAsync(() => inbox.Lines.OfType<InboxRowLine>().Count() == 2, "知らせの行が2つ並ぶ");
            await context.SettleAsync();
            return new Shot(root);
        }),

        // 行の名前と ID を付けたときに、木（peers）と見た目の前後を比べるために足した（2026-09-30）
        new Scene("stats", "統計：作り物の商品8件（よく買っているショップ・ディスク使用量の内訳の行）", async context =>
        {
            var (main, root) = await OpenLibraryAsync(context);
            main.ShowStatsCommand.Execute(null);
            var stats = context.Screen<StatsViewModel>();
            await SceneContext.UntilAsync(() => stats.Shops.Count > 0 && stats.Categories.Count > 0, "統計の行が並ぶ");
            await context.SettleAsync();
            return new Shot(root);
        }),

    ];
}
