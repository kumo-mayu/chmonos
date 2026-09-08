using System.Windows;
using System.Windows.Media;

namespace BoothAssetManager.App.Views;

/// <summary>
/// ナビの1項目に持たせる値。
///
/// 11項目が同じ形（アイコン・名前・バッジ）なので、テンプレートを1つにして
/// 項目ごとの違いだけをここから流し込む。
/// 折り畳んだときに名前を消す・バッジを寄せる、といった変更が1箇所で済む。
/// </summary>
public static class Nav
{
    /// <summary>24×24の座標系で描いた線画。Viewboxで16pxへ縮める。</summary>
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.RegisterAttached("Icon", typeof(Geometry), typeof(Nav));

    public static Geometry? GetIcon(DependencyObject element) => (Geometry?)element.GetValue(IconProperty);

    public static void SetIcon(DependencyObject element, Geometry? value) => element.SetValue(IconProperty, value);

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.RegisterAttached("Label", typeof(string), typeof(Nav));

    public static string? GetLabel(DependencyObject element) => (string?)element.GetValue(LabelProperty);

    public static void SetLabel(DependencyObject element, string? value) => element.SetValue(LabelProperty, value);

    /// <summary>今この画面を見ているか。文字とアイコンの色を切り替える。</summary>
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.RegisterAttached("IsActive", typeof(bool), typeof(Nav));

    public static bool GetIsActive(DependencyObject element) => (bool)element.GetValue(IsActiveProperty);

    public static void SetIsActive(DependencyObject element, bool value) => element.SetValue(IsActiveProperty, value);

    /// <summary>残っている作業の件数。0のときはバッジごと出さない。</summary>
    public static readonly DependencyProperty BadgeProperty =
        DependencyProperty.RegisterAttached("Badge", typeof(int), typeof(Nav));

    public static int GetBadge(DependencyObject element) => (int)element.GetValue(BadgeProperty);

    public static void SetBadge(DependencyObject element, int value) => element.SetValue(BadgeProperty, value);

    public static readonly DependencyProperty BadgeBrushProperty =
        DependencyProperty.RegisterAttached("BadgeBrush", typeof(Brush), typeof(Nav));

    public static Brush? GetBadgeBrush(DependencyObject element) => (Brush?)element.GetValue(BadgeBrushProperty);

    public static void SetBadgeBrush(DependencyObject element, Brush? value)
        => element.SetValue(BadgeBrushProperty, value);
}
