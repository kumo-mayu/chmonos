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
        SettingsStore("settings-store-blocked", "設定の保存先：ほかの長い作業（対応アバターの検出）の間は、場所を変える・書き出し・戻すが押せず、理由の1行が出る", main =>
            main.BeginLongJob("対応アバターを検出しています", "この間、アバターの編集と取り込みの検出は待たされます", new CancellationTokenSource())),
        SettingsStore("settings-backup-exported", "設定の保存先：設定の画面で書き出しが終わった（上の1行に結果・書き出しの横にエクスプローラで開く）", main =>
        {
            main.BeginStoreJob(StoreJobKind.Export, "バックアップを書き出しています…");
            main.EndStoreJob(new StoreJobOutcome("バックアップに 5,678 ファイル（1.2 GB）を書き出しました。", @"D:\作り物\Chmonos-backup-20261001-1200.zip"));
        }),
    ];

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
