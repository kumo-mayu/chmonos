using System.Windows;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace ViewShot;

/// <summary>
/// 保存した検索の節（ユーザ判断 2026-10-04・案A3）：0件・数件（今の検索と同じ行に地の色）・多いとき（探す欄と節の中だけ流す）・畳んだ姿。
/// 行の「…」とメニュー・保存の小窓はマウスで出す物なので描けない（アプリで確かめる）。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> SavedSearchScenes =>
    [
        SavedScene("search-saved-none", "保存した検索：まだ1件も無い（節の見出しと「＋ 今の検索を保存」・空の文）", count: 0, current: false, collapse: false),
        SavedScene("search-saved-some", "保存した検索：3件。今の検索と同じ1件の地の色が変わる", count: 3, current: true, collapse: false),
        SavedScene("search-saved-many", "保存した検索：12件。探す欄が出て、5件ぶんの高さで節の中だけ流す", count: 12, current: true, collapse: false),
        SavedScene("search-saved-collapsed", "保存した検索：節を畳んだ姿。今の検索と同じ物の名前が見出しに出る", count: 3, current: true, collapse: true),
    ];

    private static readonly string[] SavedNames =
    [
        "夏の衣装（持っている物）", "改変待ち", "タグを付けていない物", "かわいい80以上", "シェーダーとツール", "安いアクセサリ",
        "冬の髪型", "お気に入りのショップの新作", "撮影用の小物", "まだ使っていない物", "リストで見る衣装", "とても長い名前の保存した検索で一行に収まらない物",
    ];

    private static Scene SavedScene(string name, string description, int count, bool current, bool collapse) => new(name, description, async context =>
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

        if (collapse && !main.Search.IsSavedSectionCollapsed)
        {
            main.Search.ToggleSavedSectionCommand.Execute(null);
        }

        await context.SettleAsync();
        return new Shot(root);
    });
}
