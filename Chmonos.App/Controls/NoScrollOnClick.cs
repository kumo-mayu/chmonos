using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Chmonos.App.Controls;

/// <summary>
/// 一覧の中の物を**マウスで押したときは、見える所まで流さない**（2026-10-02 のメモ7「この機能は必要ない」）。
/// キーボードで移ったとき（矢印・Tab）は今までどおり流す——見えない所へ移るので、流さないと止まった所が見えない。
///
/// 流していたのは WPF の既定の動き：部品がキーボードのフォーカスを受けると、自分を見える所まで流すよう頼む
/// （<see cref="FrameworkElement.BringIntoView()"/> → <see cref="FrameworkElement.RequestBringIntoViewEvent"/> → 外の ScrollViewer が流す）。
/// ボタンは押した瞬間（マウスのボタンを下ろしたとき）にフォーカスを取るので、一部だけ見えているショップのカード
/// （ボタン）を押すと、全体が見える所まで流れてから開いていた。
///
/// 頼みを受ける ScrollViewer は一覧の型の中にあり、一覧そのものに付けた受け口より先に受けてしまう。
/// そこで頼みが出た所（押された部品）で、全部の部品に効く受け口（型ごとの受け口）が先に見て止める。
/// 止めるのは「この印を付けた一覧の中で、マウスのボタンを下ろしてから上げるまでの間」に出た頼みだけ。
/// 印は一覧ごとに付ける（カードの一覧 <see cref="CardRowsListBox"/> は自分で付ける）。
/// </summary>
public static class NoScrollOnClick
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(NoScrollOnClick), new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>この一覧の中でマウスのボタンを下ろしてから、まだ上げていないか。</summary>
    private static readonly DependencyProperty IsPressingProperty = DependencyProperty.RegisterAttached(
        "IsPressing", typeof(bool), typeof(NoScrollOnClick), new PropertyMetadata(false));

    /// <summary>
    /// マウスのボタンが今も下りているか（実際の機器の状態）。一覧の外で離すと「上げた」が一覧に届かず印が残るので、
    /// 機器の状態でも見る——残った印で、後から来たキーボードや戻ったときの位置合わせの流しを止めないように。
    /// 試験は機器を押せないので差し替える
    /// </summary>
    internal static Func<bool> IsAnyButtonDown { get; set; } = () =>
        Mouse.LeftButton == MouseButtonState.Pressed
        || Mouse.RightButton == MouseButtonState.Pressed
        || Mouse.MiddleButton == MouseButtonState.Pressed;

    static NoScrollOnClick()
    {
        // 型ごとの受け口は、頼みが通る道の部品ごとに、その部品の受け口より先に呼ばれる。押された部品の所で止めれば、
        // 一覧の型の中の ScrollViewer まで届かない（ScrollViewer は自分の型の受け口で流すので、一覧に付けた受け口では間に合わない）
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.RequestBringIntoViewEvent,
            new RequestBringIntoViewEventHandler(OnRequestBringIntoView));
    }

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not UIElement element)
        {
            return;
        }

        // 中の部品が「押した」を受けて止めても（ボタンは止める）見えるように、止められた物も受ける
        if (e.NewValue is true)
        {
            element.AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDown), handledEventsToo: true);
            element.AddHandler(UIElement.PreviewMouseUpEvent, new MouseButtonEventHandler(OnPreviewMouseUp), handledEventsToo: true);
            element.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown), handledEventsToo: true);
        }
        else
        {
            element.RemoveHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDown));
            element.RemoveHandler(UIElement.PreviewMouseUpEvent, new MouseButtonEventHandler(OnPreviewMouseUp));
            element.RemoveHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown));
            element.ClearValue(IsPressingProperty);
        }
    }

    private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e) => ((DependencyObject)sender).SetValue(IsPressingProperty, true);

    private static void OnPreviewMouseUp(object sender, MouseButtonEventArgs e) => ((DependencyObject)sender).ClearValue(IsPressingProperty);

    // キーを押したら、それはキーボードで移る操作。押していた印が残っていても外す
    private static void OnPreviewKeyDown(object sender, KeyEventArgs e) => ((DependencyObject)sender).ClearValue(IsPressingProperty);

    private static void OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        // 道の上の部品ごとに呼ばれる。頼みが出た所で1回だけ見る（その先の部品で同じことを繰り返さない）
        if (!ReferenceEquals(sender, e.TargetObject) || sender is not DependencyObject target)
        {
            return;
        }

        for (DependencyObject? current = target; current is not null; current = ParentOf(current))
        {
            if (!GetIsEnabled(current))
            {
                continue;
            }

            if ((bool)current.GetValue(IsPressingProperty) && IsAnyButtonDown())
            {
                e.Handled = true;
            }

            // 一番近い印の一覧で決める（一覧の中に一覧があっても、外の印では決めない）
            return;
        }
    }

    private static DependencyObject? ParentOf(DependencyObject element)
        => element is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);
}
