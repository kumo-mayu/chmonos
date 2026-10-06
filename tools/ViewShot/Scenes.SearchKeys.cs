using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Chmonos.App.Views;

namespace ViewShot;

/// <summary>
/// 検索のキーと右クリックの見た目（2026-10-06）：絞り込みのつまみに止まった印（丸い枠）と、商品のカードの右クリックのメニューの並びと区切り。
/// メニューはポップアップで台では描けないので、項目をメニューから外して、同じ型の下の段の入れ物に並べる（表示順のメニューの場面と同じ）。
/// 項目の名前・区切り・押せない項目のグレーは画面と同じ束縛のまま（押す先の命令だけはメニューの宛先から引けないので結ばれない）
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> SearchKeyScenes =>
    [
        new Scene("search-toggle-focus", "検索：絞り込みのつまみ（絞り込みと結果の境）にキーボードで止まった印", async context =>
        {
            await SeedLibraryAsync(context, count: 3);
            await context.StartAsync();
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var toggle = Look.All<Button>(root).First(button => AutomationProperties.GetAutomationId(button) == "SearchToggleFilterPanel");
            FocusPreview.Show(toggle, "PanelToggleFocusVisual", toggle);
            await context.SettleAsync();
            return new Shot(root)
            {
                Focus = () => toggle.Parent as FrameworkElement,
                FocusMargin = 40,
            };
        }),

        new Scene("card-menu", "商品のカードの右クリックのメニュー：ファイルのある商品（6つの群と区切り。更新が無いので更新の2つはグレー）", async context =>
        {
            await SeedLibraryAsync(context, count: 1);
            var main = await context.StartAsync();
            await SceneContext.UntilAsync(() => main.Search.TotalCount == 1, "商品を読み終える");
            var card = main.Search.ListItems.First();

            // 項目は1つのメニューの子なので、別に組んだ資源のメニューから外して下の段の入れ物へ移す
            var original = (ContextMenu)new ItemCardResources()["CardMenu"];
            var items = original.Items.Cast<object>().ToList();
            original.Items.Clear();
            var menu = new MenuItem { Header = "右クリック", DataContext = card };
            foreach (var item in items)
            {
                menu.Items.Add(item);
            }

            var host = new StackPanel { Orientation = Orientation.Horizontal };
            host.Children.Add(Column("商品のカードの右クリック", Detach(menu)));
            var shot = SceneContext.OnSurface(host, 20);
            await context.PresentAsync(shot);
            await context.SettleAsync();
            return new Shot(shot);
        })
        {
            Width = null,
            Height = null,
        },
    ];
}
