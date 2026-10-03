using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Chmonos.App.Controls;

/// <summary>
/// 縦のホイールを、一覧の中の ScrollViewer で止めず外へ渡す（ショップの中：一覧を1本のスクロールの中に置くため）。
///
/// 一覧（ListBox・ListView）は中に ScrollViewer を持ち、自分が流せなくてもホイールを「済んだ」にして外へ渡さない。
/// 外の ScrollViewer の中に一覧を置くと、一覧の上でホイールを回しても全体が流れない。
/// 下りの知らせ（Preview）で受けて自分で止め、外の親に同じ向きの知らせを上げ直す。
/// Shift＋ホイールの横送り（<see cref="HorizontalWheel"/>）は、それより上流で済ませてあるので手を出さない。
/// </summary>
public static class ForwardWheel
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ForwardWheel), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not UIElement element)
        {
            return;
        }

        element.PreviewMouseWheel -= OnPreviewMouseWheel;
        if (e.NewValue is true)
        {
            element.PreviewMouseWheel += OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not DependencyObject element)
        {
            return;
        }

        // 外の親（見た目の木で1つ上）に上げ直す。上げ直した知らせは外の ScrollViewer まで上る
        if (VisualTreeHelper.GetParent(element) is not UIElement parent)
        {
            return;
        }

        e.Handled = true;
        parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender,
        });
    }
}
