using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using System.IO;
using Chmonos.Core.Storage;

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

        // 読めない商品の記録の題（2026-10-05）：控えから商品名を引けた行（札にファイル名）と、引けなかった行（題がファイル名）
        new Scene("inbox-unreadable", "通知：読めない商品の記録。題が商品名の行（札にファイル名）と、題がファイル名の行", async context =>
        {
            // 知らせは本物の検出に作らせる：控えの有る商品（題が商品名）と、控えを消した商品（題がファイル名）
            await context.Fake.ItemAsync("9900601", "作り物の衣装セット");
            await context.Fake.ItemAsync("9900602", "作り物の髪飾り");
            File.Delete(AppPaths.Default.ItemCopyFile("9900602"));
            foreach (var id in new[] { "9900601", "9900602" })
            {
                File.WriteAllText(AppPaths.Default.ItemFile(id), "{\n  \"id\": \"" + id + "\",\n  \"booth\": {\n");
            }

            var main = await context.StartAsync();
            main.ShowInboxCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var inbox = context.Screen<InboxViewModel>();
            await SceneContext.UntilAsync(() => inbox.Lines.OfType<InboxRowLine>().Count() == 2, "知らせの行が2つ並ぶ");
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

        // メモ51（2026-10-05）。「商品ページの変更」の束の見出しが画面の上に合い、その商品の行が先頭で強調され、
        // 下の束の見出しに「この商品 n」が出る所。指していない商品の行を先に並べ、送らないと見えない位置にしてある
        new Scene("inbox-target", "通知：商品を指して開いた所（束の見出しが上に合う・その商品の行を強調・ほかの束の見出しに印）", async context =>
        {
            var target = await context.Fake.ItemAsync("9900511", "作り物の衣装セット");
            var other = await context.Fake.ItemAsync("9900512", "作り物の髪飾り");
            var day = new DateTimeOffset(2026, 10, 4, 21, 0, 0, TimeSpan.FromHours(9));
            var records = new List<NotificationRecord>();
            for (var i = 0; i < 6; i++)
            {
                records.Add(new NotificationRecord
                {
                    Id = $"viewshot-target-other-{i}",
                    Kind = NotificationKind.OrphanTag,
                    Title = $"タグ「作り物のタグ{i}」がどの一覧にもありません",
                    Detail = "商品に付いていますが、タグの一覧から消えています。",
                    CreatedAt = day.AddMinutes(-i),
                });
            }

            for (var i = 0; i < 4; i++)
            {
                records.Add(new NotificationRecord
                {
                    Id = $"viewshot-target-up-{i}",
                    Kind = NotificationKind.ItemUpdated,
                    ItemId = other.Id,
                    Title = $"「作り物の髪飾り」が更新されました（{i + 1}件目）",
                    Detail = "価格が変わりました。",
                    Diffs = [new NotificationDiff { Field = "価格", Before = "¥ 1,500", After = "¥ 1,800" }],
                    CreatedAt = day.AddHours(-i),
                });
            }

            records.Add(new NotificationRecord
            {
                Id = "viewshot-target-up-read",
                Kind = NotificationKind.ItemUpdated,
                ItemId = target.Id,
                Title = "「作り物の衣装セット」が更新されました（読んだ行）",
                Detail = "説明が変わりました。",
                CreatedAt = day.AddHours(-30),
                IsRead = true,
            });
            records.Add(new NotificationRecord
            {
                Id = "viewshot-target-up-unread",
                Kind = NotificationKind.ItemUpdated,
                ItemId = target.Id,
                Title = "「作り物の衣装セット」が更新されました",
                Detail = "価格が変わりました。",
                Diffs = [new NotificationDiff { Field = "価格", Before = "¥ 2,000", After = "¥ 2,400" }],
                CreatedAt = day.AddHours(-40),
            });
            records.Add(new NotificationRecord
            {
                Id = "viewshot-target-back-other",
                Kind = NotificationKind.ItemBackOnBooth,
                ItemId = other.Id,
                Title = "「作り物の髪飾り」がBOOTHに戻りました",
                Detail = "商品情報を取り直せます。",
                CreatedAt = day.AddHours(-2),
            });
            records.Add(new NotificationRecord
            {
                Id = "viewshot-target-back",
                Kind = NotificationKind.ItemBackOnBooth,
                ItemId = target.Id,
                Title = "「作り物の衣装セット」がBOOTHに戻りました",
                Detail = "商品情報を取り直せます。",
                CreatedAt = day.AddHours(-50),
            });
            await context.Seed.Notifications.SaveAsync(records);

            var main = await context.StartAsync();
            main.ShowInboxFor(target.Id);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var inbox = context.Screen<InboxViewModel>();
            inbox.UnreadOnly = false;
            await SceneContext.UntilAsync(() => inbox.Lines.OfType<InboxRowLine>().Count() == records.Count, "知らせの行が並ぶ");
            await context.SettleAsync();
            return new Shot(root);
        }),

        // 保管した通知を読む見方を足したとき（2026-11-11）：上の段（戻る・件数・月・商品名で絞る）と、種類を日時の前に言う読むだけの行
        new Scene("inbox-archive", "通知：保管した通知の見方。月の見出しの下に新しい順の行（既読の丸と行のボタンが無く、種類を日時の前に）", async context =>
        {
            var item = await context.Fake.ItemAsync("9900502", "作り物のワンピース");
            var day = new DateTimeOffset(2026, 10, 20, 21, 0, 0, TimeSpan.FromHours(9));
            await context.Seed.NotificationArchive.AppendAsync(
            [
                new NotificationRecord
                {
                    Id = "archive-1",
                    Kind = NotificationKind.ItemUpdated,
                    ItemId = item.Id,
                    Title = "作り物のワンピース",
                    Detail = "価格が変わりました。",
                    Diffs = [new NotificationDiff { Field = "価格", Before = "¥ 1,500", After = "¥ 1,800" }],
                    CreatedAt = day.AddDays(-3),
                    IsRead = true,
                },
                new NotificationRecord
                {
                    Id = "archive-2",
                    Kind = NotificationKind.ItemBackOnBooth,
                    ItemId = item.Id,
                    Title = "作り物のワンピース",
                    Detail = "商品情報を取り直せます。",
                    CreatedAt = day.AddDays(-5),
                    IsRead = true,
                },
                new NotificationRecord
                {
                    Id = "archive-3",
                    Kind = NotificationKind.OrphanTag,
                    Title = "タグ「冬服」がどの一覧にもありません",
                    Detail = "ユーザータグ：冬服",
                    CreatedAt = day.AddDays(-9),
                    IsResolved = true,
                },
            ]);
            await context.Seed.NotificationArchive.AppendAsync(
            [
                new NotificationRecord
                {
                    Id = "archive-sep",
                    Kind = NotificationKind.ItemUpdated,
                    ItemId = item.Id,
                    Title = "作り物のワンピース",
                    Detail = "説明が変わりました。",
                    CreatedAt = day.AddMonths(-1),
                    IsRead = true,
                },
            ]);

            var main = await context.StartAsync();
            main.ShowInboxCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var inbox = context.Screen<InboxViewModel>();
            inbox.ShowArchiveCommand.Execute(null);
            inbox = context.Screen<InboxViewModel>();
            await SceneContext.UntilAsync(() => inbox.Archive?.Lines.OfType<InboxRowLine>().Count() == 3, "保管した通知の行が3つ並ぶ");
            await context.SettleAsync();
            return new Shot(root);
        }),

        // 保管した通知が1つも無いとき（保管しない設定）。空の表示が並ぶようにする方法を言うことを見る
        new Scene("inbox-archive-empty", "通知：保管した通知が無いとき（保管の容量が 0）の空の表示", async context =>
        {
            var main = await context.StartAsync(settings => settings with { NotificationArchiveMegabytes = 0 });
            main.ShowInboxCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var inbox = context.Screen<InboxViewModel>();
            inbox.ShowArchiveCommand.Execute(null);
            inbox = context.Screen<InboxViewModel>();
            await SceneContext.UntilAsync(() => inbox.Archive?.IsEmpty == true && inbox.Archive.EmptyTitle == "保管した通知はありません", "空の表示が出る");
            await context.SettleAsync();
            return new Shot(root);
        }),

        // 流して続きを読む配線を、実際の一覧で確かめる（2026-11-11）：落ち着いた後は100件のまま（行の中の部品のスクロールの知らせで読み進めない）、
        // 終わりまで流して初めて続きの100件を読む。数が合わなければ撮らずに止まる
        new Scene("inbox-archive-scroll", "通知：保管した通知250件を終わりまで流し、続きの100件を読んだ所", async context =>
        {
            var day = new DateTimeOffset(2026, 10, 20, 21, 0, 0, TimeSpan.FromHours(9));
            await context.Seed.NotificationArchive.AppendAsync(
                Enumerable.Range(0, 250).Select(i => new NotificationRecord
                {
                    Id = $"archive-{i:D3}",
                    Kind = NotificationKind.ItemUpdated,
                    Title = $"作り物の衣装 {i:D3}",
                    Detail = "価格：500 → 800",
                    CreatedAt = day.AddMinutes(-i),
                    IsRead = true,
                }).ToList());

            var main = await context.StartAsync();
            main.ShowInboxCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            context.Screen<InboxViewModel>().ShowArchiveCommand.Execute(null);
            var archive = context.Screen<InboxViewModel>().Archive!;
            await SceneContext.UntilAsync(() => archive.Rows.Count == 100, "最初の100件が並ぶ");
            await context.SettleAsync();
            await context.PresentAsync(root);
            await context.SettleAsync();
            if (archive.Rows.Count != 100)
            {
                throw new InvalidOperationException($"流していないのに続きを読んだ（{archive.Rows.Count} 件）");
            }

            var view = Look.View<Chmonos.App.Views.InboxView>(root) ?? throw new InvalidOperationException("通知の画面が見つからない");
            var list = view is not null
                ? Look.Named<Chmonos.App.Controls.ContentItemsControl>(view, "ArchiveList")
                : null;
            var scroll = list?.ScrollHost ?? throw new InvalidOperationException("保管した通知の一覧が見つからない");
            scroll.ScrollToEnd();
            await context.PresentAsync(root);
            // 終わりへ跳ぶと、足した行の高さが見積もりから実際の高さに替わる間も終わりの近くに居るので、もう1回読むことがある（250件なら全部）
            await SceneContext.UntilAsync(() => archive.Rows.Count >= 200 && !archive.IsLoadingMore, "続きの100件を読む");
            await context.SettleAsync();
            await context.PresentAsync(root);
            if (archive.Rows.Count < 200)
            {
                throw new InvalidOperationException($"終わりまで流しても続きを読まない（{archive.Rows.Count} 件）");
            }

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
