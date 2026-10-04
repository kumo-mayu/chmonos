using System.Windows;
using System.Windows.Controls;

namespace Chmonos.App.Controls;

/// <summary>
/// 一覧の行の右クリックのメニューを開く前に、その行を選ぶ（メモ35）。
///
/// ListBox は右クリックでは選ばない。タグの管理・属性の管理の左の一覧は、右の欄が「今選んでいる行」の操作を持つので、
/// 選ばないままメニューを開くと、押した物が別の行に効く。メニューの項目は右の欄と同じ命令を使うため、先に選んでおく。
/// マウスの右ボタンでもキーボード（Shift+F10・アプリケーションキー）でも、メニューを開く前の同じ出来事（ContextMenuOpening）で選ぶ。
/// </summary>
public static class SelectOnContextMenu
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SelectOnContextMenu), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ListBoxItem item)
        {
            return;
        }

        item.ContextMenuOpening -= OnContextMenuOpening;
        if ((bool)e.NewValue)
        {
            item.ContextMenuOpening += OnContextMenuOpening;
        }
    }

    private static void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            item.IsSelected = true;
        }
    }
}
