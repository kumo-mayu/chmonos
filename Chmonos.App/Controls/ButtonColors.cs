using System.Windows;
using System.Windows.Media;

namespace Chmonos.App.Controls;

/// <summary>
/// 暗黙の Button の型（`App.xaml`）が「乗せたとき」に使う地と枠の色の口。
/// 色だけ違うボタン（`PrimaryButton`）が型を丸ごと写すと、土台の型を直しても届かない。
/// 型は <see cref="HoverBackground"/>・<see cref="HoverBorderBrush"/> を読み、スタイルが色だけを差し替える。
/// 既定は土台のスタイルが決める（地は `SurfaceAlt`、枠はそのボタンの今の `BorderBrush`＝変えない）。
/// </summary>
public static class ButtonColors
{
    /// <summary>乗せたときの地。</summary>
    public static readonly DependencyProperty HoverBackgroundProperty = DependencyProperty.RegisterAttached(
        "HoverBackground", typeof(Brush), typeof(ButtonColors), new PropertyMetadata(null));

    /// <summary>乗せたときの枠。</summary>
    public static readonly DependencyProperty HoverBorderBrushProperty = DependencyProperty.RegisterAttached(
        "HoverBorderBrush", typeof(Brush), typeof(ButtonColors), new PropertyMetadata(null));

    public static Brush? GetHoverBackground(DependencyObject element) => (Brush?)element.GetValue(HoverBackgroundProperty);

    public static void SetHoverBackground(DependencyObject element, Brush? value) => element.SetValue(HoverBackgroundProperty, value);

    public static Brush? GetHoverBorderBrush(DependencyObject element) => (Brush?)element.GetValue(HoverBorderBrushProperty);

    public static void SetHoverBorderBrush(DependencyObject element, Brush? value) => element.SetValue(HoverBorderBrushProperty, value);
}
