using System.Windows;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace ViewShot;

/// <summary>
/// 保存した条件（ユーザ判断 2026-10-05・メモ36。「条件を追加」の隣のボタンから開く一覧）：0件・数件（今の検索と同じ行に ✓ と地の色）・多いとき（探す欄と一覧の中だけ流す）・閉じた姿。
/// 一覧はポップアップ（別の窓）で撮れないので、ポップアップの中身だけを外して並べる（開いた一覧の姿。ボタンとの位置は撮れない）。
/// 閉じた姿は主の窓の中でボタンを撮る。
/// 行の「…」とメニュー・保存の小窓はマウスで出す物なので描けない（アプリで確かめる）。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> SavedSearchScenes =>
    [
        SavedScene("search-saved-none", "保存した条件：まだ1件も無い（一覧の見出しと「＋ 今の検索を保存」・空の文）", count: 0, current: false, open: true),
        SavedScene("search-saved-some", "保存した条件：3件。今の検索と同じ1件に ✓ と地の色が付く", count: 3, current: true, open: true),
        SavedScene("search-saved-many", "保存した条件：12件。探す欄が出て、5件ぶんの高さで一覧の中だけ流す", count: 12, current: true, open: true),
        SavedScene("search-saved-closed", "保存した条件：閉じた姿。「条件を追加」の隣のボタンの末尾に、今の検索と同じ物がある点が出る", count: 3, current: true, open: false),
    ];

    private static readonly string[] SavedNames =
    [
        "夏の衣装（持っている物）", "改変待ち", "タグを付けていない物", "かわいい80以上", "シェーダーとツール", "安いアクセサリ",
        "冬の髪型", "お気に入りのショップの新作", "撮影用の小物", "まだ使っていない物", "リストで見る衣装", "とても長い名前の保存した条件で一行に収まらない物",
    ];

    private static Scene SavedScene(string name, string description, int count, bool current, bool open) => new(name, description, async context =>
    {
        await SeedLibraryAsync(context, count: 8);
        await context.Seed.SavedSearches.SaveAsync(new SavedSearchList
        {
            Entries = SavedNames.Take(count).Select((saved, index) => new SearchHistoryEntry
            {
                Name = saved,
                Text = index == 0 ? "作り物" : $"語{index}",
                View = index == 10 ? ResultView.List : ResultView.Card,
            }).ToList(),
        });

        var main = await context.StartAsync();
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await SceneContext.UntilAsync(() => main.Search.TotalCount == 8, "商品を読み終える");

        foreach (var module in main.Search.Modules.ToList())
        {
            module.RemoveCommand!.Execute(null);
        }

        if (current)
        {
            main.Search.QueryText = "作り物";
        }

        await context.SettleAsync();

        var view = Look.View<SearchView>(root) ?? throw new InvalidOperationException("検索の画面がない");
        var button = (FrameworkElement)view.FindName("SavedSearchesButton");
        if (open)
        {
            var popup = (System.Windows.Controls.Primitives.Popup)view.FindName("SavedSearchPopup");
            var panel = (FrameworkElement)popup.Child;
            popup.Child = null;
            panel.DataContext = main.Search;
            return new Shot(SceneContext.OnSurface(panel));
        }

        return new Shot(root)
        {
            Focus = () => button.Parent is FrameworkElement strip && strip.Parent is FrameworkElement column ? column : button,
            FocusMargin = 60,
        };
    })
    {
        // 開いた一覧は部品1つだけを描くので、中身の大きさに合わせる
        Width = open ? null : 1280,
        Height = open ? null : 800,
    };
}
