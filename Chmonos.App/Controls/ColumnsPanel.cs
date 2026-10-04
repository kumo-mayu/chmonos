using System.Windows;
using System.Windows.Controls;

namespace Chmonos.App.Controls;

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

    /// <summary>
    /// 1列に固定する（リストの見方のとき。メモ32-④ 2026-10-04）。並べ替えのドラッグは「この行の前／後ろ」で落とし先を決めるので、
    /// 横に連なっていると、右の行の前が左の行の後ろでもあり、どちらへ落ちるのか見て分からない。
    /// </summary>
    public static readonly DependencyProperty SingleColumnProperty = DependencyProperty.Register(
        nameof(SingleColumn),
        typeof(bool),
        typeof(ColumnsPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public bool SingleColumn
    {
        get => (bool)GetValue(SingleColumnProperty);
        set => SetValue(SingleColumnProperty, value);
    }

    private int ColumnsFor(double width)
        => SingleColumn || width <= 0 || double.IsInfinity(width)
            ? 1
            : Math.Max(1, (int)(width / Math.Max(1, MinColumnWidth)));

    public static readonly DependencyProperty FullWidthProperty = DependencyProperty.RegisterAttached(
        "FullWidth",
        typeof(bool),
        typeof(ColumnsPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    /// <summary>その項目だけ幅いっぱいを使う（畳みを開いた行など）。</summary>
    public static void SetFullWidth(UIElement element, bool value) => element.SetValue(FullWidthProperty, value);

    public static bool GetFullWidth(UIElement element) => (bool)element.GetValue(FullWidthProperty);

    /// <summary>
    /// 左から右へ、はみ出したら次の段へ置く（**読む順を素直にする**。ユーザ指摘 2026-09-18：
    /// いちばん低い列へ積む形だと、開いた行の高さで並びが飛んで順番が読めなかった）。
    /// 段の高さは、その段でいちばん高い項目に合わせる。
    /// </summary>
    private double Layout(double width, bool arrange)
    {
        var columns = ColumnsFor(width);
        var columnWidth = double.IsInfinity(width) ? MinColumnWidth : width / columns;

        var top = 0d;
        var column = 0;
        var rowHeight = 0d;

        foreach (UIElement child in InternalChildren)
        {
            // 幅いっぱいを使う項目は、段の先頭から始めて1つで段を占める
            var full = GetFullWidth(child);
            if (full && column > 0)
            {
                top += rowHeight;
                column = 0;
                rowHeight = 0;
            }

            var childWidth = full ? columnWidth * columns : columnWidth;
            child.Measure(new Size(childWidth, double.PositiveInfinity));

            if (arrange)
            {
                child.Arrange(new Rect(column * columnWidth, top, childWidth, child.DesiredSize.Height));
            }

            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            column += full ? columns : 1;

            if (column >= columns)
            {
                top += rowHeight;
                column = 0;
                rowHeight = 0;
            }
        }

        return top + rowHeight;
    }

    protected override Size MeasureOverride(Size available)
    {
        var height = Layout(available.Width, arrange: false);
        var columns = ColumnsFor(available.Width);

        return new Size(
            double.IsInfinity(available.Width) ? MinColumnWidth * columns : available.Width,
            height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        Layout(final.Width, arrange: true);
        return final;
    }
}
