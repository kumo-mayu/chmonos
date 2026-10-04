using System.Windows.Controls;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace ViewShot;

internal static partial class Scenes
{
    // 設定の画面は縦に長い。全体を1枚に収める高さにして、直す前と後で「ほかの欄が変わっていないか」を丸ごと比べられるようにする。
    // 高さを中身に合わせる（null）と、除外を全部並べていた頃の画面は 5,000 件で十数万 px になり描けない
    private const double SettingsHeight = 3000;

    private static IEnumerable<Scene> Settings =>
    [
        SettingsExcluded("settings-excluded-none", "設定の全体：管理対象から除外したファイルが 0 件", count: 0, open: false),
        SettingsExcluded("settings-excluded-few", "設定の全体：除外したファイルが 3 件・欄は閉じた形", count: 3, open: false),
        SettingsExcluded("settings-excluded-many", "設定の全体：除外したファイルが 5,000 件・欄は閉じた形", count: 5000, open: false),
        SettingsExcluded("settings-excluded-few-open", "設定の「隠したもの」：除外したファイルが 3 件・欄を開いた形", count: 3, open: true),
        SettingsExcluded("settings-excluded-many-open", "設定の「隠したもの」：除外したファイルが 5,000 件・欄を開いた形（一覧の中だけが流れる）", count: 5000, open: true),
        SettingsLongPaths(),
        SettingsSortDividers("settings-sort-dividers-on", "設定の「一覧と検索」：区切りの設定が入（入手日の子のチェックは押せる）", parent: true),
        SettingsSortDividers("settings-sort-dividers-off", "設定の「一覧と検索」：区切りの設定が切（入手日の子のチェックは押せず薄い）", parent: false),
        SettingsStore("settings-store-blocked", "設定の保存先：ほかの長い作業（対応アバターの検出）の間は、場所を変える・書き出し・戻すが押せず、理由の1行が出る", main =>
            main.BeginLongJob("対応アバターを検出しています", "この間、アバターの編集と取り込みの検出は待たされます", new CancellationTokenSource())),
        SettingsStore("settings-backup-exported", "設定の保存先：設定の画面で書き出しが終わった（データの欄のボタンの下に結果・横にエクスプローラで開く）", main =>
        {
            main.BeginStoreJob(StoreJobKind.Export, "バックアップを書き出しています…");
            main.EndStoreJob(new StoreJobOutcome("バックアップに 5,678 ファイル（1.2 GB）を書き出しました。", @"D:\作り物\Chmonos-backup-20261001-1200.zip"));
        }),
        SettingsReset("settings-reset-all", "設定の「データ」：すべての設定を既定に戻すボタン（確かめの窓は「キャンセル」で閉じた。窓の文は結果に出る）", done: false),
        SettingsReset("settings-reset-all-done", "設定：すべての設定を既定に戻した後（値が既定へ戻り、ボタンの下に結果の1行が出る）", done: true),
        SettingsNotes("settings-notes-before", "設定：操作の知らせが出る前（出る場所は前もって空けてある）", shown: false),
        SettingsNotes("settings-notes-after", "設定：知らせが出た後（欄の下・ボタンの下・見出しの横。ほかの物は動かない）", shown: true),
    ];

    /// <summary>
    /// 設定をいくつか既定から変えておき、「すべての設定を既定に戻す」を押す。確かめの窓は台が答える（done なら OK、そうでなければキャンセル）
    /// </summary>
    private static Scene SettingsReset(string name, string title, bool done)
        => new(name, title, async context =>
        {
            var main = await context.StartAsync();
            main.ShowSettings();
            var settings = context.Screen<SettingsViewModel>();
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => !settings.IsLoading, "設定を読み終わる");

            settings.ShowAdult = false;
            settings.RefreshIntervalDays = 30;
            settings.FetchIntervalMs = 9000;
            settings.SaveImages = false;
            settings.AssignShortcut(settings.ShortcutRows[0], "Ctrl+Shift+F9");
            await context.SettleAsync();

            var before = Chmonos.App.Services.Notice.Intercept;
            Chmonos.App.Services.Notice.Intercept = request =>
            {
                context.Notices.Add($"「{request.Caption}」{request.Text}");
                return done ? System.Windows.MessageBoxResult.OK : System.Windows.MessageBoxResult.Cancel;
            };
            settings.ResetAllSettingsCommand.Execute(null);
            if (done)
            {
                await SceneContext.UntilAsync(() => settings.ResetNote.Length > 0, "既定に戻し終わる");
            }

            Chmonos.App.Services.Notice.Intercept = before;
            await context.SettleAsync();
            Console.WriteLine($"  外れ値：表示 {settings.ShowAdult}・取り直す間隔 {settings.RefreshIntervalDays} 日・通信の間隔 {settings.FetchIntervalMs} ms・画像を保存 {settings.SaveImages}・取り込み元 {settings.Folders.Count} 件");

            return new Shot(root) { Focus = () => Look.View<SettingsView>(root), FocusMargin = 0 };
        })
        {
            Height = SettingsHeight,
        };

    /// <summary>
    /// 操作の結果の知らせを、欄・ボタン・見出しの近くへ出す前と後（2026-10-04）。2枚を比べて、下の物が動かないことを見る。
    /// 後の方は、数の欄を範囲の外に打ち、ショートカットを重ね、幅を戻し、除外を1件解除し、書き出しの結果を出す
    /// </summary>
    private static Scene SettingsNotes(string name, string title, bool shown)
        => new(name, title, async context =>
        {
            await SeedExcludedAsync(context, 3);
            var main = await context.StartAsync();
            main.ShowSettings();
            var settings = context.Screen<SettingsViewModel>();
            settings.IsExcludedExpanded = true;
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => !settings.IsLoading && settings.ExcludedText == "3 件", "設定を読み終わる");

            if (shown)
            {
                settings.RefreshIntervalDays = 1000;
                settings.NotificationRetentionCount = 1;
                settings.FetchIntervalMs = 1;
                settings.ImageMaxEdgePixels = 99999;
                settings.SearchHistoryCount = 500;
                settings.ResetPaneWidthsCommand.Execute(null);
                settings.AssignShortcut(settings.ShortcutRows[0], "Ctrl+Shift+F9");
                settings.AssignShortcut(settings.ShortcutRows[1], "Ctrl+Shift+F9");
                settings.Excluded[0].RestoreCommand!.Execute(null);
                main.BeginStoreJob(StoreJobKind.Export, "バックアップを書き出しています…");
                main.EndStoreJob(new StoreJobOutcome("バックアップに 5,678 ファイル（1.2 GB）を書き出しました。", @"D:\作り物\Chmonos-backup-20261001-1200.zip"));
                await SceneContext.UntilAsync(() => settings.ExcludedText == "2 件", "除外が1件減る");
            }

            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<SettingsView>(root), FocusMargin = 0 };
        })
        {
            Height = SettingsHeight,
        };

    /// <summary>設定の画面を開いてから、主画面に状態を入れ、上の1行と「データの保存先」の欄を描く。</summary>
    private static Scene SettingsStore(string name, string title, Action<MainViewModel> arrange)
        => new(name, title, async context =>
        {
            var main = await context.StartAsync();
            main.ShowSettings();
            var settings = context.Screen<SettingsViewModel>();
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => !settings.IsLoading, "設定を読み終わる");

            arrange(main);
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.View<SettingsView>(root), FocusMargin = 0 };
        })
        {
            Height = SettingsHeight,
        };

    /// <summary>
    /// 取り込み元と監視対象に、1行に収まらない長いパスと短いパスを並べる（公開前の点検 2026-10-01）。
    /// 長いパスは間が省かれ、最後のフォルダ名が見えること。どれも作り物の場所なので「見つかりません」が付く
    /// </summary>
    private static Scene SettingsLongPaths()
        => new("settings-long-paths", "設定の取り込み元・監視対象：長いパス（間を省いて最後のフォルダ名を残す）と短いパス", async context =>
        {
            string[] folders =
            [
                @"D:\作り物のフォルダ\とても長い名前のフォルダ（入れ子1）\さらに長い名前のフォルダ（入れ子2）\もっと深い所にあるダウンロードの置き場\VRChat用の素材\2026年9月に買った分",
                @"D:\作り物\短い",
            ];
            var main = await context.StartAsync(settings => settings with { ImportFolders = folders, WatchedFolders = folders });
            main.ShowSettings();
            var settings = context.Screen<SettingsViewModel>();
            settings.IsFoldersExpanded = true;
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => !settings.IsLoading && settings.Folders.Count == 2, "設定を読み終わる");
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.Ancestor<Border>(Look.Text(root, "監視対象フォルダ")) };
        });

    /// <summary>
    /// 並べ替えの区切りの親と、入手日の子のチェック（ユーザ判断 2026-10-01）。子は親の文字の頭に揃えて下がり、親が切れていれば薄い
    /// </summary>
    private static Scene SettingsSortDividers(string name, string title, bool parent)
        => new(name, title, async context =>
        {
            var main = await context.StartAsync(settings => settings with { ShowSortDividers = parent });
            main.ShowSettings();
            var settings = context.Screen<SettingsViewModel>();
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => !settings.IsLoading, "設定を読み終わる");
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.Ancestor<Border>(Look.Text(root, "一覧と検索")) };
        });

    private static Scene SettingsExcluded(string name, string title, int count, bool open)
        => new(name, title, async context =>
        {
            await SeedExcludedAsync(context, count);
            var main = await context.StartAsync();
            main.ShowSettings();
            var settings = context.Screen<SettingsViewModel>();

            // 開閉はアプリを閉じるまで覚える（静的）。場面はプロセスごとに分かれるので、ここで決めれば前の場面を持ち越さない
            settings.IsExcludedExpanded = open;
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => !settings.IsLoading, "設定を読み終わる");
            await SceneContext.UntilAsync(() => settings.ExcludedText == $"{count} 件", "除外の件数が出る");
            await context.SettleAsync();

            return open
                ? new Shot(root) { Focus = () => Look.Ancestor<Border>(Look.Text(root, "どれも普段の画面には表示されません")) }
                : new Shot(root) { Focus = () => Look.View<SettingsView>(root), FocusMargin = 0 };
        })
        {
            Height = open ? 800 : SettingsHeight,
        };

    /// <summary>
    /// 作り物の除外の記録を書く。新しい物ほど後ろ（除外した順に足される記録と同じ）。
    /// 3件に1件は理由の文を付け、行の2段目が出る物と出ない物を混ぜる。
    /// </summary>
    private static Task SeedExcludedAsync(SceneContext context, int count)
    {
        var start = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.FromHours(9));
        var entries = Enumerable.Range(0, count)
            .Select(index => new ExcludedEntry
            {
                Hash = Fake.Hex($"excluded-{index}"),
                Paths = [$@"D:\作り物の素材\テクスチャ集\texture_{index:00000}.png"],
                ExcludedAt = start.AddMinutes(index),
                Reason = index % 3 == 0 ? "BOOTHの商品ではない" : null,
            })
            .ToList();
        return context.Seed.Excluded.SaveAsync(entries);
    }
}
