using System.Windows;
using System.Windows.Controls;

namespace Chmonos.App.Controls;

/// <summary>
/// 同じ大きさの項目を格子に並べ、読む向きを横（左から右、次の段へ）と縦（上から下、次の列へ）で切り替える
/// （ユーザ指示 2026-09-19：値の高い順・低い順の並びが横固定なのは、縦に読む人には違和感がある）。
///
/// 縦に流すのに <see cref="WrapPanel"/> の縦向きを使わないのは、縦の WrapPanel は高さの上限が無いと
/// 折り返さず1列になるため。ここでは先に列の数を幅から決め、段の数を「件数 ÷ 列」で出してから並べる。
/// 項目の大きさは全部同じ（いちばん大きい物）に揃える——商品の行は幅を決め打ちにしてあるので、揃えても崩れない。
/// </summary>
public sealed class FlowGridPanel : Panel
{
    public static readonly DependencyProperty FlowsVerticallyProperty = DependencyProperty.Register(
        nameof(FlowsVertically), typeof(bool), typeof(FlowGridPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>true なら上から下へ流して次の列へ、false なら左から右へ流して次の段へ。</summary>
    public bool FlowsVertically
    {
        get => (bool)GetValue(FlowsVerticallyProperty);
        set => SetValue(FlowsVerticallyProperty, value);
    }

    private Size _cell;
    private int _columns = 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0.0;
        var height = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            width = Math.Max(width, child.DesiredSize.Width);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        _cell = new Size(width, height);
        var count = InternalChildren.Count;
        if (count == 0 || width <= 0)
        {
            return new Size(0, 0);
        }

        _columns = double.IsInfinity(availableSize.Width)
            ? count
            : Math.Clamp((int)(availableSize.Width / width), 1, count);

        var rows = (count + _columns - 1) / _columns;
        return new Size(_columns * width, rows * height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = InternalChildren.Count;
        if (count == 0)
        {
            return finalSize;
        }

        var rows = (count + _columns - 1) / _columns;
        for (var i = 0; i < count; i++)
        {
            var (row, column) = FlowsVertically ? (i % rows, i / rows) : (i / _columns, i % _columns);
            InternalChildren[i].Arrange(new Rect(column * _cell.Width, row * _cell.Height, _cell.Width, _cell.Height));
        }

        return finalSize;
    }
}
