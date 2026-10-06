using System.Diagnostics;
using System.Windows.Controls;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// 通知の行の頭の商品の絵（担当L93・ユーザ指示 2026-10-06「通知画面にもアイコンが必要だろう」）。
/// 見た目の場面は、絵のある商品・絵の無い商品・消えた商品・商品を指さない知らせ・題が折り返す長い行を並べる。
/// 計測の場面は、知らせ n 件（商品ごとに1件・全部に絵）を開いて流し、開く時間・流す1歩の最長・止まったコマ・メモリを書き出す
/// （1歩 120px。33ms を超えた歩を「止まったコマ」と数える。perf-measure の決まり。描く手間は台では測れない）
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> InboxPictureScenes =>
    [
        new Scene("inbox-pictures", "通知：行の頭の商品の絵（絵あり・絵の無い商品・消えた商品・題が折り返す行・商品を指さない知らせ）", async context =>
        {
            var dressed = await context.Fake.ItemAsync("9900701", "作り物の衣装セット", images: 2);
            var bare = await context.Fake.ItemAsync("9900702", "【作り物】絵の無い髪型", images: 0);
            var day = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(9));
            await context.Seed.Notifications.SaveAsync(
            [
                new NotificationRecord
                {
                    Id = "viewshot-pic-1",
                    Kind = NotificationKind.ItemUpdated,
                    ItemId = dressed.Id,
                    Title = "作り物の衣装セット",
                    Detail = "価格が変わりました。",
                    Diffs = [new NotificationDiff { Field = "価格", Before = "¥ 1,500", After = "¥ 1,800" }],
                    CreatedAt = day,
                },
                new NotificationRecord
                {
                    Id = "viewshot-pic-2",
                    Kind = NotificationKind.ItemUpdated,
                    ItemId = dressed.Id,
                    // 幅 900 で題が折り返す長さ。絵が題の行に上をそろえたまま、行が下へ伸びるかを見る
                    Title = "作り物の衣装セット【複数アバター対応】春夏秋冬の4着とアクセサリー一式・テクスチャ差分つき・改変用の素材も同梱した長い名前の商品",
                    Detail = "商品名が変わりました。",
                    Diffs =
                    [
                        new NotificationDiff { Field = "商品名", Before = "作り物の衣装セット", After = "作り物の衣装セット【複数アバター対応】" },
                        new NotificationDiff { Field = "価格", Before = "¥ 1,800", After = "¥ 2,000" },
                    ],
                    CreatedAt = day.AddHours(-3),
                    IsRead = true,
                },
                new NotificationRecord
                {
                    Id = "viewshot-pic-3",
                    Kind = NotificationKind.ItemUpdated,
                    ItemId = bare.Id,
                    Title = "【作り物】絵の無い髪型",
                    Detail = "説明が変わりました。",
                    CreatedAt = day.AddHours(-5),
                },
                new NotificationRecord
                {
                    Id = "viewshot-pic-4",
                    Kind = NotificationKind.ItemUpdated,
                    ItemId = "9900799",
                    Title = "消えた作り物の小物",
                    Detail = "価格が変わりました。",
                    CreatedAt = day.AddHours(-7),
                },
                new NotificationRecord
                {
                    Id = "viewshot-pic-5",
                    Kind = NotificationKind.HandEditMismatch,
                    Title = "手で直したJSONに食い違いがあります",
                    Detail = "同じ名前のユーザータグが2つあります：作り物のタグ",
                    CreatedAt = day.AddHours(-9),
                    IsStrong = true,
                },
            ]);

            var main = await context.StartAsync();
            main.ShowInboxCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var inbox = context.Screen<InboxViewModel>();
            inbox.UnreadOnly = false;
            await SceneContext.UntilAsync(() => inbox.Lines.OfType<InboxRowLine>().Count() == 5, "知らせの行が5つ並ぶ");
            await context.SettleAsync();
            return new Shot(root);
        }),

        new Scene("perf-inbox-500", "計測：知らせ500件（商品ごとに1件・全部に絵）を開いて流す", context => InboxPerfAsync(context, 500, images: true)),
        new Scene("perf-inbox-500-noimages", "計測：知らせ500件（商品に絵が無い。枠と頭文字だけ）", context => InboxPerfAsync(context, 500, images: false)),
    ];

    private static async Task<Shot> InboxPerfAsync(SceneContext context, int count, bool images)
    {
        var day = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(9));
        var records = new List<NotificationRecord>();
        for (var i = 0; i < count; i++)
        {
            var id = $"99{i:00000}";
            await context.Fake.ItemAsync(id, $"作り物の衣装{i:00000}", images: images ? 1 : 0);
            records.Add(new NotificationRecord
            {
                Id = $"viewshot-perf-{i}",
                Kind = NotificationKind.ItemUpdated,
                ItemId = id,
                Title = $"作り物の衣装{i:00000}",
                Detail = "価格が変わりました。",
                Diffs = [new NotificationDiff { Field = "価格", Before = "¥ 1,500", After = "¥ 1,800" }],
                CreatedAt = day.AddMinutes(-i),
            });
        }

        await context.Seed.Notifications.SaveAsync(records);

        var main = await context.StartAsync();
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await context.SettleAsync();
        var before = PrivateMb();

        var clock = Stopwatch.StartNew();
        main.ShowInboxCommand.Execute(null);
        var inbox = context.Screen<InboxViewModel>();
        inbox.UnreadOnly = false;
        await SceneContext.UntilAsync(() => inbox.Lines.OfType<InboxRowLine>().Count() == count, "知らせの行が並ぶ");
        var view = Look.View<InboxView>(root) ?? throw new InvalidOperationException("通知の画面が見つかりません。");
        view.UpdateLayout();
        var opened = clock.ElapsedMilliseconds;
        await Stage.IdleAsync();
        Console.WriteLine($"PERF open count={count} images={images} layout_ms={opened} idle_ms={clock.ElapsedMilliseconds} mem_mb={PrivateMb()} before_mb={before}");

        var scroll = Look.All<ScrollViewer>(view).First(candidate => candidate.ScrollableHeight > 0);
        var steps = new List<double>();
        var step = Stopwatch.StartNew();
        while (steps.Count < 300 && scroll.VerticalOffset < scroll.ScrollableHeight - 0.5)
        {
            step.Restart();
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 120);
            view.UpdateLayout();
            steps.Add(step.Elapsed.TotalMilliseconds);
        }

        // 裏で読んだ絵が届き切るのを待ってから、メモリを読む
        await context.SettleAsync();
        Console.WriteLine(FormattableString.Invariant(
            $"PERF scroll extent={scroll.ExtentHeight:F0} steps={steps.Count} total_ms={steps.Sum():F0} max_ms={(steps.Count == 0 ? 0 : steps.Max()):F1} over33={steps.Count(ms => ms > 33)}"));
        Console.WriteLine($"PERF mem end_mb={PrivateMb()}");
        return new Shot(root);
    }
}
