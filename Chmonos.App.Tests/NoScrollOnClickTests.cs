using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// 一部だけ見えているカードをマウスで押しても流れず、キーボードで移ったときは流れる（2026-10-02 のメモ7-②）。
///
/// 流れるのは WPF の既定の動き（フォーカスを受けた部品が自分を見える所まで流させる）なので、
/// フォーカスを受けられる窓口（出さないポップアップの窓・画面の外）に載せて、本当にフォーカスを移して確かめる。
/// 窓口の無い部品ではフォーカスが移らず、流れもしない（直しを戻しても通ってしまう）。
/// </summary>
public class NoScrollOnClickTests
{
    private const double ViewportHeight = 100;
    private const double CardHeight = 60;

    [Fact]
    public Task カードの一覧は_マウスで押したときに流さない印を自分で付ける() => UiThread.Run(() =>
    {
        Assert.True(NoScrollOnClick.GetIsEnabled(new CardRowsListBox()));
    });

    [Fact]
    public Task 一部だけ見えているカードをマウスで押しても_一覧は流れない() => WithList((list, cards) =>
    {
        // 2枚目は 60〜120 にあり、見えているのは 100 まで（下の 20 が切れている）
        Press(cards[1]);

        Assert.True(cards[1].IsKeyboardFocused);
        Assert.Equal(0, ScrollOf(list).VerticalOffset);
    });

    [Fact]
    public Task キーボードで移ったときは_見える所まで流れる() => WithList((list, cards) =>
    {
        Press(cards[0]);
        Release(cards[0]);

        // 矢印で隣へ移ったのと同じ：キーを押してから、隣の部品へフォーカスを移す
        cards[0].RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(cards[0])!, 0, Key.Down)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });
        cards[1].Focus();
        Settle(list);

        Assert.True(cards[1].IsKeyboardFocused);
        Assert.True(ScrollOf(list).VerticalOffset > 0, "隣のカードが見える所まで流れる");
    });

    [Fact]
    public Task 押したまま一覧の外で離しても_印が残って後の流しを止めない() => WithList((list, cards) =>
    {
        // 「上げた」が一覧に届かなかった（外で離した）。機器のボタンはもう上がっている
        list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent });
        NoScrollOnClick.IsAnyButtonDown = () => false;

        cards[1].Focus();
        Settle(list);

        Assert.True(ScrollOf(list).VerticalOffset > 0, "戻ったときの位置合わせなど、後から来た流しは効く");
    });

    /// <summary>高さ100の一覧に、高さ60のボタン（カードの代わり）を5枚並べ、フォーカスを受けられる窓口に載せる。</summary>
    private static Task WithList(Action<CardRowsListBox, Button[]> body) => UiThread.Run(() =>
    {
        var cards = Enumerable.Range(0, 5).Select(index => new Button { Height = CardHeight, Content = $"カード{index}" }).ToArray();
        var list = new CardRowsListBox { Height = ViewportHeight, Width = 200, ItemsSource = cards };
        ScrollViewer.SetCanContentScroll(list, false);

        using var source = new HwndSource(new HwndSourceParameters("NoScrollOnClickTests")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP だけ（出さない）
            ExtendedWindowStyle = 0x00000080, // WS_EX_TOOLWINDOW
            PositionX = -30000,
            PositionY = -30000,
            Width = 1,
            Height = 1,
        })
        {
            RootVisual = list,
        };

        var previous = NoScrollOnClick.IsAnyButtonDown;
        NoScrollOnClick.IsAnyButtonDown = () => true;
        try
        {
            Settle(list);
            Assert.Equal(0, ScrollOf(list).VerticalOffset);
            body(list, cards);
        }
        finally
        {
            NoScrollOnClick.IsAnyButtonDown = previous;
        }
    });

    /// <summary>マウスの左のボタンを下ろしたのと同じ知らせを送る（ボタンはここでフォーカスを取る）。</summary>
    private static void Press(Button card)
    {
        card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent });
        card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseDownEvent });
        Settle(card);
    }

    private static void Release(Button card)
    {
        card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseUpEvent });
        card.ReleaseMouseCapture();
        Settle(card);
    }

    /// <summary>流す頼みは並べ直しの後で効くので、並べ直しと、後回しにされた仕事を済ませる。</summary>
    private static void Settle(FrameworkElement element)
    {
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        element.UpdateLayout();
    }

    private static ScrollViewer ScrollOf(CardRowsListBox list)
        => ContentItemsControl.FindScrollViewer(list) ?? throw new InvalidOperationException("一覧の中の ScrollViewer が見つかりません。");
}
