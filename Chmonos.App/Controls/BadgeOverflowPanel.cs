using System.Windows;
using System.Windows.Controls;

namespace Chmonos.App.Controls;

/// <summary>
/// カードの下の段の札を、右寄せで横1行に並べるパネル。入り切らない札は出さず、最後に置いた「ほか」の札（<see cref="IsMoreProperty"/>）へ
/// 「+n」と、出さなかった札の名前の吹き出しを書く（メモ52）。
///
/// 札は右から左へ伸びる並びで、札が多いと左のカードの端を越えたり、左に置いた容量の文字に重なっていた。
/// 容量の文字は別の欄が先に取るので、このパネルが受ける幅は容量の残りで、その中に収まる分だけを出す。
/// 出す札は並びの前から（見つからない・更新あり・取り込み中・未編集・所持の順で、大事な物が前）
/// </summary>
public sealed class BadgeOverflowPanel : Panel
{
    public static readonly DependencyProperty IsMoreProperty =
        DependencyProperty.RegisterAttached("IsMore", typeof(bool), typeof(BadgeOverflowPanel), new PropertyMetadata(false));

    /// <summary>「ほか」の吹き出しに書く札の名前。</summary>
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.RegisterAttached("Label", typeof(string), typeof(BadgeOverflowPanel), new PropertyMetadata(""));

    private static readonly DependencyPropertyKey IsShownPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly("IsShown", typeof(bool), typeof(BadgeOverflowPanel), new PropertyMetadata(true));

    /// <summary>並べ直しの結果、この札を出したか（出さない札は大きさ0の枠に置くだけで、描かれない）。</summary>
    public static readonly DependencyProperty IsShownProperty = IsShownPropertyKey.DependencyProperty;

    public static bool GetIsShown(DependencyObject element) => (bool)element.GetValue(IsShownProperty);

    public static bool GetIsMore(DependencyObject element) => (bool)element.GetValue(IsMoreProperty);

    public static void SetIsMore(DependencyObject element, bool value) => element.SetValue(IsMoreProperty, value);

    public static string GetLabel(DependencyObject element) => (string)element.GetValue(LabelProperty);

    public static void SetLabel(DependencyObject element, string value) => element.SetValue(LabelProperty, value);

    /// <summary>札と札の間。</summary>
    public double Gap { get; set; } = 4;

    /// <summary>出さなかった札の数（並べ直した直後の値）。</summary>
    public int HiddenCount { get; private set; }

    private int _shown;

    private UIElement? More => Children.OfType<UIElement>().FirstOrDefault(child => GetIsMore(child));

    private List<UIElement> Present
        => Children.OfType<UIElement>().Where(child => !GetIsMore(child) && child.Visibility != Visibility.Collapsed).ToList();

    protected override Size MeasureOverride(Size availableSize)
    {
        var more = More;
        var present = Present;
        var open = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (var badge in present)
        {
            badge.Measure(open);
        }

        var height = present.Select(badge => badge.DesiredSize.Height).DefaultIfEmpty(0).Max();
        var total = present.Sum(badge => badge.DesiredSize.Width + Gap);

        _shown = present.Count;
        if (total > availableSize.Width && more is not null)
        {
            // 「+n」の札の幅は桁で変わるので、いちばん多いときの数で見込んで、残る幅から出せる数を決める
            SetMore(more, present.Count, present.Select(badge => GetLabel(badge)));
            more.Measure(open);
            var room = availableSize.Width - more.DesiredSize.Width - Gap;
            _shown = 0;
            var used = 0.0;
            foreach (var badge in present)
            {
                used += badge.DesiredSize.Width + Gap;
                if (used > room)
                {
                    break;
                }

                _shown++;
            }

            var hidden = present.Skip(_shown).Select(badge => GetLabel(badge)).ToList();
            SetMore(more, hidden.Count, hidden);
            more.Measure(open);
            height = Math.Max(height, more.DesiredSize.Height);
        }

        HiddenCount = present.Count - _shown;
        var width = present.Take(_shown).Sum(badge => badge.DesiredSize.Width + Gap)
                    + (HiddenCount > 0 && more is not null ? more.DesiredSize.Width + Gap : 0);
        return new Size(Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // 右端から左へ。出さない札は大きさ0で置く（Collapsed にすると、結び付けの値を上書きしてしまう）
        var more = More;
        var order = new List<UIElement>();
        if (HiddenCount > 0 && more is not null)
        {
            order.Add(more);
        }

        order.AddRange(Enumerable.Reverse(Present.Take(_shown)));
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

    private static void SetMore(UIElement more, int hiddenCount, IEnumerable<string> labels)
    {
        if (more is not Border { Child: TextBlock text } border)
        {
            return;
        }

        text.Text = $"+{hiddenCount}";
        border.ToolTip = string.Join("・", labels.Where(label => label.Length > 0));
    }
}
