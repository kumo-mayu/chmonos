using System.Windows;
using System.Windows.Controls;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

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

        new Scene("search-edit-status", "検索の絞り込み：編集状況（未入力のみ・項目2つ・件数付き・すべての印）", async context =>
        {
            var (search, root) = await StartFiltersAsync(context);
            var status = (UneditedModule)AddModule(search, SearchModuleKind.Unedited);
            status.Fields.First(toggle => toggle.Field == Chmonos.Core.Services.EditField.Memo).IsOn = true;
            await context.SettleAsync();
            return FiltersShot(root);
        }),

        new Scene("search-edit-status-both", "検索の絞り込み：編集状況を「両方」にした所（項目のチェックとつなぎ方が薄くなって押せない）", async context =>
        {
            var (search, root) = await StartFiltersAsync(context);
            var status = (UneditedModule)AddModule(search, SearchModuleKind.Unedited);
            status.Fields.First(toggle => toggle.Field == Chmonos.Core.Services.EditField.Memo).IsOn = true;
            status.Selected = status.Options.First(option => option.Key == "both");
            await context.SettleAsync();
            return FiltersShot(root);
        }),

        UpdatedScene("search-updated", "検索：条件「更新あり」で絞った結果（未読の更新がある2件のカードの札「更新あり」）", list: false),
        UpdatedScene("search-updated-list", "検索：条件「更新あり」で絞ったリスト（行の札「更新あり」）", list: true),
        UpdatedScene("search-selected-updated", "検索：未読の更新がある1件を含む3件を選んだ下の帯（「既読にする」が出る）", list: false, select: true),
        UpdatedScene("search-selected-quiet", "検索：未読の更新が無い2件だけを選んだ下の帯（「既読にする」は出ない）", list: false, select: true, quiet: true),
    ];

    /// <summary>
    /// 未読の更新がある商品（2026-10-02）：条件「更新あり」（足したときの「更新ありのみ」）で絞った結果と、カード・行の札「更新あり」。
    /// 8件のうち2件に未読の更新、1件に既読の更新を置く（既読の物には札が出ない）
    /// </summary>
    /// <param name="select">条件で絞らず、カードを選んで下の帯を出す（2026-10-02 のまとめて既読にする）。</param>
    /// <param name="quiet">選ぶのを未読の更新が無い商品だけにする。</param>
    private static Scene UpdatedScene(string name, string description, bool list, bool select = false, bool quiet = false) => new(name, description, async context =>
    {
        await SeedLibraryAsync(context, count: 8);
        NotificationRecord Updated(string id, string itemId, bool isRead = false) => new()
        {
            Id = id,
            Kind = NotificationKind.ItemUpdated,
            Title = "作り物の更新",
            Detail = string.Empty,
            ItemId = itemId,
            IsRead = isRead,
            CreatedAt = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.FromHours(9)),
        };
        await context.Seed.Notifications.SaveAsync([Updated("n1", "9900301"), Updated("n2", "9900303"), Updated("n3", "9900304", isRead: true)]);

        var main = await context.StartAsync();
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await SceneContext.UntilAsync(() => main.Search.TotalCount == 8, "商品を読み終える");
        foreach (var module in main.Search.Modules.ToList())
        {
            module.RemoveCommand!.Execute(null);
        }

        if (select)
        {
            string[] picks = quiet ? ["9900302", "9900304"] : ["9900301", "9900302", "9900304"];
            foreach (var card in main.Search.ListItems.Where(card => picks.Contains(card.Item.Id)))
            {
                card.IsSelected = true;
            }
        }
        else
        {
            AddModule(main.Search, SearchModuleKind.Updated);
        }

        main.Search.IsListMode = list;
        await context.SettleAsync();
        return new Shot(root);
    });

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
