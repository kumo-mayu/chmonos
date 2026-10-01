using System.Windows;
using System.Windows.Controls;
using Chmonos.App.ViewModels;

namespace ViewShot;

/// <summary>
/// 検索の絞り込みの条件（2026-10-01：除く・同じ種類を複数・編集状況）。
/// 見たい所は絞り込みの欄（「＋ 条件を追加」から下）。「…」とメニューはマウスを乗せる・押すと出る物なので描けない（アプリで確かめる）。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> SearchFilters =>
    [
        new Scene("search-exclude", "検索の絞り込み：除いている条件（札「除く」・AND の要約・畳んだ姿）と、除かない条件", async context =>
        {
            var (search, root) = await StartFiltersAsync(context);

            var tags = (ListModule)AddModule(search, SearchModuleKind.BoothTag);
            tags.AddKey("衣装");
            tags.AddKey("夏");
            tags.MatchAll = true;
            tags.IsExcluded = true;

            var likes = (RangeModule)AddModule(search, SearchModuleKind.WishList);
            likes.MinText = "100";
            likes.MaxEnabled = false;
            likes.IsExcluded = true;
            likes.IsCollapsed = true;

            var category = AddModule(search, SearchModuleKind.Category);
            category.IsCollapsed = true;

            await context.SettleAsync();
            return FiltersShot(root);
        }),

        new Scene("search-many", "検索の絞り込み：同じ種類の条件が2つ（片方は除く）で、間に別の種類が挟まった並び", async context =>
        {
            var (search, root) = await StartFiltersAsync(context);
            await ManyAsync(search);
            await context.SettleAsync();
            return FiltersShot(root);
        }),

        new Scene("search-many-grouped", "検索の絞り込み：上の並びで「同じ種類の条件を隣に並べる」を押した後", async context =>
        {
            var (search, root) = await StartFiltersAsync(context);
            await ManyAsync(search);
            search.GroupModulesCommand.Execute(null);
            await context.SettleAsync();
            return FiltersShot(root);
        }),
    ];

    /// <summary>BOOTHタグ（衣装）→ スキ数 → BOOTHタグ（夏を除く）の並びを作る。</summary>
    private static Task ManyAsync(SearchViewModel search)
    {
        var include = (ListModule)AddModule(search, SearchModuleKind.BoothTag);
        include.AddKey("衣装");

        var likes = (RangeModule)AddModule(search, SearchModuleKind.WishList);
        likes.IsCollapsed = true;

        var exclude = (ListModule)AddModule(search, SearchModuleKind.BoothTag);
        exclude.AddKey("夏");
        exclude.IsExcluded = true;
        return Task.CompletedTask;
    }

    /// <summary>作り物の8件に BOOTH タグとスキ数を散らして検索を開き、既定の条件を外して空から始める。</summary>
    private static async Task<(SearchViewModel Search, FrameworkElement Root)> StartFiltersAsync(SceneContext context)
    {
        await SeedLibraryAsync(context, count: 8, (index, item) => item with
        {
            Booth = item.Booth with
            {
                Tags = index % 2 == 0 ? ["衣装", "夏"] : ["小物"],
                WishListsCount = index * 40,
            },
        });

        var main = await context.StartAsync();
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await SceneContext.UntilAsync(() => main.Search.TotalCount == 8, "商品を読み終える");

        foreach (var module in main.Search.Modules.ToList())
        {
            module.RemoveCommand!.Execute(null);
        }

        return (main.Search, root);
    }

    /// <summary>「＋ 条件を追加」から足す（人が押すのと同じ口）。</summary>
    private static SearchModule AddModule(SearchViewModel search, SearchModuleKind kind)
    {
        var before = search.Modules.ToList();
        search.ModuleMenu.SelectMany(heading => heading.Entries).OfType<SearchModuleMenuEntry>()
            .First(entry => entry.Kind == kind).AddCommand.Execute(null);
        return search.Modules.Except(before).Single();
    }

    /// <summary>絞り込みの欄（「＋ 条件を追加」から下）を切り出す。</summary>
    private static Shot FiltersShot(FrameworkElement root) => new(root)
    {
        Focus = () => Look.Ancestor<ScrollViewer>(Look.Named<FrameworkElement>(root, "ModulesList")),
        FocusMargin = 0,
    };
}
