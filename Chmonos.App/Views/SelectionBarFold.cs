using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Chmonos.App.Views;

/// <summary>
/// 選択の帯が1行に入り切らないとき、右寄りのボタンから「その他 ▾」の中へ畳む（ユーザ判断 2026-11-20：
/// 幅 1280 で入り切らなければ、右から「その他」へ送る形を作る）。
/// </summary>
/// <remarks>
/// 畳むのは「全てUnityへ送る…」→「改変に追加…」の順の2つだけ。「お気に入り ▾」「タグ・属性 ▾」はそれ自体がメニューで、
/// 「その他」の中で入れ子にすると3段になる。「まとめて編集する」は帯の主の操作なので畳まない。
/// 2つ畳んでも入らない幅（窓の最小 900 で絞り込みの欄を開いたとき）は、今までどおり折り返す。
/// 幅は描いた後の大きさで決める（文字の大きさ・表示の倍率で変わるので、決まった数にしない）。
/// </remarks>
internal static class SelectionBarFold
{
    /// <summary>畳む順（右から）。帯のボタンと「その他」の項目の AutomationId の末尾が同じ。</summary>
    public static readonly string[] Order = ["SendToUnity", "AddToModification"];

    /// <summary>
    /// 何個畳むか。<paramref name="fixedWidth"/> は畳まないボタンの幅の合計、<paramref name="foldWidths"/> は畳める物の幅（畳む順）。
    /// 全部並べて入るなら 0、畳んでも入らないなら全部
    /// </summary>
    public static int HowMany(double available, double fixedWidth, IReadOnlyList<double> foldWidths)
    {
        var total = fixedWidth + foldWidths.Sum();
        var count = 0;
        while (count < foldWidths.Count && total > available)
        {
            total -= foldWidths[count];
            count++;
        }

        return count;
    }

    /// <summary>帯の大きさが変わった（または左の文が変わった）。並べ直す。</summary>
    public static void Apply(Border bar)
    {
        if (bar.Child is not Panel { Children: [FrameworkElement left, WrapPanel buttons] } row || buttons.Visibility != Visibility.Visible)
        {
            return;
        }

        var inner = bar.ActualWidth - bar.Padding.Left - bar.Padding.Right;
        var gap = row is Controls.SplitRowPanel split ? split.Gap : 0;
        var minLeft = row is Controls.SplitRowPanel splitLeft ? splitLeft.MinLeftWidth : 0;
        left.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var available = inner - Math.Min(minLeft, left.DesiredSize.Width) - gap;

        var fixedWidth = 0.0;
        var foldWidths = new double[Order.Length];
        foreach (var button in buttons.Children.OfType<FrameworkElement>())
        {
            var index = Array.IndexOf(Order, FoldName(button));
            var width = WidthOf(button);
            if (index >= 0)
            {
                foldWidths[index] = width;
            }
            else if (button.Visibility != Visibility.Collapsed)
            {
                fixedWidth += width;
            }
        }

        var fold = HowMany(available, fixedWidth, foldWidths);
        foreach (var button in buttons.Children.OfType<FrameworkElement>())
        {
            var index = Array.IndexOf(Order, FoldName(button));
            if (index >= 0)
            {
                button.Visibility = index < fold ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }

    /// <summary>「その他 ▾」を開いた。帯から畳んだボタンの分だけ、メニューの上に出す。</summary>
    public static void ShowFolded(ContextMenu menu)
    {
        if (menu.PlacementTarget is not FrameworkElement { Parent: Panel buttons })
        {
            return;
        }

        var folded = buttons.Children.OfType<FrameworkElement>()
            .Where(button => FoldName(button) is not null && button.Visibility == Visibility.Collapsed)
            .Select(FoldName)
            .ToHashSet();
        var any = false;
        foreach (var item in menu.Items.OfType<FrameworkElement>())
        {
            var name = AutomationProperties.GetAutomationId(item);
            if (name.StartsWith("SelectionBar.More.Folded.", StringComparison.Ordinal))
            {
                var shown = folded.Contains(name["SelectionBar.More.Folded.".Length..]);
                item.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
                any |= shown;
            }
            else if (name == "SelectionBar.More.FoldedSeparator")
            {
                item.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private static string? FoldName(FrameworkElement element)
    {
        var name = AutomationProperties.GetAutomationId(element);
        return name.StartsWith("SelectionBar.", StringComparison.Ordinal) && Order.Contains(name["SelectionBar.".Length..])
            ? name["SelectionBar.".Length..]
            : null;
    }

    /// <summary>
    /// ボタンの幅（右の余白を含む）。畳んでいる間は測れないので、出していたときの幅を覚えておく（Tag。帯のボタンは Tag を使わない）
    /// </summary>
    private static double WidthOf(FrameworkElement button)
    {
        if (button.Visibility == Visibility.Collapsed)
        {
            return button.Tag is double remembered ? remembered : 0;
        }

        button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = button.DesiredSize.Width;
        if (FoldName(button) is not null)
        {
            button.Tag = width;
        }

        return width;
    }
}
