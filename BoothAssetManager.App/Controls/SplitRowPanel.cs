using System.Windows;
using System.Windows.Controls;

namespace BoothAssetManager.App.Controls;

/// <summary>
/// 左と右の2つの塊を1行に並べ、入り切らなければ右の塊を次の行へ送る帯。
///
/// 検索の結果の上の帯は、件数（左）と表示順・カード／リスト（右）を右寄せで重ねて置いていたため、
/// 幅 900px の窓で右の塊が件数の文字の上に乗って読めなくなった（点検 2026-09-23）。
/// <see cref="WrapPanel"/> は右寄せを持たないので、送った後も1行のときも右の塊を右端に揃えるにはこの形が要る。
///
/// 左の塊は1行のときは残りの幅で測り直す——中の「何で絞っているか」の文は長いと省略で縮むので、
/// その文の長さだけで右を送ってしまわないよう、左が要る幅は <see cref="MinLeftWidth"/> までと見なす。
/// </summary>
public sealed class SplitRowPanel : Panel
{
    public static readonly DependencyProperty MinLeftWidthProperty = DependencyProperty.Register(
        nameof(MinLeftWidth), typeof(double), typeof(SplitRowPanel),
        new FrameworkPropertyMetadata(160.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(SplitRowPanel),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>1行に並べるとき、左に最低これだけ残す（左が本当に要る幅がこれより小さければその幅）。</summary>
    public double MinLeftWidth
    {
        get => (double)GetValue(MinLeftWidthProperty);
        set => SetValue(MinLeftWidthProperty, value);
    }

    /// <summary>1行のときの左右の間と、2行のときの上下の間。</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    private bool _stacked;

    private UIElement? Left => InternalChildren.Count > 0 ? InternalChildren[0] : null;

    private UIElement? Right => InternalChildren.Count > 1 ? InternalChildren[1] : null;

    protected override Size MeasureOverride(Size availableSize)
    {
        var left = Left;
        var right = Right;
        if (left is null)
        {
            return new Size();
        }

        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
        if (right is null || right.Visibility == Visibility.Collapsed)
        {
            left.Measure(availableSize);
            right?.Measure(infinite);
            _stacked = false;
            return left.DesiredSize;
        }

        right.Measure(infinite);
        left.Measure(infinite);
        var rightWidth = right.DesiredSize.Width;
        var leftNeeds = Math.Min(left.DesiredSize.Width, MinLeftWidth);
        var width = double.IsInfinity(availableSize.Width)
            ? left.DesiredSize.Width + Gap + rightWidth
            : availableSize.Width;

        _stacked = leftNeeds + Gap + rightWidth > width;
        if (_stacked)
        {
            left.Measure(new Size(width, double.PositiveInfinity));
            right.Measure(new Size(width, double.PositiveInfinity));
            return new Size(
                Math.Max(left.DesiredSize.Width, right.DesiredSize.Width),
                left.DesiredSize.Height + Gap / 2 + right.DesiredSize.Height);
        }

        left.Measure(new Size(Math.Max(0, width - Gap - rightWidth), double.PositiveInfinity));
        return new Size(
            Math.Min(width, left.DesiredSize.Width + Gap + rightWidth),
            Math.Max(left.DesiredSize.Height, right.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var left = Left;
        var right = Right;
        if (left is null)
        {
            return finalSize;
        }

        if (right is null || right.Visibility == Visibility.Collapsed)
        {
            left.Arrange(new Rect(0, (finalSize.Height - left.DesiredSize.Height) / 2, finalSize.Width, left.DesiredSize.Height));
            return finalSize;
        }

        var rightSize = right.DesiredSize;
        if (_stacked)
        {
            left.Arrange(new Rect(0, 0, finalSize.Width, left.DesiredSize.Height));
            // 送った後も右端に揃える。左端に落とすと、1行のときと表示順の位置が変わり、目で探し直すことになる
            var top = left.DesiredSize.Height + Gap / 2;
            right.Arrange(new Rect(Math.Max(0, finalSize.Width - rightSize.Width), top, Math.Min(rightSize.Width, finalSize.Width), rightSize.Height));
            return finalSize;
        }

        var leftWidth = Math.Max(0, finalSize.Width - Gap - rightSize.Width);
        left.Arrange(new Rect(0, (finalSize.Height - left.DesiredSize.Height) / 2, leftWidth, left.DesiredSize.Height));
        right.Arrange(new Rect(finalSize.Width - rightSize.Width, (finalSize.Height - rightSize.Height) / 2, rightSize.Width, rightSize.Height));
        return finalSize;
    }
}
