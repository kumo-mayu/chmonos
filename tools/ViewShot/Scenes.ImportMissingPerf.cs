using System.Diagnostics;
using System.Windows.Controls;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Services;

namespace ViewShot;

/// <summary>
/// 「見つからないファイルを探す」の結果が数千件のときの、出るまでの時間・開く時間・流したときの止まりを書き出す（担当M74・メモ74）。
/// 結果の行を監視対象の欄の中に入れ子で並べていた頃（仮想化なし）と、独立した欄の仮想化した一覧とを、同じ場面で比べるために残す。
/// 版によって畳む印の持ち方が違うので、開くのは ViewModel の「Is…Expanded」をまとめて真にする。
/// 流すのは、画面の中の流せる ScrollViewer を内側から順に、1歩 120px（ホイールの約2.5刻み）で同じ距離だけ。
/// 1歩の並べ直しが 33ms（2コマ）を超えた回を「止まったコマ」と数える（perf-measure の決まり）。描く手間は台では測れない
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> ImportMissingPerfScenes =>
    [
        new Scene("perf-import-missing-3000", "計測：見つからないファイルを探した結果が3000件ずつ（紐付け直した・見つからなかった）", context => ImportMissingPerfAsync(context, 3000)),
        new Scene("perf-import-missing-30", "計測：見つからないファイルを探した結果が30件ずつ", context => ImportMissingPerfAsync(context, 30)),
    ];

    private static async Task<Shot> ImportMissingPerfAsync(SceneContext context, int count)
    {
        var main = await context.StartAsync();
        main.ShowImportCommand.Execute(null);
        var root = context.MainWindow();
        await context.PresentAsync(root);
        var import = context.Screen<ImportViewModel>();
        var view = Look.View<ImportView>(root) ?? throw new InvalidOperationException("取り込み画面が見つかりません。");

        MissingFileOutcome Outcome(int i, bool relinked) => new()
        {
            ItemId = $"99{i:00000}",
            ItemName = $"作り物の衣装{i:00000}",
            OldPaths = [$@"D:\Booth\downloads\costume_{i:00000}.zip"],
            NewPath = relinked ? $@"E:\保管\衣装\costume_{i:00000}.zip" : null,
        };

        var result = new MissingFileSearchResult
        {
            MissingBefore = count * 2,
            Relinked = count,
            Hashed = count * 2,
            RelinkedFiles = [.. Enumerable.Range(0, count).Select(i => Outcome(i, relinked: true))],
            NotFoundFiles = [.. Enumerable.Range(count, count).Select(i => Outcome(i, relinked: false))],
        };

        await context.SettleAsync();
        var before = PrivateMb();

        var clock = Stopwatch.StartNew();
        Backdoor.ShowMissingSearchText(import, ImportViewModel.MissingSearchSummary(result));
        import.ShowMissingFiles(result);
        view.UpdateLayout();
        var shown = clock.ElapsedMilliseconds;
        await Stage.IdleAsync();
        var shownIdle = clock.ElapsedMilliseconds;
        Console.WriteLine($"PERF show count={count} layout_ms={shown} idle_ms={shownIdle} rows={RowCount(view)}");

        clock.Restart();
        foreach (var property in typeof(ImportViewModel).GetProperties()
                     .Where(property => property.Name.EndsWith("Expanded", StringComparison.Ordinal) && property.PropertyType == typeof(bool) && property.CanWrite))
        {
            property.SetValue(import, true);
        }

        view.UpdateLayout();
        var opened = clock.ElapsedMilliseconds;
        await Stage.IdleAsync();
        Console.WriteLine($"PERF open layout_ms={opened} idle_ms={clock.ElapsedMilliseconds} rows={RowCount(view)} mem_mb={PrivateMb()} before_mb={before}");

        // 内側の一覧から先に流す（外の全体の ScrollViewer は最後）
        var scrollers = Look.All<ScrollViewer>(view).Where(scroll => scroll.ScrollableHeight > 0).Reverse().ToList();
        foreach (var scroll in scrollers)
        {
            var steps = new List<double>();
            var step = Stopwatch.StartNew();
            while (steps.Count < 300 && scroll.VerticalOffset < scroll.ScrollableHeight - 0.5)
            {
                step.Restart();
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 120);
                view.UpdateLayout();
                steps.Add(step.Elapsed.TotalMilliseconds);
            }

            Console.WriteLine(FormattableString.Invariant(
                $"PERF scroll name={scroll.Name} extent={scroll.ExtentHeight:F0} steps={steps.Count} total_ms={steps.Sum():F0} max_ms={(steps.Count == 0 ? 0 : steps.Max()):F1} over33={steps.Count(ms => ms > 33)} rows={RowCount(view)}"));
        }

        Console.WriteLine($"PERF mem end_mb={PrivateMb()}");
        return new Shot(root);
    }

    // 作られている結果の行（商品名のボタン）。仮想化していれば見えている辺りの数になる
    private static int RowCount(ImportView view)
        => Look.All<Button>(view).Count(button => System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "ImportMissingResultItem");
}
