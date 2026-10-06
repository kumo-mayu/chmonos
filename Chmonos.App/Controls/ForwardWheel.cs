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

    /// <summary>
    /// 高さに上限のある一覧（外の ScrollViewer の中で、自分の中を流す物）で、端まで流しきった後のホイールだけ外へ渡す
    /// （取り込み画面の、見つからないファイルを探した結果。メモ74）。
    /// 一覧の中の ScrollViewer は端でもホイールを「済んだ」にするので、一覧の上にマウスがあると、下の欄へ画面を送れなくなる。
    /// 中を流せる間は一覧が流れ、端に着いたら画面が流れる（ブラウザの入れ子のスクロールと同じ）
    /// </summary>
    public static readonly DependencyProperty AtEdgesProperty = DependencyProperty.RegisterAttached(
        "AtEdges", typeof(bool), typeof(ForwardWheel), new PropertyMetadata(false, OnAtEdgesChanged));

    public static bool GetAtEdges(DependencyObject element) => (bool)element.GetValue(AtEdgesProperty);

    public static void SetAtEdges(DependencyObject element, bool value) => element.SetValue(AtEdgesProperty, value);

    private static void OnAtEdgesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not UIElement element)
        {
            return;
        }

        element.PreviewMouseWheel -= OnPreviewMouseWheelAtEdges;
        if (e.NewValue is true)
        {
            element.PreviewMouseWheel += OnPreviewMouseWheelAtEdges;
        }
    }

    private static void OnPreviewMouseWheelAtEdges(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not DependencyObject element || InnerScroll(element) is not { } scroll)
        {
            return;
        }

        var atTop = scroll.VerticalOffset <= 0;
        var atBottom = scroll.VerticalOffset >= scroll.ScrollableHeight - 0.5;
        if ((e.Delta > 0 && atTop) || (e.Delta < 0 && atBottom))
        {
            OnPreviewMouseWheel(sender, e);
        }
    }

    /// <summary>一覧の型の中の ScrollViewer（いちばん外側の物）。</summary>
    private static System.Windows.Controls.ScrollViewer? InnerScroll(DependencyObject element)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is System.Windows.Controls.ScrollViewer scroll)
            {
                return scroll;
            }

            if (InnerScroll(child) is { } inner)
            {
                return inner;
            }
        }

        return null;
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
