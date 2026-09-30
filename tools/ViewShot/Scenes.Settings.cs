using System.Windows.Controls;
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.App.Views;
using BoothAssetManager.Core.Models;

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
    ];

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
