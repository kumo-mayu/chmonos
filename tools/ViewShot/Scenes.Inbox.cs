using BoothAssetManager.App.ViewModels;
using BoothAssetManager.Core.Models;

namespace ViewShot;

internal static partial class Scenes
{
    // 既読の丸を Tab で止まれるようにしたときに、見た目が変わっていないことを前後で比べるために足した（2026-09-30）。
    // フォーカスの枠は、この台では描けない（止まった所は experiments/PeerProbe -- focus で見る）
    private static IEnumerable<Scene> Inbox =>
    [
        new Scene("inbox-rows", "要確認：束が2つ（更新・参照切れ）。未読と既読の行・重要の印・変わったところの札", async context =>
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
