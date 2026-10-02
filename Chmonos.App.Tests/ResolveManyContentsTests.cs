using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 中身の多い zip の行を選んでも、画面が固まらない（手動確認 2026-10-02 の5・大容量の確かめ 2026-09-30）。
///
/// 中身7万件の zip の行を選ぶと、右の「アーカイブの中身」が7万行を作って約49秒止まり、メモリが約1GBまで上がっていた。
/// 見える行だけ作る（仮想化）ように直してあるので、本物の画面（<see cref="ResolveView"/>）に7万件の行を選ばせ、
/// 組み立てて並べたときに作られる行の数と、かかった時間で確かめる。時間は固まる（数十秒）と見分けるための上限で、
/// 速さの比べ合いではない。実際の窓での手触りは人が見る。
/// </summary>
public class ResolveManyContentsTests
{
    private const int ContentCount = 70_000;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    [Fact]
    public Task 中身が7万件のzipの行を選んでも_見える行だけを作り_固まらない() => TestApp.Run(async app =>
    {
        var path = app.NewFile(@"a\huge.zip");
        await app.Store.Unresolved.SaveAsync(
        [
            new UnresolvedFile
            {
                Hash = Make.HashOf(path),
                Paths = [path],
                SizeBytes = 3,
                ModifiedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                FirstSeenAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                Contents = [.. Enumerable.Range(0, ContentCount).Select(i => $"content/part{i:D5}.png")],
            },
        ]);
        var main = await app.StartAsync();
        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();
        var resolve = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);
        var view = new ResolveView { DataContext = resolve };

        // 選ぶ（行を選ぶと、右の中身の一覧が7万件を持つ）
        var stopwatch = Stopwatch.StartNew();
        resolve.Selected = Assert.Single(resolve.Files);
        Assert.Equal(ContentCount, resolve.SelectedContents.Count);
        Assert.Equal($"アーカイブの中身 {ContentCount} 件", resolve.ContentsSummary);

        // 組み立てて並べる（窓の高さは、中身の枠の最大の高さ 150 より十分大きい）
        view.Measure(new Size(1200, 900));
        view.Arrange(new Rect(0, 0, 1200, 900));
        view.UpdateLayout();
        stopwatch.Stop();

        var rows = Descendants(view).OfType<TextBlock>()
            .Count(text => text.Text.StartsWith("content/part", StringComparison.Ordinal));
        Assert.InRange(rows, 1, 300);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"7万件の行を選んで並べるのに {stopwatch.Elapsed.TotalSeconds:F1} 秒かかっています（仮想化が効いていれば1秒かからない）");
    });
}
