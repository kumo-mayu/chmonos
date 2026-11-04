using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chmonos.App.Controls;

/// <summary>
/// 横に並べて折り返すパネル（<see cref="WrapPanel"/> と同じ並べ方）。ただし <see cref="KeepWithNextProperty"/> を付けた子は、
/// 次の子と同じ行に置けないとき、自分も次の行へ送る。
///
/// 商品ページの対応アバターの素体の見出し（ユーザ指示 2026-11-04：段は分けずに札の並びへ見出しを差し込む）。
/// ふつうの折り返しだと、見出しだけが行の末尾に残り、そのまとまりの札が次の行から始まって、どの素体の札か読み取りにくかった。
/// 見出しと次の札を合わせても1行に入らないほど狭いときは、ふつうに折り返す（見出しだけで1行を取らない）
/// </summary>
public sealed class KeepWithNextWrapPanel : Panel
{
    /// <summary>
    /// 次の子と同じ行に置く印。パネルの子（一覧の入れ物）か、その中の最初の部品（テンプレートの根）に付ける。
    /// </summary>
    public static readonly DependencyProperty KeepWithNextProperty =
        DependencyProperty.RegisterAttached("KeepWithNext", typeof(bool), typeof(KeepWithNextWrapPanel),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static bool GetKeepWithNext(DependencyObject element) => (bool)element.GetValue(KeepWithNextProperty);

    public static void SetKeepWithNext(DependencyObject element, bool value) => element.SetValue(KeepWithNextProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var child = new Size(availableSize.Width, double.PositiveInfinity);
        foreach (UIElement element in InternalChildren)
        {
            element.Measure(child);
        }

        return Layout(availableSize.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize.Width, arrange: true);
        return finalSize;
    }

    /// <summary>行を決める。測るときと置くときで同じ決め方にする（食い違うと、測った高さと置いた位置がずれる）。</summary>
    private Size Layout(double width, bool arrange)
    {
        var children = InternalChildren;
        var lines = new List<(int From, int To, double Height)>();
        var x = 0.0;
        var lineHeight = 0.0;
        var lineStart = 0;
        var widest = 0.0;
        for (var i = 0; i < children.Count; i++)
        {
            var size = children[i].DesiredSize;
            var need = size.Width;
            if (size.Width > 0 && KeepsWithNext(children[i]) && NextShown(i) is { } next)
            {
                var together = size.Width + children[next].DesiredSize.Width;
                if (together <= width)
                {
                    need = together;
                }
            }

            if (x > 0 && x + need > width)
            {
                lines.Add((lineStart, i, lineHeight));
                widest = Math.Max(widest, x);
                lineStart = i;
                x = 0;
                lineHeight = 0;
            }

            x += size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        lines.Add((lineStart, children.Count, lineHeight));
        widest = Math.Max(widest, x);

        var y = 0.0;
        foreach (var (from, to, height) in lines)
        {
            if (arrange)
            {
                var left = 0.0;
                for (var i = from; i < to; i++)
                {
                    var size = children[i].DesiredSize;
                    children[i].Arrange(new Rect(left, y, size.Width, height));
                    left += size.Width;
                }
            }

            y += height;
        }

        return new Size(double.IsInfinity(width) ? widest : Math.Min(widest, width), y);
    }

    /// <summary>次に幅のある子（隠した子は幅0なので飛ばす）。</summary>
    private int? NextShown(int index)
    {
        for (var i = index + 1; i < InternalChildren.Count; i++)
        {
            if (InternalChildren[i].DesiredSize.Width > 0)
            {
                return i;
            }
        }

        return null;
    }

    private static bool KeepsWithNext(UIElement element)
        => GetKeepWithNext(element)
            || (VisualTreeHelper.GetChildrenCount(element) > 0 && VisualTreeHelper.GetChild(element, 0) is DependencyObject root && GetKeepWithNext(root));
}
