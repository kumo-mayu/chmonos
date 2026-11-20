using System.Windows.Controls;

namespace ViewShot;

/// <summary>
/// 検索のリストの列の見出しの右クリック（ユーザのメモ 2026-11-20：「この列を非表示」を足した）。
/// メニューはポップアップで台では描けないので、見出しの右クリックで開いたときと同じ中身を作り、メニューごと面の上に置いて描く。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> ListColumnMenuScenes =>
    [
        new Scene("search-list-column-menu", "検索のリストの見出し「属性」を右クリックしたメニュー：「この列を左へ」「この列を右へ」「この列を非表示」と列のサブメニュー", async context =>
        {
            await SeedCardInfoLibraryAsync(context, 40);
            var main = await context.StartAsync();
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => main.Search.TotalCount == 40, "商品を読み終える");
            main.Search.IsListMode = true;
            await context.SettleAsync();

            var header = Look.All<GridViewColumnHeader>(root).First(candidate => candidate.Column?.Header as string == "属性");
            var view = Look.All<ListView>(root).Select(list => list.View).OfType<GridView>().First(grid => grid.Columns.Contains(header.Column));
            var menu = view.ColumnHeaderContextMenu;
            menu.PlacementTarget = header;
            menu.IsOpen = true;
            await context.SettleAsync();

            // ContextMenu は親を持てない（面に置けない）ので、開いたポップアップの姿を VisualBrush で写して面の上に置く
            var picture = new System.Windows.Shapes.Rectangle
            {
                Width = menu.ActualWidth,
                Height = menu.ActualHeight,
                Fill = new System.Windows.Media.VisualBrush(menu) { Stretch = System.Windows.Media.Stretch.None },
            };
            var shot = SceneContext.OnSurface(picture, 20);
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
