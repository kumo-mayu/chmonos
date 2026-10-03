using System.Windows;
using System.Windows.Controls;

namespace Chmonos.App.Controls;

/// <summary>
/// 子を横1行に並べ、入り切らない子は出さないパネル。出さなかった数を、持ち主の一覧（<see cref="ItemsControl"/>）の
/// <see cref="HiddenCountProperty"/> に書く（その数で「ほか n 件」のボタンを出す）。
///
/// 商品ページの上の帯の「変わったところ」の並び（メモ17）。折り返すと、変わった所の多い商品で帯が何段にも伸び、
/// その分だけ本文が押し下げられる（ユーザ指示 2026-10-03「画面が一瞬で大きくズレるような動作は避ける」）。高さを1行に決めるために使う
/// </summary>
public sealed class OneLinePanel : Panel
{
    public static readonly DependencyProperty HiddenCountProperty =
        DependencyProperty.RegisterAttached("HiddenCount", typeof(int), typeof(OneLinePanel), new PropertyMetadata(0));

    public static int GetHiddenCount(DependencyObject element) => (int)element.GetValue(HiddenCountProperty);

    public static void SetHiddenCount(DependencyObject element, int value) => element.SetValue(HiddenCountProperty, value);

    /// <summary>子と子の間。</summary>
    public double Gap { get; set; } = 6;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0.0;
        var height = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            var next = width + (width > 0 ? Gap : 0) + child.DesiredSize.Width;
            if (next > availableSize.Width)
            {
                break;
            }

            width = next;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        // 1つも入らなくても高さは1行ぶん取る（帯の高さを変えない）
        foreach (UIElement child in InternalChildren)
        {
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        var hidden = 0;
        var full = false;
        foreach (UIElement child in InternalChildren)
        {
            var left = x + (x > 0 ? Gap : 0);
            full = full || left + child.DesiredSize.Width > finalSize.Width;
            if (full)
            {
                // 出さない子は幅0で置き、押せなくして Tab でも止まらないようにする（見えない物にフォーカスが行くと、どこにいるか分からない）。
                // 中の部品（一覧の行のボタン）ごと止めるので Focusable ではなく IsEnabled（どちらも配置には効かない）。
                // 止まらなくなった子は「ほか n 件」の一覧から押せる
                child.Arrange(new Rect(0, 0, 0, 0));
                child.SetCurrentValue(IsEnabledProperty, false);
                hidden++;
                continue;
            }

            child.Arrange(new Rect(left, (finalSize.Height - child.DesiredSize.Height) / 2, child.DesiredSize.Width, child.DesiredSize.Height));
            child.SetCurrentValue(IsEnabledProperty, true);
            x = left + child.DesiredSize.Width;
        }

        var owner = (DependencyObject?)ItemsControl.GetItemsOwner(this) ?? this;
        if (GetHiddenCount(owner) != hidden)
        {
            SetHiddenCount(owner, hidden);
        }

        return finalSize;
    }
}
