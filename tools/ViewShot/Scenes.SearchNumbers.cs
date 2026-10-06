using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// 検索の数と日付の条件（2026-10-06・メモ82〜84：価格の「すべての価格が範囲内」・入手日の「最初の購入のみ／すべての購入」・属性の分布の帯と1%刻み）と、
/// 編集画面の属性の1%刻み。カレンダーは吹き出し（別の窓）なので描けない（月の合わせ方は試験で確かめる）。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> SearchNumberScenes =>
    [
        new Scene("search-price-match-all", "検索の絞り込み：価格（BOOTHの価格）で「すべての価格が範囲内の商品のみ」を入れ、外れ値の文が出た所", async context =>
        {
            var search = await StartNumbersAsync(context, (index, item) => item with
            {
                Booth = item.Booth with
                {
                    Variations = index == 0
                        ? [new BoothVariation { Id = 1, Price = 1000 }, new BoothVariation { Id = 2, Price = 99999 }]
                        : [new BoothVariation { Id = 1, Price = 0 }, new BoothVariation { Id = 2, Price = 500 + (index * 150) }],
                },
            });

            var price = (RangeModule)AddModule(search, SearchModuleKind.Price);
            price.Source = price.Sources.First(option => option.Key == "booth");
            price.MaxText = "1500";
            price.MatchAll = true;
            await context.SettleAsync();
            return FiltersShot(context.MainWindow());
        }),

        new Scene("search-dates", "検索の絞り込み：公開日と入手日（入手日は「最初の購入のみ」の選び欄つき）", async context =>
        {
            var start = new DateOnly(2024, 1, 10);
            var search = await StartNumbersAsync(context, (index, item) => item with
            {
                Booth = item.Booth with { PublishedAt = new DateTimeOffset(start.AddDays(index * 40).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9)) },
                Local = item.Local with
                {
                    AcquiredAt = start.AddDays((index * 45) + 3),
                    Purchases = index % 3 == 0
                        ? [new Purchase { VariationId = 1, Price = 500 }, new Purchase { VariationId = 2, Price = 300, PurchasedAt = start.AddDays((index * 45) + 200) }]
                        : [],
                },
            });

            AddModule(search, SearchModuleKind.PublishedAt);
            AddModule(search, SearchModuleKind.AcquiredAt);
            await context.SettleAsync();
            return FiltersShot(context.MainWindow());
        }),

        new Scene("search-attribute", "検索の絞り込み：属性2つ（分布の帯・1%刻みの範囲・AND の切り替え）", async context =>
        {
            await context.Seed.Attributes.SaveAsync(new AttributeMaster
            {
                Attributes = [new AttributeDefinition { Name = "質感" }, new AttributeDefinition { Name = "かわいい" }],
            });
            int[] texture = [12, 37, 41, 44, 58, 63, 67, 71, 72, 88, 93, 100];
            int[] cute = [5, 20, 25, 50, 55, 80];
            var search = await StartNumbersAsync(context, (index, item) => item with
            {
                Local = item.Local with
                {
                    Attributes = index < cute.Length
                        ? new Dictionary<string, int> { ["質感"] = texture[index], ["かわいい"] = cute[index] }
                        : new Dictionary<string, int> { ["質感"] = texture[index] },
                },
            });

            var attribute = (AttributeModule)AddModule(search, SearchModuleKind.Attribute);
            attribute.AddRow("質感");
            attribute.AddRow("かわいい");
            attribute.Rows[0].Min = 37;
            attribute.Rows[0].Max = 72;
            await context.SettleAsync();
            return FiltersShot(context.MainWindow());
        }),

        new Scene("search-recent", "検索の絞り込み：最近（商品ページを開いた・一か月の帯・7〜28日前・新しい順に並べた後）", async context =>
        {
            var search = await StartRecentAsync(context);
            var recent = (RecentModule)AddModule(search, SearchModuleKind.Recent);
            recent.Selected = recent.Options.First(option => option.Key == "viewed");
            recent.LowPosition = 7 * 100.0 / recent.Span;
            recent.HighPosition = 28 * 100.0 / recent.Span;
            recent.SortCommand.Execute(null);
            await context.SettleAsync();
            // 窓ごと撮る：表示順の欄に「商品ページを開いた日」が入り切るかも見る
            return new Shot(context.MainWindow());
        }),

        new Scene("search-recent-week", "検索の絞り込み：最近（一週間の帯・右端の「それより前」の1本・除く）", async context =>
        {
            var search = await StartRecentAsync(context);
            var recent = (RecentModule)AddModule(search, SearchModuleKind.Recent);
            recent.Selected = recent.Options.First(option => option.Key == "viewed");
            recent.Period = RecentPeriod.Week;
            recent.HighPosition = 2 * 100.0 / recent.Span;
            recent.IsExcluded = true;
            await context.SettleAsync();
            return FiltersShot(context.MainWindow());
        }),

        new Scene("edit-attributes", "編集画面：属性の欄（1%刻みの値 37%・62%・100%と、並べてあるだけの行）", async context =>
        {
            await context.Seed.Attributes.SaveAsync(new AttributeMaster
            {
                Attributes = [new AttributeDefinition { Name = "質感" }, new AttributeDefinition { Name = "かわいい" }, new AttributeDefinition { Name = "軽さ" }],
            });
            var item = await context.Fake.ItemAsync(
                "9900403",
                "作り物の衣装（属性の確かめ）",
                record => record with
                {
                    Local = record.Local with { Attributes = new Dictionary<string, int> { ["質感"] = 37, ["かわいい"] = 62, ["軽さ"] = 100 } },
                });

            var main = await context.StartAsync();
            await main.ShowEditAsync([item.Id]);
            var root = context.MainWindow();
            await context.PresentAsync(root);
            return new Shot(root) { Focus = () => Look.View<EditView>(root), FocusMargin = 0 };
        }),
    ];

    /// <summary>撮る絵が日によって変わらないよう、「最近」の今を止める。</summary>
    private static readonly DateTimeOffset RecentNow = new(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(9));

    /// <summary>作り物の12件に「商品ページを開いた」足跡（今日〜200日前）を付けて検索を開く。1件は記録なし。</summary>
    private static async Task<SearchViewModel> StartRecentAsync(SceneContext context)
    {
        int[] days = [0, 0, 1, 2, 3, 5, 9, 12, 20, 26, 45];
        await context.Seed.Recent.SaveAsync(new Chmonos.Core.Services.RecentLog
        {
            Entries =
            [
                .. days.Select((ago, index) => new Chmonos.Core.Services.RecentEntry
                {
                    ItemId = (9900301 + index).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ViewedAt = RecentNow.AddDays(-ago).AddHours(-2),
                    AddedAt = RecentNow.AddDays(-200),
                }),
            ],
        });

        var search = await StartNumbersAsync(context, (_, item) => item);
        search.Clock = () => RecentNow;
        return search;
    }

    /// <summary>作り物の12件を置いて検索を開き、既定の条件を外して空から始める。</summary>
    private static async Task<SearchViewModel> StartNumbersAsync(SceneContext context, Func<int, ItemRecord, ItemRecord> change)
    {
        await SeedLibraryAsync(context, count: 12, change);
        var main = await context.StartAsync();
        await context.PresentAsync(context.MainWindow());
        await SceneContext.UntilAsync(() => main.Search.TotalCount == 12, "商品を読み終える");
        foreach (var module in main.Search.Modules.ToList())
        {
            module.RemoveCommand!.Execute(null);
        }

        return main.Search;
    }
}
