using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// リストの横にはみ出して見えていない列は、セルの中身を作らない（ユーザ判断 2026-11-27「B」：全部の列を出すと流す時に重い）。
/// 横に流して見える範囲に入ったら作り、出たら空に戻す。列そのもの（幅・並び・見出し）は残す。
/// </summary>
public class ItemListColumnRealizeTests
{
    [Fact]
    public void 見える範囲に少しでも掛かる列だけを作る()
    {
        double[] widths = [100, 100, 100, 100, 100];

        // 0〜250 が見える：3本目（200〜300）は一部だけ掛かるので作る
        Assert.Equal([true, true, true, false, false], ItemListColumnsView.VisibleColumns(widths, 0, 250, 0));

        // 右へ 220 流した（220〜470）：1本目（0〜100）と2本目（100〜200）は外れる
        Assert.Equal([false, false, true, true, true], ItemListColumnsView.VisibleColumns(widths, 220, 250, 0));

        // 余白の分だけ外の列も作る（境目で少し流すたびに作り直さない）
        Assert.Equal([false, true, true, true, true], ItemListColumnsView.VisibleColumns(widths, 220, 250, 30));

        // ちょうど境目で接するだけの列は作らない
        Assert.Equal([false, false, true, false, false], ItemListColumnsView.VisibleColumns(widths, 200, 100, 0));

        // 幅0の列は場所を取らないが、範囲の中にあれば作る（幅が戻ったときに空で残らない）
        Assert.Equal([true, true, true], ItemListColumnsView.VisibleColumns([100, 0, 100], 0, 150, 0));
    }

    [Fact]
    public Task 全部の列を出すと_横にはみ出した列は空で_横に流すと作られ_戻すと空に戻る() => TestApp.Run(async app =>
    {
        var columns = (await app.StartAsync()).Search.ListColumns;
        foreach (var definition in ItemListColumnCatalog.All.Where(column => column.Key != ItemListColumnCatalog.Select))
        {
            columns.SetShown(definition.Key, true);
        }

        var list = new ListView { ItemsSource = new[] { new object(), new object(), new object() } };
        list.Resources.MergedDictionaries.Add(new Views.ItemCardResources());
        ItemListColumnsView.SetColumns(list, columns);
        var host = new Grid { Width = 800, Height = 300 };
        host.Children.Add(list);
        Layout(host);

        var view = (GridView)list.View;
        var real = view.Columns.Select(column => column.CellTemplate).ToList();
        var scroll = Descendants<ScrollViewer>(list).First(viewer => ReferenceEquals(viewer.TemplatedParent, list));
        Assert.True(scroll.ExtentWidth > scroll.ViewportWidth * 2, "全部の列は一覧の幅を大きく超える");

        // 左端（0〜800）：右の列は作らず空。左の列と選ぶ列は作る
        var shown = Expected(view, 0, scroll.ViewportWidth);
        Assert.Contains(false, shown);
        AssertCells(list, view, shown);
        var lastKey = columns.Order[^1];
        Assert.False(shown[^1], $"右端の列（{lastKey}）は見えない");

        // 右端まで流す：右の列が作られ、左の列（選ぶ列のほか）は空に戻る
        scroll.ScrollToHorizontalOffset(scroll.ExtentWidth);
        Layout(host);
        shown = Expected(view, scroll.HorizontalOffset, scroll.ViewportWidth);
        Assert.True(shown[^1]);
        Assert.False(shown[1], "左の列（絵）は見えない");
        AssertCells(list, view, shown);

        // 左端へ戻すと、元のとおり
        scroll.ScrollToHorizontalOffset(0);
        Layout(host);
        AssertCells(list, view, Expected(view, 0, scroll.ViewportWidth));

        // 一覧を広げると、見える範囲に入った列が作られる
        host.Width = 4000;
        Layout(host);
        AssertCells(list, view, Expected(view, scroll.HorizontalOffset, scroll.ViewportWidth));
        Assert.True(Expected(view, scroll.HorizontalOffset, scroll.ViewportWidth).Count(value => value) > shown.Count(value => value));

        // 全部が見える幅なら、全部の列が元の型（最初に作った列の型はそのまま）
        scroll.ScrollToHorizontalOffset(0);
        host.Width = 100000;
        Layout(host);
        var empty = real[^1];
        Assert.DoesNotContain(view.Columns, column => ReferenceEquals(column.CellTemplate, empty));
        Assert.Equal(real.Take(2), view.Columns.Take(2).Select(column => column.CellTemplate));
        AssertCells(list, view, [.. view.Columns.Select(_ => true)]);
    });

    [Fact]
    public Task 並べ替えると_見える位置に来た列が作られる() => TestApp.Run(async app =>
    {
        var columns = (await app.StartAsync()).Search.ListColumns;
        foreach (var definition in ItemListColumnCatalog.All.Where(column => column.Key != ItemListColumnCatalog.Select))
        {
            columns.SetShown(definition.Key, true);
        }

        var list = new ListView { ItemsSource = new[] { new object(), new object() } };
        list.Resources.MergedDictionaries.Add(new Views.ItemCardResources());
        ItemListColumnsView.SetColumns(list, columns);
        var host = new Grid { Width = 800, Height = 300 };
        host.Children.Add(list);
        Layout(host);

        var view = (GridView)list.View;
        var scroll = Descendants<ScrollViewer>(list).First(viewer => ReferenceEquals(viewer.TemplatedParent, list));
        var last = view.Columns[^1];
        Assert.False(Expected(view, 0, scroll.ViewportWidth)[^1]);
        var empty = last.CellTemplate;

        // 見出しのドラッグと同じ（列の並びの Move）。右端の列を左から2番目へ
        view.Columns.Move(view.Columns.Count - 1, 1);
        await UiThread.Settle();
        Layout(host);
        Assert.NotSame(empty, last.CellTemplate);
        AssertCells(list, view, Expected(view, 0, scroll.ViewportWidth));
    });

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(element.Width, element.Height));
        element.Arrange(new Rect(0, 0, element.Width, element.Height));
        element.UpdateLayout();
    }

    /// <summary>今の幅と横の位置から、作るはずの列（選ぶ列はいつも作る）。</summary>
    private static bool[] Expected(GridView view, double offset, double viewport)
    {
        var shown = ItemListColumnsView.VisibleColumns(view.Columns.Select(column => column.ActualWidth).ToList(), offset, viewport, ItemListColumnsView.RealizeMargin);
        shown[0] = true;
        return shown;
    }

    /// <summary>どの行のセルも、作る列は中身があり、作らない列は中身の無い枠だけ。</summary>
    private static void AssertCells(ListView list, GridView view, bool[] shown)
    {
        // 作らない列は、どれも同じ空の型を指す
        var empties = view.Columns.Where((_, i) => !shown[i]).Select(column => column.CellTemplate).Distinct().ToList();
        Assert.True(empties.Count <= 1, "作らない列の型は1つ");
        var empty = empties.SingleOrDefault();
        for (var i = 0; i < view.Columns.Count; i++)
        {
            Assert.True(ReferenceEquals(view.Columns[i].CellTemplate, empty) != shown[i], $"{i}番目の列（{view.Columns[i].Header}）は {(shown[i] ? "作る" : "作らない")}");
        }

        var rows = Descendants<GridViewRowPresenter>(list).ToList();
        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            // セルの並び（子の順）は見出しをドラッグした後は列の順と一致しないので、型で列と照らす
            var cells = Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(row)).Select(i => (ContentPresenter)VisualTreeHelper.GetChild(row, i)).ToList();
            Assert.Equal(view.Columns.Count, cells.Count);
            Assert.Equal(
                view.Columns.Select(column => column.CellTemplate).OrderBy(RuntimeHelpers.GetHashCode),
                cells.Select(cell => cell.ContentTemplate).OrderBy(RuntimeHelpers.GetHashCode));
            foreach (var cell in cells)
            {
                var hasContent = Descendants<FrameworkElement>(cell).Skip(1).Any();
                Assert.True(hasContent != ReferenceEquals(cell.ContentTemplate, empty), "作る列のセルだけに中身がある");
            }
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var deeper in Descendants<T>(child))
            {
                yield return deeper;
            }
        }
    }
}
