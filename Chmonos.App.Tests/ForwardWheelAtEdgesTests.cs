using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// 高さに上限のある一覧（取り込み画面の、見つからないファイルを探した結果。メモ74）は、中を流せる間はホイールで中が流れ、
/// 端まで流しきったら外の画面が流れる。一覧の中の ScrollViewer は端でもホイールを「済んだ」にするので、渡さないと一覧の上で画面が止まる。
/// </summary>
public class ForwardWheelAtEdgesTests
{
    [Fact]
    public Task 中を流せる間は一覧が流れ_下の端に着いたら画面が流れる() => WithNested((outer, inner) =>
    {
        Wheel(inner, -120);
        Settle(outer);
        Assert.True(inner.Scroll.VerticalOffset > 0);
        Assert.Equal(0, outer.VerticalOffset);

        inner.Scroll.ScrollToEnd();
        Settle(outer);
        Wheel(inner, -120);
        Settle(outer);
        Assert.True(outer.VerticalOffset > 0);
    });

    [Fact]
    public Task 上の端で上へ回すと画面が流れる() => WithNested((outer, inner) =>
    {
        outer.ScrollToVerticalOffset(100);
        Settle(outer);

        Wheel(inner, 120);
        Settle(outer);

        Assert.True(outer.VerticalOffset < 100);
        Assert.Equal(0, inner.Scroll.VerticalOffset);
    });

    /// <summary>
    /// 行の中の欄が開いた候補の窓（Popup）のホイールは、持ち主の一覧も通る。一覧が端にいても外へ渡さない
    /// （渡して「済んだ」にすると候補の窓が流れない。ユーザ指摘 2026-11-20：変換の右の欄のつなぎ先を選ぶ欄）
    /// </summary>
    [Fact]
    public Task 行の中の候補の窓のホイールは_一覧が端でも外へ渡さない() => UiThread.Run(() =>
    {
        var inPopup = new Border { Height = 20 };
        var list = new ItemsControl
        {
            MaxHeight = 100,
            Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                """
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ItemsControl">
                    <ScrollViewer VerticalScrollBarVisibility="Auto" CanContentScroll="False"><ItemsPresenter /></ScrollViewer>
                </ControlTemplate>
                """),
        };
        list.Items.Add(new System.Windows.Controls.Primitives.Popup { Child = inPopup });
        list.Items.Add(new Border { Height = 20 });
        ForwardWheel.SetAtEdges(list, true);
        ForwardWheel.SetIsEnabled(list, true);
        var outer = new ScrollViewer { Height = 200, Width = 200, Content = list, CanContentScroll = false };
        using var source = new HwndSource(new HwndSourceParameters("ForwardWheelPopupTests")
        {
            WindowStyle = unchecked((int)0x80000000),
            ExtendedWindowStyle = 0x00000080,
            PositionX = -30000,
            PositionY = -30000,
            Width = 1,
            Height = 1,
        })
        {
            RootVisual = outer,
        };
        Settle(outer);

        // 一覧は流せる量が無いので、上も下も端。前はここで外へ渡して「済んだ」にしていた
        var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
        inPopup.RaiseEvent(preview);

        Assert.False(preview.Handled);
    });

    private sealed record Inner(ItemsControl List, ScrollViewer Scroll);

    /// <summary>一覧の行の上でホイールを回す（知らせは画面の根から行まで下り、行から上る。一覧の中の ScrollViewer も道の上にある）。</summary>
    private static void Wheel(Inner inner, int delta)
    {
        var target = (UIElement)inner.Scroll.Content;
        var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
        target.RaiseEvent(preview);
        if (!preview.Handled)
        {
            target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = UIElement.MouseWheelEvent });
        }
    }

    /// <summary>高さ200の画面の中に、上の余白・高さ100の一覧（30行）・下の余白を縦に並べる。</summary>
    private static Task WithNested(Action<ScrollViewer, Inner> body) => UiThread.Run(() =>
    {
        var list = new ItemsControl
        {
            MaxHeight = 100,
            ItemsSource = Enumerable.Range(0, 30).Select(index => $"行{index}").ToList(),
            Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                """
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ItemsControl">
                    <ScrollViewer VerticalScrollBarVisibility="Auto" CanContentScroll="False"><ItemsPresenter /></ScrollViewer>
                </ControlTemplate>
                """),
        };
        ForwardWheel.SetAtEdges(list, true);

        var panel = new StackPanel();
        panel.Children.Add(new Border { Height = 50 });
        panel.Children.Add(list);
        panel.Children.Add(new Border { Height = 400 });
        var outer = new ScrollViewer { Height = 200, Width = 200, Content = panel, CanContentScroll = false };

        using var source = new HwndSource(new HwndSourceParameters("ForwardWheelAtEdgesTests")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP だけ（出さない）
            ExtendedWindowStyle = 0x00000080, // WS_EX_TOOLWINDOW
            PositionX = -30000,
            PositionY = -30000,
            Width = 1,
            Height = 1,
        })
        {
            RootVisual = outer,
        };

        Settle(outer);
        var inner = FindScroll(list) ?? throw new InvalidOperationException("一覧の中の ScrollViewer がありません。");
        body(outer, new Inner(list, inner));
    });

    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if ((child as ScrollViewer ?? FindScroll(child)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static void Settle(FrameworkElement element)
    {
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        element.UpdateLayout();
    }
}
