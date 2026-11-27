using System.Windows;
using System.Windows.Controls;

namespace Chmonos.App.Controls;

/// <summary>
/// 札（チップ）を折り返して並べ、全部で <see cref="MaxLines"/> 行を超えるときは、畳んでいる間その行数で切るパネル。
/// 畳んでも札は必ず1つは見せる（先頭の札が最後の行の空きに入らなければ、その幅に縮めて名前を「…」で切る）。
/// 検索の左の条件で積んだ札の並び（ユーザ判断 2026-11-20「チップのままで、折り畳めるようにしよう。一覧できることも大切だ。」）。
///
/// 行の数は実際に並べた幅で決める（札の数では決めない。名前の長さで1行に入る数が変わる）。
/// 切ったときは最後の行の右に <see cref="ReserveWidth"/> を空け、そこに持ち主の型が「ほか n 件」を重ねる。
/// 広げたときも、最後の行に空きが無ければ1行足して「折りたたむ」を置く場所を取る。
/// 空ける幅は決め打ち：隠した数で幅を変えると、空きが変わって隠す数が変わり、配置が回る
/// </summary>
public sealed class ChipWrapPanel : Panel
{
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded), typeof(bool), typeof(ChipWrapPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty HiddenCountProperty = DependencyProperty.Register(
        nameof(HiddenCount), typeof(int), typeof(ChipWrapPanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty OverflowsProperty = DependencyProperty.Register(
        nameof(Overflows), typeof(bool), typeof(ChipWrapPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>畳んでいる間の行の数。並びごとに変える（対応アバターは1行。<see cref="ViewModels.ChipFold.MaxLines"/>）。</summary>
    public static readonly DependencyProperty MaxLinesProperty = DependencyProperty.Register(
        nameof(MaxLines), typeof(int), typeof(ChipWrapPanel),
        new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>
    /// 札が並びに置かれて見えているか（このパネルが子に書き、札の中へ受け継ぐ）。札の頭の絵は見えている札だけが読む：
    /// 隠した札も部品は作られ、結びは見えるかに関わらず働くので、絵を直に結ぶと「ほか n 件」の中の札まで読む。
    /// 既定は false（並べる前に結びが絵を読まないように）
    /// </summary>
    public static readonly DependencyProperty IsPlacedProperty = DependencyProperty.RegisterAttached(
        "IsPlaced", typeof(bool), typeof(ChipWrapPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsPlaced(DependencyObject element) => (bool)element.GetValue(IsPlacedProperty);

    public static void SetIsPlaced(DependencyObject element, bool value) => element.SetValue(IsPlacedProperty, value);

    public static readonly DependencyProperty RevealRequestedProperty = DependencyProperty.Register(
        nameof(RevealRequested), typeof(bool), typeof(ChipWrapPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>広げて全部を並べるか。</summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>畳んで隠した子の数（このパネルが書く）。</summary>
    public int HiddenCount
    {
        get => (int)GetValue(HiddenCountProperty);
        set => SetValue(HiddenCountProperty, value);
    }

    /// <summary>全部を並べると <see cref="MaxLines"/> 行を超えるか（このパネルが書く。広げている間も true）。</summary>
    public bool Overflows
    {
        get => (bool)GetValue(OverflowsProperty);
        set => SetValue(OverflowsProperty, value);
    }

    /// <summary>人が子を足した。並べて隠れる子があれば広げ、false に戻す。</summary>
    public bool RevealRequested
    {
        get => (bool)GetValue(RevealRequestedProperty);
        set => SetValue(RevealRequestedProperty, value);
    }

    /// <summary>畳んでいる間の行の数。既定の2行なら札が数個は一目で見え、枠の高さは札1行ぶんしか増えない。</summary>
    public int MaxLines
    {
        get => (int)GetValue(MaxLinesProperty);
        set => SetValue(MaxLinesProperty, value);
    }

    /// <summary>
    /// 最後の行の右に「ほか n 件」「折りたたむ」のために空ける幅。11.5px の字の「ほか 13 件」が撮った画で約48px（2026-11-20）なので、
    /// 3桁と右の余白2が入る62にする。広く取ると、検索の左の欄（約215px）では2行目に入る札が減る
    /// </summary>
    public double ReserveWidth { get; set; } = 62;

    private Layout _layout = Layout.Empty;

    /// <summary>全部の幅で測った大きさ。縮めて測り直した札も、行の割り振りはこの大きさで決める（縮めた幅で割り振ると、溢れるかが測るたびに変わる）。</summary>
    private Size[] _fullSizes = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        var sizes = new Size[InternalChildren.Count];
        for (var i = 0; i < sizes.Length; i++)
        {
            var child = InternalChildren[i];

            // 幅を限って測る：札は名前の列が縮む作りなので、幅を無限にすると長い名前の札が欄からはみ出す（メモ70）
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            sizes[i] = child.DesiredSize;
        }

        _fullSizes = sizes;
        _layout = Lay(sizes, availableSize.Width, MaxLines, ReserveWidth, IsExpanded);

        // 縮めて置く札は、その幅で測り直す。全部の幅で測ったまま狭く置くと、名前が「…」にならずに右が切れる
        for (var i = 0; i < sizes.Length; i++)
        {
            if (_layout.Places[i] is { } place && place.Width < sizes[i].Width)
            {
                InternalChildren[i].Measure(new Size(place.Width, double.PositiveInfinity));
            }
        }

        return _layout.Size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var sizes = _fullSizes;
        if (sizes.Length != InternalChildren.Count)
        {
            sizes = new Size[InternalChildren.Count];
            for (var i = 0; i < sizes.Length; i++)
            {
                sizes[i] = InternalChildren[i].DesiredSize;
            }
        }

        var layout = Lay(sizes, finalSize.Width, MaxLines, ReserveWidth, IsExpanded);
        _layout = layout;
        var shownChanged = false;
        for (var i = 0; i < sizes.Length; i++)
        {
            var child = InternalChildren[i];
            if (layout.Places[i] is { } place)
            {
                child.Arrange(place);
                shownChanged |= !child.IsEnabled;
                child.SetCurrentValue(IsEnabledProperty, true);
                child.SetCurrentValue(IsPlacedProperty, true);
            }
            else
            {
                // 隠した子は幅0で置き、押せなくして Tab・矢印でも止まらないようにする（OneLinePanel と同じ。見えない物にフォーカスが行くと、どこにいるか分からない）。
                // 中の × ごと止めるので Focusable ではなく IsEnabled
                child.Arrange(new Rect(0, 0, 0, 0));
                shownChanged |= child.IsEnabled;
                child.SetCurrentValue(IsEnabledProperty, false);
                child.SetCurrentValue(IsPlacedProperty, false);
            }
        }

        if (HiddenCount != layout.Hidden)
        {
            SetCurrentValue(HiddenCountProperty, layout.Hidden);
        }

        if (Overflows != layout.Overflows)
        {
            SetCurrentValue(OverflowsProperty, layout.Overflows);
        }

        if (RevealRequested)
        {
            SetCurrentValue(RevealRequestedProperty, false);
            if (layout.Hidden > 0)
            {
                SetCurrentValue(IsExpandedProperty, true);
            }
        }

        // 矢印の並びが覚えた止まり先が隠れた札だと、Tab で並びに入れなくなる。止まり先を見えている札で選び直させる
        if (shownChanged && ItemsControl.GetItemsOwner(this) is { } owner)
        {
            ArrowGroup.Requeue(owner);
        }

        return finalSize;
    }

    internal sealed record Layout(Rect?[] Places, Size Size, int Hidden, bool Overflows)
    {
        public static readonly Layout Empty = new([], default, 0, false);
    }

    /// <summary>並べ方を決める（試験は大きさを渡して確かめる）。置かない子の場所は null。</summary>
    internal static Layout Lay(IReadOnlyList<Size> sizes, double width, int maxLines, double reserve, bool expanded)
    {
        var places = new Rect?[sizes.Count];
        if (sizes.Count == 0)
        {
            return new Layout(places, default, 0, false);
        }

        // 全部を並べたときの行の割り振り（行の頭の番号）
        var starts = new List<int>();
        var x = 0.0;
        for (var i = 0; i < sizes.Count; i++)
        {
            if (i == 0 || (x > 0 && x + sizes[i].Width > width))
            {
                starts.Add(i);
                x = 0;
            }

            x += sizes[i].Width;
        }

        var overflows = starts.Count > maxLines;
        var y = 0.0;
        var usedWidth = 0.0;
        var hidden = 0;
        if (!overflows || expanded)
        {
            var lastRowWidth = 0.0;
            var lastRowHeight = 0.0;
            for (var row = 0; row < starts.Count; row++)
            {
                var end = row + 1 < starts.Count ? starts[row + 1] : sizes.Count;
                (lastRowWidth, lastRowHeight) = PlaceRow(sizes, places, starts[row], end, y);
                usedWidth = Math.Max(usedWidth, lastRowWidth);
                y += lastRowHeight;
            }

            // 広げたときは最後の行の右に「折りたたむ」を置く。入らなければ1行足す
            if (overflows && lastRowWidth + reserve > width)
            {
                y += lastRowHeight;
            }
        }
        else
        {
            // 畳む：前の行は全部の幅で、最後の行だけ右を空けて並べ、入らない子から後は全部隠す（並びの順を崩さない）
            var i = 0;
            for (var row = 0; row < maxLines; row++)
            {
                var limit = row == maxLines - 1 ? width - reserve : width;
                var rowX = 0.0;
                var rowHeight = 0.0;
                while (i < sizes.Count && ((rowX == 0 && row < maxLines - 1) || rowX + sizes[i].Width <= limit))
                {
                    places[i] = new Rect(rowX, y, sizes[i].Width, sizes[i].Height);
                    rowX += sizes[i].Width;
                    rowHeight = Math.Max(rowHeight, sizes[i].Height);
                    i++;
                }

                // 1つも置けないまま最後の行に来た（1行で畳む並びの先頭が長い札）。札が1つも見えないと何を選んだか分からないので、
                // 空きの幅に縮めて先頭の札を置く（ユーザ判断 2026-11-27「一つを残して畳んで良い」）
                if (i == 0 && row == maxLines - 1)
                {
                    var shrunk = Math.Max(0, Math.Min(sizes[0].Width, limit));
                    places[0] = new Rect(0, y, shrunk, sizes[0].Height);
                    rowX = shrunk;
                    rowHeight = sizes[0].Height;
                    i = 1;
                }

                // 最後の行に1つも入らなくても、ボタンを置くので1行ぶんの高さを取る
                if (rowHeight == 0 && i < sizes.Count)
                {
                    rowHeight = sizes[i].Height;
                }

                usedWidth = Math.Max(usedWidth, rowX);
                y += rowHeight;
            }

            hidden = sizes.Count - i;
        }

        // 溢れるときは右端にボタンを重ねるので、幅いっぱいを取る（幅が無限の時は折り返さないので溢れない）
        var desiredWidth = overflows && !double.IsInfinity(width) ? width : usedWidth;
        return new Layout(places, new Size(desiredWidth, y), hidden, overflows);
    }

    private static (double Width, double Height) PlaceRow(IReadOnlyList<Size> sizes, Rect?[] places, int start, int end, double y)
    {
        var x = 0.0;
        var height = 0.0;
        for (var i = start; i < end; i++)
        {
            places[i] = new Rect(x, y, sizes[i].Width, sizes[i].Height);
            x += sizes[i].Width;
            height = Math.Max(height, sizes[i].Height);
        }

        return (x, height);
    }
}
