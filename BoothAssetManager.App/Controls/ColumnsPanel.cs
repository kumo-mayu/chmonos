using System.Windows;
using System.Windows.Controls;

namespace BoothAssetManager.App.Controls;

/// <summary>
/// 幅に応じて列を増やす並べ方（ユーザ指示 2026-09-18：タグの管理の小分類が縦に全部並び、
/// 数が増えると大分類の情報が画面の外へ出る）。
///
/// <see cref="WrapPanel"/> との違いは**列の幅が揃う**こと。小分類の行は名前もメモ欄も
/// 長さがまちまちなので、WrapPanel だと1行ごとに幅が変わって目が泳ぐ。
/// 列の数は「幅 ÷ <see cref="MinColumnWidth"/>」で決め、余りは列に均等に配る。
///
/// 並べ替えのドラッグは行の当たり判定で見ているので、ここは大きさだけを決める。
/// </summary>
public sealed class ColumnsPanel : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth),
        typeof(double),
        typeof(ColumnsPanel),
        new FrameworkPropertyMetadata(420d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>1列に要る最小の幅。これを下回るときは1列にする。</summary>
    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    private int ColumnsFor(double width)
        => width <= 0 || double.IsInfinity(width)
            ? 1
            : Math.Max(1, (int)(width / Math.Max(1, MinColumnWidth)));

    protected override Size MeasureOverride(Size available)
    {
        var columns = ColumnsFor(available.Width);
        var columnWidth = double.IsInfinity(available.Width) ? MinColumnWidth : available.Width / columns;

        // 列ごとの高さを持ち、いちばん低い列へ順に積む（行の高さがそろっていないため）
        var heights = new double[columns];
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(columnWidth, double.PositiveInfinity));

            var shortest = 0;
            for (var index = 1; index < columns; index++)
            {
                if (heights[index] < heights[shortest])
                {
                    shortest = index;
                }
            }

            heights[shortest] += child.DesiredSize.Height;
        }

        return new Size(
            double.IsInfinity(available.Width) ? columnWidth * columns : available.Width,
            heights.Length == 0 ? 0 : heights.Max());
    }

    protected override Size ArrangeOverride(Size final)
    {
        var columns = ColumnsFor(final.Width);
        var columnWidth = final.Width / columns;
        var heights = new double[columns];

        foreach (UIElement child in InternalChildren)
        {
            var shortest = 0;
            for (var index = 1; index < columns; index++)
            {
                if (heights[index] < heights[shortest])
                {
                    shortest = index;
                }
            }

            child.Arrange(new Rect(
                shortest * columnWidth,
                heights[shortest],
                columnWidth,
                child.DesiredSize.Height));

            heights[shortest] += child.DesiredSize.Height;
        }

        return final;
    }
}
