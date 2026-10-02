using System.Windows;
using System.Windows.Controls;
using Chmonos.App.ViewModels;

namespace ViewShot;

/// <summary>
/// ナビ（左の列）。2026-10-02 のメモ6：低い窓でナビを畳むと、縦のスクロールバーが細い列の中身をつぶしていた。
/// メモ8：畳んだナビで「戻る」の左矢印と「ナビを開く」の右向きの山形が並び、紛らわしかった。
/// 低い窓（高さ 600）で開いた所と畳んだ所を描く。高さを変えて見るときは --height、幅は --width
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> Nav =>
    [
        NavScene("nav-low-open", "ナビ：低い窓（高さ 600）で開いた所（下の「設定」まで届くか・送る印）", collapsed: false),
        NavScene("nav-low-collapsed", "ナビ：低い窓（高さ 600）で畳んだ所（スクロールバーで中身がつぶれないか・開く印と戻るの矢印）", collapsed: true),
    ];

    private static Scene NavScene(string name, string title, bool collapsed)
        => new(name, title, async context =>
        {
            await SeedLibraryAsync(context, count: 3);
            var main = await context.StartAsync();
            var root = context.MainWindow();
            await context.PresentAsync(root);

            // 戻るに行き先がある状態にする（畳んだナビで「戻る」と「開く」が並ぶ所を見る）
            main.ShowStatsCommand.Execute(null);
            await context.SettleAsync();
            main.ShowSearchCommand.Execute(null);
            if (collapsed)
            {
                main.ToggleNavCommand.Execute(null);
            }

            await context.SettleAsync();

            // ナビの列だけを切り出す（本文は描くたびに変わる物が無く、見る所でもない）
            return new Shot(root)
            {
                Focus = () => Look.All<Border>(root).FirstOrDefault(border => border.Name == "NavRail"),
                FocusMargin = 0,
            };
        })
        {
            Height = 600,
        };

    /// <summary>
    /// 編集画面の商品説明の欄（メモ4-①：高さを人が変えられるように）。説明の長い商品で、欄の下の縁のつまみが見える所まで送る
    /// </summary>
    private static Scene EditDescription()
        => new("edit-description", "編集画面：説明の長い商品の「商品説明」の欄（高さの上限と、下の縁の高さを変えるつまみ）", async context =>
        {
            var item = await context.Fake.ItemAsync(
                "9900402",
                "作り物の衣装（編集画面の説明の確かめ）",
                record => record with { Booth = record.Booth! with { Description = LongBody("9900402") } });

            var main = await context.StartAsync();
            await main.ShowEditAsync([item.Id]);
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await context.SettleAsync();

            Look.Text(root, "商品説明")?.BringIntoView();
            await context.SettleAsync();

            return new Shot(root)
            {
                Focus = () => Look.Ancestor<Border>(Look.Ancestor<StackPanel>(Look.Text(root, "商品説明"))),
                FocusMargin = 12,
            };
        });
}
