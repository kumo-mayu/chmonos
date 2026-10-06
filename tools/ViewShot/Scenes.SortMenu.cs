using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// 検索の表示順のボタンが開くメニュー（ユーザ判断 2026-10-06：「属性 ▸」の子に属性が入る形）。
/// メニューはポップアップで台では描けないので、ボタンの ContextMenu をボタンから外して並べ、「属性 ▸」の下の段は型のポップアップから外して右に並べる
/// （「＋ 条件を追加」の場面と同じ。項目の名前・区切り線・印・グレーは画面と同じ型と束縛のまま）。
/// 下の段は、画面ではポップアップが画面の高さで止まって中が流れる。台では止まらないので、高さを決めて流れる姿を見る
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> SortMenuScenes =>
    [
        new Scene("search-sort-menu", "検索の表示順のメニュー：属性40個で属性の1つで並べた所（左：メニュー、右：属性 ▸ の下の段を高さ360で流す姿）", async context =>
        {
            await context.Seed.Attributes.SaveAsync(new AttributeMaster
            {
                Attributes = Enumerable.Range(1, 40).Select(index => new AttributeDefinition { Name = $"作り物の属性{index:00}" }).ToList(),
            });
            var (search, host, menu, surface) = await StartSortMenuAsync(context);
            search.SortField = search.SortFields.First(sortField => sortField.AttributeName == "作り物の属性03");
            await context.SettleAsync();

            var attributes = search.SortMenu.Single(entry => entry.IsParent);
            if (menu.ItemContainerGenerator.ContainerFromIndex(search.SortMenu.Count - 1) is not MenuItem parent)
            {
                throw new InvalidOperationException($"「{attributes.Label}」の項目が作られていません。");
            }

            var submenu = (FrameworkElement)Detach(parent);
            submenu.MaxHeight = 360;
            host.Children.Add(Column("属性 ▸ の下の段", submenu));
            await context.SettleAsync();
            return new Shot(surface);
        })
        {
            Width = null,
            Height = null,
        },

        new Scene("search-sort-menu-no-attributes", "検索の表示順のメニュー：属性が0個（属性 ▸ は押せずグレー）・入手日で並べた所", async context =>
        {
            var (_, _, _, surface) = await StartSortMenuAsync(context);
            return new Shot(surface);
        })
        {
            Width = null,
            Height = null,
        },
    ];

    private static async Task<(SearchViewModel Search, StackPanel Host, MenuItem Menu, FrameworkElement Surface)> StartSortMenuAsync(SceneContext context)
    {
        await SeedLibraryAsync(context, count: 3);
        var main = await context.StartAsync();
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await SceneContext.UntilAsync(() => main.Search.TotalCount == 3, "商品を読み終える");

        var button = Look.All<Button>(root).First(item => AutomationProperties.GetAutomationId(item) == "SearchSortField");
        // ContextMenu は木に載せられない（親を持てない）。同じ項目の型と同じ値の結び付きを持つ下の段の項目を作り、
        // その下の段の入れ物（メニューと同じ色の枠・流す入れ物・項目）を外して載せる。中の項目は ContextMenu の中と同じ型（下の段の項目）になる
        var original = button.ContextMenu ?? throw new InvalidOperationException("表示順のボタンにメニューがありません。");
        var menu = new MenuItem { Header = "表示順", ItemContainerStyle = original.ItemContainerStyle, DataContext = main.Search };
        menu.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding(nameof(SearchViewModel.SortMenu)));

        var host = new StackPanel { Orientation = Orientation.Horizontal };
        host.Children.Add(Column("表示順", Detach(menu)));
        var shot = SceneContext.OnSurface(host, 20);
        await context.PresentAsync(shot);
        await context.SettleAsync();
        return (main.Search, host, menu, shot);
    }
}
