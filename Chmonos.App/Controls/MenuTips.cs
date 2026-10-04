using System.Windows;

namespace Chmonos.App.Controls;

/// <summary>
/// メニュー項目の吹き出しを、押せるときと押せないときで分けて書く（`Themes/Controls.xaml` の MenuItem の既定の見た目が読む）。
///
/// **右クリックのメニューは、できない操作も出したまま押せなくして理由を言う**（ユーザ判断 2026-10-04）。
/// 普通の ToolTip は押せるときも出るので、「押せないときの理由」だけを別に持てるようにした。
/// </summary>
public static class MenuTips
{
    /// <summary>押せるときの吹き出し（何が起きるか）。</summary>
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(string), typeof(MenuTips), new PropertyMetadata(null));

    /// <summary>押せないときの吹き出し（その理由）。</summary>
    public static readonly DependencyProperty DisabledProperty = DependencyProperty.RegisterAttached(
        "Disabled", typeof(string), typeof(MenuTips), new PropertyMetadata(null));

    public static string? GetEnabled(DependencyObject element) => (string?)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, string? value) => element.SetValue(EnabledProperty, value);

    public static string? GetDisabled(DependencyObject element) => (string?)element.GetValue(DisabledProperty);

    public static void SetDisabled(DependencyObject element, string? value) => element.SetValue(DisabledProperty, value);
}
