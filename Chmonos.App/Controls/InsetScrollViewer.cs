using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chmonos.App.Controls;

/// <summary>
/// 上に別の部品を**重ねて**出す ScrollViewer（ショップの中：1行の見出しと、止まった「商品」の行を中身の上に重ねる）。
///
/// 重ねた部品の下に隠れた所まで流しても、WPF の「見える所まで流して」（<see cref="FrameworkElement.BringIntoView()"/>）は
/// 隠れていることを知らず、「見えている」と答える。矢印キーでカードを移ったとき、重ねた帯の下にカードが残る。
/// 帯の高さを <see cref="TopInset"/> に教えると、その下に出る所まで流す。
///
/// 頼みを受けるのは、押された部品の側の受け口（型ごと）。中に ListBox のような ScrollViewer を持つ一覧があると、
/// 頼みはその ScrollViewer が受けて済ませ、外のこの部品まで届かない（内側が流せない一覧でも）。
/// そこで頼みが出た所で、内側に先んじて見る。内側には届かせたまま（<see cref="RoutedEventArgs.Handled"/> にしない）なので、
/// 一覧の中の横送りなど、内側の流しは今までどおり動く。
/// マウスで押したカードを流さない決まり（<see cref="NoScrollOnClick"/>）は、ここでも守る。
/// </summary>
public sealed class InsetScrollViewer : ScrollViewer
{
    /// <summary>上に重ねている帯の高さ（px）。この下に出る所まで流す。重ねていなければ 0。</summary>
    public static readonly DependencyProperty TopInsetProperty = DependencyProperty.Register(
        nameof(TopInset), typeof(double), typeof(InsetScrollViewer), new PropertyMetadata(0.0));

    /// <summary>
    /// 重ねた帯そのものを中身の中に持つ部品（止めた行）に付ける。その中の部品は帯の下に隠れていないので、頼みを見ない。
    /// 見ると、止めた行の中のチェックを押すたびに、行の下まで流してしまう
    /// </summary>
    public static readonly DependencyProperty IsPinnedProperty = DependencyProperty.RegisterAttached(
        "IsPinned", typeof(bool), typeof(InsetScrollViewer), new PropertyMetadata(false));

    static InsetScrollViewer()
    {
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.RequestBringIntoViewEvent,
            new RequestBringIntoViewEventHandler(OnRequestBringIntoView));
    }

    public double TopInset
    {
        get => (double)GetValue(TopInsetProperty);
        set => SetValue(TopInsetProperty, value);
    }

    public static bool GetIsPinned(DependencyObject element) => (bool)element.GetValue(IsPinnedProperty);

    public static void SetIsPinned(DependencyObject element, bool value) => element.SetValue(IsPinnedProperty, value);

    private static void OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        // 道の上の部品ごとに呼ばれる。頼みが出た所で1回だけ見る
        if (!ReferenceEquals(sender, e.TargetObject) || sender is not FrameworkElement target || target.ActualHeight <= 0)
        {
            return;
        }

        // マウスで押したカードは流さない。止める側（NoScrollOnClick）が止めるまで待たず、ここで先に引き下がる
        if (NoScrollOnClick.ShouldSuppress(target))
        {
            return;
        }

        InsetScrollViewer? viewer = null;
        for (DependencyObject? current = target; current is not null; current = ParentOf(current))
        {
            if (GetIsPinned(current))
            {
                return;
            }

            if (current is InsetScrollViewer found)
            {
                viewer = found;
                break;
            }
        }

        if (viewer?.Content is not Visual content || viewer.ViewportHeight <= 0)
        {
            return;
        }

        Rect rect;
        try
        {
            var local = e.TargetRect.IsEmpty ? new Rect(target.RenderSize) : e.TargetRect;
            rect = target.TransformToAncestor(content).TransformBounds(local);
        }
        catch (InvalidOperationException)
        {
            // 窓の外に出た部品（メニュー・候補の窓）は中身の木につながっていない
            return;
        }

        if (OffsetToReveal(viewer.VerticalOffset, viewer.ViewportHeight, viewer.TopInset, rect.Top, rect.Bottom) is { } offset)
        {
            viewer.ScrollToVerticalOffset(offset);
        }
    }

    /// <summary>
    /// 帯の下に出す流れの位置。もう出ていれば null（動かさない）。
    /// 画面（帯の下から下端まで）より高い物は、上端を帯の下に合わせる（全体は見せられないので、頭から見せる）
    /// </summary>
    /// <param name="offset">今の流れの位置。</param>
    /// <param name="viewport">見えている高さ。</param>
    /// <param name="inset">上に重ねた帯の高さ。</param>
    /// <param name="top">見せたい物の上端（中身の上からの位置）。</param>
    /// <param name="bottom">見せたい物の下端。</param>
    internal static double? OffsetToReveal(double offset, double viewport, double inset, double top, double bottom)
    {
        var visibleTop = offset + inset;
        var visibleBottom = offset + viewport;
        if (top >= visibleTop - 0.5 && bottom <= visibleBottom + 0.5)
        {
            return null;
        }

        if (bottom - top >= viewport - inset || top < visibleTop)
        {
            return Math.Max(0, top - inset);
        }

        return bottom - viewport;
    }

    // 文の中の Run のような見た目の木に居ない物は、論理の木で上へ出る
    private static DependencyObject? ParentOf(DependencyObject node)
        => node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);
}
