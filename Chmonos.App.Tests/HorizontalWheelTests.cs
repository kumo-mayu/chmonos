using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// Shift＋ホイールで横に送る（<see cref="HorizontalWheel"/>。ユーザ指摘 2026-10-02 メモ1・メモ4）。
///
/// 入れ物にホイールの知らせ（下りの PreviewMouseWheel）を送り、横の位置が動くかを見る。
/// 本物の Shift は押せないので、修飾キーを答える所を差し替える。実機のマウスでは見ていない
/// </summary>
public class HorizontalWheelTests
{
    private static void Settle(FrameworkElement root)
    {
        root.Measure(new Size(300, 200));
        root.Arrange(new Rect(0, 0, 300, 200));
        root.UpdateLayout();
    }

    private static void Wheel(UIElement target, int delta)
    {
        target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
    }

    private static void WithShift(ModifierKeys modifiers, Action body)
    {
        HorizontalWheel.Register();
        var before = HorizontalWheel.Modifiers;
        HorizontalWheel.Modifiers = () => modifiers;
        try
        {
            body();
        }
        finally
        {
            HorizontalWheel.Modifiers = before;
        }
    }

    /// <summary>横にだけ送れる入れ物（商品ページ・改変の詳細の本文と同じ置き方）。中身は 1000px。</summary>
    private static (ScrollViewer Viewer, Border Inner) Wide(double width = 1000)
    {
        var inner = new Border { Width = width, Height = 100 };
        var viewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = inner,
        };
        return (viewer, inner);
    }

    [Fact]
    public Task Shiftとホイールで_手前へ回すと右へ送り_奥へ回すと左へ戻す() => UiThread.Run(() => WithShift(ModifierKeys.Shift, () =>
    {
        var (viewer, inner) = Wide();
        Settle(viewer);

        Wheel(inner, -120);
        viewer.UpdateLayout();
        Assert.Equal(120, viewer.HorizontalOffset);

        Wheel(inner, 120);
        viewer.UpdateLayout();
        Assert.Equal(0, viewer.HorizontalOffset);
    }));

    [Theory]
    [InlineData(ModifierKeys.None)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift)]
    public Task Shift_だけでないときは_横に送らない(ModifierKeys modifiers) => UiThread.Run(() => WithShift(modifiers, () =>
    {
        var (viewer, inner) = Wide();
        Settle(viewer);

        Wheel(inner, -120);
        viewer.UpdateLayout();
        Assert.Equal(0, viewer.HorizontalOffset);
    }));

    [Fact]
    public Task 画面全体の入れ物_ViewportFitHost_も送る() => UiThread.Run(() => WithShift(ModifierKeys.Shift, () =>
    {
        // 主の窓の ScreenScroller と同じ置き方：送る仕組みを ViewportFitHost が持ち、中身は最小の幅のせいで窓より広い
        var inner = new Border { MinWidth = 900, Height = 100 };
        var host = new ViewportFitHost { Child = inner };
        var viewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = true,
            Content = host,
        };
        Settle(viewer);
        Assert.True(viewer.ScrollableWidth > 0);

        Wheel(inner, -120);
        viewer.UpdateLayout();
        Assert.Equal(120, host.HorizontalOffset);
    }));

    [Fact]
    public Task 内側が右端まで来ていたら_外側の入れ物が続けて送る() => UiThread.Run(() => WithShift(ModifierKeys.Shift, () =>
    {
        // 組み込んだ商品ページ（内側）を送り切ったら、画面全体（外側）が送る
        var (innerViewer, inner) = Wide(width: 400);
        innerViewer.Width = 300;
        var outer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { innerViewer, new Border { Width = 600 } } },
        };
        Settle(outer);

        Wheel(inner, -120);
        outer.UpdateLayout();
        Assert.Equal(100, innerViewer.HorizontalOffset);
        Assert.Equal(0, outer.HorizontalOffset);

        Wheel(inner, -120);
        outer.UpdateLayout();
        Assert.Equal(100, innerViewer.HorizontalOffset);
        Assert.Equal(120, outer.HorizontalOffset);
    }));

    [Fact]
    public Task 横に送れる入れ物が無ければ_知らせを済ませず_縦に流れるままにする() => UiThread.Run(() => WithShift(ModifierKeys.Shift, () =>
    {
        var (viewer, inner) = Wide(width: 200);
        Settle(viewer);

        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
        inner.RaiseEvent(args);
        Assert.False(args.Handled);
    }));
}
