using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// フォーカスを受けた部品を見える所まで流すとき、外側に出す印の分だけ余分に流す（2026-10-03 の点検・案D）。
/// フォーカスを受けられる窓口に載せて、本当にフォーカスを移して確かめる（窓口が無いとフォーカスが移らず、流れもしない）。
/// </summary>
public class FocusScrollMarginTests
{
    private const double ViewportHeight = 100;
    private const double RowHeight = 60;

    [Fact]
    public Task 下に切れている部品へフォーカスが移ると_印の分だけ余分に流れる() => WithRows(rows =>
    {
        // 2つ目は 60〜120 にあり、見えているのは 100 まで。最小限なら 20 流す
        rows.Buttons[1].Focus();
        Settle(rows.Scroll);

        Assert.True(rows.Buttons[1].IsKeyboardFocused);
        Assert.Equal(RowHeight * 2 - ViewportHeight + FocusScrollMargin.Margin, rows.Scroll.VerticalOffset);
    });

    [Fact]
    public Task 上に切れている部品へフォーカスが移ると_印の分だけ余分に戻る() => WithRows(rows =>
    {
        rows.Scroll.ScrollToVerticalOffset(RowHeight + 10);
        Settle(rows.Scroll);

        // 2つ目（60〜120）の上の 10 が切れている。最小限なら 60 まで戻す
        rows.Buttons[1].Focus();
        Settle(rows.Scroll);

        Assert.Equal(RowHeight - FocusScrollMargin.Margin, rows.Scroll.VerticalOffset);
    });

    [Fact]
    public Task 全部見えている部品へ移っても_流れない() => WithRows(rows =>
    {
        rows.Buttons[0].Focus();
        Settle(rows.Scroll);

        // 1つ目（0〜60）は余白の 4 を足しても上は 0 で止まり、下も 64 で見えている
        Assert.Equal(0, rows.Scroll.VerticalOffset);
    });

    private sealed record Rows(ScrollViewer Scroll, Button[] Buttons);

    /// <summary>高さ100の流れに、高さ60のボタンを5つ縦に並べ、フォーカスを受けられる窓口に載せる。</summary>
    private static Task WithRows(Action<Rows> body) => UiThread.Run(() =>
    {
        FocusScrollMargin.Register();

        var buttons = Enumerable.Range(0, 5).Select(index => new Button { Height = RowHeight, Content = $"行{index}" }).ToArray();
        var panel = new StackPanel();
        foreach (var button in buttons)
        {
            panel.Children.Add(button);
        }

        var scroll = new ScrollViewer { Height = ViewportHeight, Width = 200, Content = panel, CanContentScroll = false };

        using var source = new HwndSource(new HwndSourceParameters("FocusScrollMarginTests")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP だけ（出さない）
            ExtendedWindowStyle = 0x00000080, // WS_EX_TOOLWINDOW
            PositionX = -30000,
            PositionY = -30000,
            Width = 1,
            Height = 1,
        })
        {
            RootVisual = scroll,
        };

        Settle(scroll);
        Assert.Equal(0, scroll.VerticalOffset);
        body(new Rows(scroll, buttons));
    });

    /// <summary>流す頼みは並べ直しの後で効くので、並べ直しと、後回しにされた仕事を済ませる。</summary>
    private static void Settle(FrameworkElement element)
    {
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        element.UpdateLayout();
    }
}
