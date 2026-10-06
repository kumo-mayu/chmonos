using System.Windows;
using System.Windows.Controls;

namespace Chmonos.App.Controls;

/// <summary>
/// カードの下の段の札を、右寄せで横1行に並べるパネル。入り切らないときは、後ろの札から順に文字の無い色の丸（<see cref="IsDotProperty"/>）にして幅に収める
/// （ユーザ判断 2026-10-06「+3 などにたたむのではなく文字を持たないただの丸にして横幅に収めましょう」。前は入らない分を「+n」にまとめていた・メモ52）。
///
/// 札は右から左へ伸びる並びで、札が多いと左のカードの端を越えたり、左に置いた容量の文字に重なっていた。
/// 容量の文字は別の欄が先に取るので、このパネルが受ける幅は容量の残りで、その中に収まる分だけを札のまま出す。
/// 札の並びは前ほど大事（見つからない・更新あり・取り込み中・未編集・所持）なので、丸にするのは後ろから——大事な札ほど文字のまま残る。
/// 丸は同じ部品のまま描き方だけを替える（<see cref="StatusBadge"/>）。カードは数千枚並ぶので、丸のための部品は足さない。
/// 押せる札（更新あり）は丸になっても同じボタンなので、押せば札と同じ動きをする。
///
/// 吹き出しは札の部品に直に書かず <see cref="TipProperty"/> に書く。丸のときは札の名前を頭に足した吹き出しに替える（文字が無いので、名前が分からない）。
/// </summary>
public sealed class BadgeOverflowPanel : Panel
{
    /// <summary>札の名前。丸にしたときの吹き出しの頭に書く。</summary>
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.RegisterAttached("Label", typeof(string), typeof(BadgeOverflowPanel),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    /// <summary>札の吹き出し（札のままのとき）。結び付けで替わったら並べ直して吹き出しを書き直す。</summary>
    public static readonly DependencyProperty TipProperty =
        DependencyProperty.RegisterAttached("Tip", typeof(string), typeof(BadgeOverflowPanel),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    private static readonly DependencyPropertyKey IsDotPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly("IsDot", typeof(bool), typeof(BadgeOverflowPanel),
            new FrameworkPropertyMetadata(false,
                FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// 丸にしたか。札（ボタンなら型の中の <see cref="StatusBadge"/>）まで継がれ、<see cref="StatusBadge"/> が丸に描く。
    /// </summary>
    public static readonly DependencyProperty IsDotProperty = IsDotPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey IsShownPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly("IsShown", typeof(bool), typeof(BadgeOverflowPanel), new PropertyMetadata(true));

    /// <summary>並べ直しの結果、この札を出したか（丸も出した内。全部を丸にしても入らないほど狭いときだけ、後ろの丸を出さない）。</summary>
    public static readonly DependencyProperty IsShownProperty = IsShownPropertyKey.DependencyProperty;

    public static bool GetIsShown(DependencyObject element) => (bool)element.GetValue(IsShownProperty);

    public static bool GetIsDot(DependencyObject element) => (bool)element.GetValue(IsDotProperty);

    public static string GetLabel(DependencyObject element) => (string)element.GetValue(LabelProperty);

    public static void SetLabel(DependencyObject element, string value) => element.SetValue(LabelProperty, value);

    public static string GetTip(DependencyObject element) => (string)element.GetValue(TipProperty);

    public static void SetTip(DependencyObject element, string value) => element.SetValue(TipProperty, value);

    /// <summary>札と札の間。</summary>
    public double Gap { get; set; } = 4;

    /// <summary>丸にした札の数（並べ直した直後の値）。</summary>
    public int DotCount { get; private set; }

    /// <summary>丸にしても入らず出さなかった札の数（並べ直した直後の値。ふつうのカードの幅では0）。</summary>
    public int HiddenCount { get; private set; }

    private int _shown;

    private List<UIElement> Present
        => Children.OfType<UIElement>().Where(child => child.Visibility != Visibility.Collapsed).ToList();

    /// <summary>丸の吹き出し。名前を1行目に、札の吹き出しがあれば2行目に書く（札のときに読めた物を減らさない）。</summary>
    public static string DotTipOf(string label, string tip) => tip.Length == 0 ? label : $"{label}\n{tip}";

    protected override Size MeasureOverride(Size availableSize)
    {
        var present = Present;
        var open = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (var badge in present)
        {
            badge.Measure(open);
        }

        // 札のままの幅は、今丸に描いている札でも StatusBadge が測っておく（丸から戻せるかを、丸を解かずに見られる）
        var full = present.Select(FullWidthOf).ToList();
        var dotWidth = StatusBadge.DotSlot + Gap;
        var dots = 0;
        while (dots < present.Count
               && full.Take(present.Count - dots).Sum(width => width + Gap) + dots * dotWidth > availableSize.Width)
        {
            dots++;
        }

        DotCount = dots;
        _shown = present.Count;
        if (dots == present.Count)
        {
            // 全部を丸にしても入らないほど狭い。後ろの丸から出さない（カードの左端を越えない）
            _shown = Math.Min(present.Count, (int)Math.Floor((availableSize.Width + 0.01) / dotWidth));
        }

        HiddenCount = present.Count - _shown;
        for (var i = 0; i < present.Count; i++)
        {
            var badge = present[i];
            var dot = i >= present.Count - dots;
            if (GetIsDot(badge) != dot)
            {
                badge.SetValue(IsDotPropertyKey, dot);
                badge.Measure(open);
            }

            ApplyTip(badge, dot);
        }

        var height = present.Select(badge => badge.DesiredSize.Height).DefaultIfEmpty(0).Max();
        var width = present.Take(_shown).Sum(badge => badge.DesiredSize.Width + Gap);
        return new Size(Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // 右端から左へ。出さない札は大きさ0で置く（Collapsed にすると、結び付けの値を上書きしてしまう）
        var order = Enumerable.Reverse(Present.Take(_shown)).ToList();
        var right = finalSize.Width;
        foreach (var child in order)
        {
            var left = right - child.DesiredSize.Width;
            child.Arrange(new Rect(left, 0, child.DesiredSize.Width, finalSize.Height));
            right = left - Gap;
        }

        foreach (var child in Children.OfType<UIElement>())
        {
            var shown = order.Contains(child);
            child.SetValue(IsShownPropertyKey, shown);
            if (!shown)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
            }
        }

        return finalSize;
    }

    /// <summary>札のままの幅。丸に描ける札（自分か型の根が <see cref="StatusBadge"/>）はそれが測った幅、ほかは測った幅のまま。</summary>
    private static double FullWidthOf(UIElement badge)
    {
        var drawn = BadgeOf(badge);
        if (drawn is null)
        {
            return badge.DesiredSize.Width;
        }

        // ボタンのように外に余白を持つ部品は、その分を足す（外の幅 − 中の今の幅 ＋ 中の札のままの幅）
        return badge.DesiredSize.Width - drawn.DesiredSize.Width + drawn.FullWidth;
    }

    private static StatusBadge? BadgeOf(UIElement badge) => badge switch
    {
        StatusBadge self => self,
        Control { } control when System.Windows.Media.VisualTreeHelper.GetChildrenCount(control) > 0
            => System.Windows.Media.VisualTreeHelper.GetChild(control, 0) as StatusBadge,
        _ => null,
    };

    private static void ApplyTip(UIElement badge, bool dot)
    {
        if (badge is not FrameworkElement element)
        {
            return;
        }

        var tip = GetTip(element) ?? "";
        object? next = dot ? DotTipOf(GetLabel(element) ?? "", tip) : (tip.Length == 0 ? null : tip);
        if (!Equals(element.ToolTip, next))
        {
            element.ToolTip = next;
        }
    }
}
