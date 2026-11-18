using System.Windows;
using System.Windows.Controls;

namespace Chmonos.App.Controls;

/// <summary>
/// 先頭の子（商品名）の右隣に、2つめ以降の子（ショップ名）をすぐ続けて並べる行。
///
/// 流した時の帯（<c>ItemCompactBar</c>）でショップ名を右端に置くと、商品名が短い時に両者が離れて、どの名前のショップか読み取りにくい（ユーザ指摘 2026-11-18）。
/// DockPanel や Grid の Auto 列は、中身の希望の幅で測るので長い名前が「…」で縮まない。
/// そこで、続ける側を先に測って幅を確保し、残りで先頭を測る。
/// 続ける側は広さの半分までに抑える（半分は経験則：商品名が読めなくなるほど、ショップ名で潰さないため）。
/// </summary>
public sealed class TrailingFollowPanel : Panel
{
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(TrailingFollowPanel),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>先頭の子と続く子の間（ショップ名の前の余白）。</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    private double _leadWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width;
        var followWidth = 0.0;
        var followHeight = 0.0;
        var hasFollow = false;
        for (var i = 1; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            hasFollow = true;
            var cap = double.IsInfinity(width) ? double.PositiveInfinity : Math.Max(0, (width - Gap) / 2);
            child.Measure(new Size(cap, availableSize.Height));
            followWidth += child.DesiredSize.Width;
            followHeight = Math.Max(followHeight, child.DesiredSize.Height);
        }

        var gap = hasFollow ? Gap : 0;
        var leadHeight = 0.0;
        _leadWidth = 0;
        if (InternalChildren.Count > 0 && InternalChildren[0].Visibility != Visibility.Collapsed)
        {
            var rest = double.IsInfinity(width) ? double.PositiveInfinity : Math.Max(0, width - followWidth - gap);
            InternalChildren[0].Measure(new Size(rest, availableSize.Height));
            _leadWidth = InternalChildren[0].DesiredSize.Width;
            leadHeight = InternalChildren[0].DesiredSize.Height;
        }

        return new Size(_leadWidth + gap + followWidth, Math.Max(leadHeight, followHeight));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        if (InternalChildren.Count > 0 && InternalChildren[0].Visibility != Visibility.Collapsed)
        {
            var lead = InternalChildren[0];
            lead.Arrange(new Rect(x, 0, lead.DesiredSize.Width, finalSize.Height));
            x += lead.DesiredSize.Width;
        }

        var firstFollow = true;
        for (var i = 1; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            if (firstFollow)
            {
                x += Gap;
                firstFollow = false;
            }

            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
            x += child.DesiredSize.Width;
        }

        return finalSize;
    }
}
