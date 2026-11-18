using System.Windows;
using System.Windows.Media;

namespace Chmonos.App.Controls;

/// <summary>
/// 木の行の字下げの縦線の形（1行ぶん）。平らに並べた一覧の並びから計算する（<see cref="IndentGuideShape.Compute"/>）。
/// </summary>
/// <param name="Depth">行の深さ。0 は線を引かない。</param>
/// <param name="Continues">段ごと（0〜Depth-1）に、線がこの行の下まで続くか。続かない段は、その段の最後の子を過ぎている。</param>
/// <param name="OpensBelow">次の行がこの行の子か（開いたフォルダ）。三角の下から次の行へ線をつなぐ。</param>
public sealed record IndentGuideShape(int Depth, IReadOnlyList<bool> Continues, bool OpensBelow)
{
    public static readonly IndentGuideShape None = new(0, [], false);

    /// <summary>
    /// 平らに並べた行の深さから、行ごとの線の形を出す。段 k の線は、その下に深さ k+1 の行が、深さ k 以下の行より前に
    /// まだ出てくる間だけ続ける（エクスプローラの木の線と同じく、最後の子で止まる）。
    /// **木の形ではなく、今見えている並びから出す**——絞り込み・畳みで出ていない行があっても、見えている線と食い違わない
    /// </summary>
    public static IReadOnlyList<IndentGuideShape> Compute(IReadOnlyList<int> depths)
    {
        var shapes = new IndentGuideShape[depths.Count];
        var deepest = depths.Count == 0 ? 0 : depths.Max();

        // 後ろから見て、深さ m の行がまだ後に出てくるか（深さ m より浅い行で区切られるまで）
        var later = new bool[deepest + 2];
        for (var i = depths.Count - 1; i >= 0; i--)
        {
            var depth = depths[i];
            var continues = new bool[depth];
            for (var level = 0; level < depth; level++)
            {
                continues[level] = later[level + 1];
            }

            var opensBelow = i + 1 < depths.Count && depths[i + 1] == depth + 1;
            shapes[i] = new IndentGuideShape(depth, continues, opensBelow);

            for (var m = depth + 1; m < later.Length; m++)
            {
                later[m] = false;
            }

            later[depth] = true;
        }

        return shapes;
    }
}

/// <summary>
/// 木の行の左に、深さの数だけ細い縦線を描く（open.md 99 行：中身が多い時に階層が分かりにくい）。
/// 一覧は見える行だけ作るので、親の行から下へ伸ばす形は描けない。行ごとに自分の段の分を描く。
/// 部品を並べずに1つの描画で済ませる（行は流すたびに作り直されるので、線の数だけ部品を作ると重くなる）
/// </summary>
public sealed class IndentGuides : FrameworkElement
{
    public static readonly DependencyProperty ShapeProperty = DependencyProperty.Register(
        nameof(Shape), typeof(IndentGuideShape), typeof(IndentGuides),
        new FrameworkPropertyMetadata(IndentGuideShape.None, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>1段の字下げの幅。行の字下げ（<c>FolderViewRow.Indent</c> の 16）と同じにする。</summary>
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(IndentGuides),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>段 0 の線の横の位置。開閉の三角（幅 18）の真ん中に合わせる。</summary>
    public static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(
        nameof(Offset), typeof(double), typeof(IndentGuides),
        new FrameworkPropertyMetadata(9.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>三角の下の端の、行の真ん中からの距離（開いたフォルダの線を三角の下から始める）。三角の高さ 9 の半分と隙間。</summary>
    public static readonly DependencyProperty GlyphHalfProperty = DependencyProperty.Register(
        nameof(GlyphHalf), typeof(double), typeof(IndentGuides),
        new FrameworkPropertyMetadata(7.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>線を最後の子で止めるか。切ると、深さの数だけ上から下まで引く。</summary>
    public static readonly DependencyProperty StopsAtLastChildProperty = DependencyProperty.Register(
        nameof(StopsAtLastChild), typeof(bool), typeof(IndentGuides),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(IndentGuides),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public IndentGuideShape Shape
    {
        get => (IndentGuideShape)GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public double Offset
    {
        get => (double)GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    public double GlyphHalf
    {
        get => (double)GetValue(GlyphHalfProperty);
        set => SetValue(GlyphHalfProperty, value);
    }

    public bool StopsAtLastChild
    {
        get => (bool)GetValue(StopsAtLastChildProperty);
        set => SetValue(StopsAtLastChildProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IndentGuides()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    protected override Size MeasureOverride(Size availableSize) => default;

    protected override void OnRender(DrawingContext drawingContext)
    {
        var shape = Shape;
        var height = ActualHeight;
        if (height <= 0)
        {
            return;
        }

        // 1画素の線を画素の真ん中に置く（端に掛かると2画素の薄い線にぼやける）
        var dpi = VisualTreeHelper.GetDpi(this);
        var thickness = 1 / dpi.DpiScaleX;
        var pen = new Pen(Stroke, thickness);
        pen.Freeze();
        var middle = height / 2;

        for (var level = 0; level < shape.Depth; level++)
        {
            var x = Snap(Offset + (level * Step), dpi.DpiScaleX) + (thickness / 2);
            var isParent = level == shape.Depth - 1;
            var continues = !StopsAtLastChild || (level < shape.Continues.Count && shape.Continues[level]);
            if (continues)
            {
                drawingContext.DrawLine(pen, new Point(x, 0), new Point(x, height));
            }
            else if (isParent)
            {
                // 最後の子：親からの線をこの行の真ん中で止める
                drawingContext.DrawLine(pen, new Point(x, 0), new Point(x, middle));
            }
        }

        if (shape.OpensBelow)
        {
            var x = Snap(Offset + (shape.Depth * Step), dpi.DpiScaleX) + (thickness / 2);
            drawingContext.DrawLine(pen, new Point(x, middle + GlyphHalf), new Point(x, height));
        }
    }

    private static double Snap(double value, double scale) => Math.Round(value * scale) / scale;
}
