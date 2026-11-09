using Chmonos.App.ViewModels;

namespace ViewShot;

/// <summary>
/// 計測：商品ページを開いたときの最初の配置のうち、商品説明（見出しごとの選べる文字の箱）が占める時間（計画の 10 (2)・2026-11-09）。
/// 窓を出さずに描く台の中で、短い商品から説明の長い商品（本文と見出し21個）へ移り、最初の配置（UpdateLayout）までを測る。
/// 説明の欄を開いたままと畳んだままの差が、説明の分。後から足す分（画面の外の見出し・欄）は最初の配置に入らない。
/// 窓の大きさは --width・--height で変える。結果は「PERF」の行に出す。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> ItemOpenPerfScenes =>
    [
        new Scene("perf-item-description", "計測：説明の長い商品を開く最初の配置（説明を開いた・畳んだ）", ItemOpenPerfAsync),
    ];

    private const int ItemOpenRounds = 12;

    private static async Task<Shot> ItemOpenPerfAsync(SceneContext context)
    {
        var (longItem, shortItem) = await SeedLongItemAsync(context);
        var main = await context.StartAsync();
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await SceneContext.UntilAsync(() => !main.Search.IsLoading, "商品を読み終える");

        foreach (var expanded in new[] { true, false, true, false })
        {
            SectionFolds.DescriptionExpanded = expanded;
            var laps = new List<double>();
            for (var round = 0; round < ItemOpenRounds; round++)
            {
                main.ShowItem(shortItem);
                root.UpdateLayout();
                await context.SettleAsync();

                var lap = System.Diagnostics.Stopwatch.StartNew();
                main.ShowItem(longItem);
                root.UpdateLayout();
                laps.Add(lap.Elapsed.TotalMilliseconds);
                await context.SettleAsync();
            }

            // 1回目は温まっていないので外す
            var kept = laps.Skip(1).Order().ToList();
            Console.WriteLine(
                $"PERF description_expanded={expanded} median_ms={kept[kept.Count / 2]:F1} min_ms={kept[0]:F1} max_ms={kept[^1]:F1} rounds={kept.Count}");
        }

        SectionFolds.DescriptionExpanded = true;
        return new Shot(root);
    }
}
