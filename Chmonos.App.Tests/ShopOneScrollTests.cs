using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;

namespace Chmonos.App.Tests;

/// <summary>
/// ショップの中は、上の段（バナー・見出しと集計・メモ）と商品の一覧を1つのスクロールにする（2026-10-03 のメモ24）。
/// 名前の段が流れ去ったら1行の見出しを重ね、「このショップの商品」の行はそのすぐ下で止める。
/// 計算で決まる所（出す条件・止める量・重なりの下まで流す位置）は値で、部品の動き（流す・ホイール・位置の戻し）は窓口に載せて確かめる。
/// </summary>
public class ShopOneScrollTests
{
    [Theory]
    // 名前の段が見えている間は出さない（同じ物が2つ並ぶ）
    [InlineData(0, 474, false)]
    [InlineData(300, 474, false)]
    [InlineData(473, 474, false)]
    // 名前の段の下端が上端に着いたら出す（端数は0.5まで着いた扱い）
    [InlineData(473.5, 474, true)]
    [InlineData(474, 474, true)]
    [InlineData(900, 474, true)]
    // まだ測れていない（下端が0）間は、一番上でも出さない
    [InlineData(0, 0, false)]
    [InlineData(10, 0, false)]
    public void 名前の段の下端が上端に着いたら_1行の見出しを出す(double offset, double nameBottom, bool expected)
    {
        Assert.Equal(expected, ShopViewModel.ShowsCompactHeader(offset, nameBottom));
    }

    [Theory]
    // 行が上端（1行の見出しのすぐ下）に着くまでは動かさない
    [InlineData(0, 600, 52, 0)]
    [InlineData(547, 600, 52, 0)]
    [InlineData(548, 600, 52, 0)]
    // 着いたら、流した分だけ下げて止める
    [InlineData(600, 600, 52, 52)]
    [InlineData(1000, 600, 52, 452)]
    public void 商品の行は_上端に着いたら流した分だけ下げて止める(double offset, double rowTop, double pinTop, double expected)
    {
        Assert.Equal(expected, ShopViewModel.StickyShift(offset, rowTop, pinTop), 3);
    }

    [Fact]
    public Task 流れの位置に合わせて_1行の見出しが出入りする() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var main = await app.StartAsync();
        await main.ShowShopAsync("sample-shop");
        var shop = Assert.IsType<ShopViewModel>(main.CurrentViewModel);
        await app.SettleAsync();

        Assert.False(shop.IsHeaderCompact);

        shop.NoteScrolled(offset: 600, nameBottom: 474);
        Assert.True(shop.IsHeaderCompact);

        // 少し戻して名前の段がまた見えたら、1行の見出しは引っ込む（重ねているだけなので、中身は動かない）
        shop.NoteScrolled(offset: 300, nameBottom: 474);
        Assert.False(shop.IsHeaderCompact);
    });

    [Fact]
    public Task 見方か絞りを変える直前に知らせる_同じ値のときは知らせない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var main = await app.StartAsync();
        await main.ShowShopAsync("sample-shop");
        var shop = Assert.IsType<ShopViewModel>(main.CurrentViewModel);
        await app.SettleAsync();

        var seen = new List<(bool Viewing, bool WasList)>();
        shop.ListAboutToChange += viewChanges => seen.Add((viewChanges, shop.IsListMode));

        shop.IsListMode = true;
        shop.IsListMode = true;
        shop.OwnedOnly = true;
        shop.OwnedOnly = true;
        shop.UpdatedOnly = true;

        // 変える直前の値で知らせる（変えた後では、見ていた一覧がもう隠れていて位置を読めない）
        Assert.Equal([(true, false), (true, true), (true, true)], seen);
        await app.SettleAsync();
    });

    [Theory]
    // もう見えていれば動かさない
    [InlineData(100, 200, 50, 160, 200, -1)]
    // 重ねた帯（上から50）の下に隠れているなら、帯の下に出す。WPF の既定は「見えている」と答えて動かさない所
    [InlineData(100, 200, 50, 120, 180, 70)]
    // 下にはみ出すなら、下端を合わせる
    [InlineData(100, 200, 50, 250, 350, 150)]
    // 画面（帯の下から下端まで）より高い物は、頭を帯の下に合わせる
    [InlineData(0, 200, 50, 400, 700, 350)]
    // 上へ流しても 0 より前へは行かない
    [InlineData(30, 200, 50, 10, 70, 0)]
    // 帯が無ければ、見えている範囲そのまま
    [InlineData(100, 200, 0, 110, 150, -1)]
    public void 重ねた帯の下に出す流れの位置(double offset, double viewport, double inset, double top, double bottom, double expected)
    {
        // expected が -1 のときは「動かさない」（null）
        var actual = InsetScrollViewer.OffsetToReveal(offset, viewport, inset, top, bottom);
        if (expected < 0)
        {
            Assert.Null(actual);
        }
        else
        {
            Assert.Equal(expected, actual!.Value, 3);
        }
    }

    [Fact]
    public Task 帯に重なって隠れたカードへ移ると_帯の下まで流れる() => WithScroll(inset: 100, (viewer, list, cards) =>
    {
        viewer.ScrollToVerticalOffset(300);
        Settle(viewer);

        // 2枚目は 360〜420。画面（300〜500）の中だが、上の 100 は帯に隠れている。WPF の既定なら動かさない
        var top = Top(cards[1], viewer.Content);
        cards[1].BringIntoView();
        Settle(viewer);

        Assert.Equal(top - 100, viewer.VerticalOffset, 3);
    });

    [Fact]
    public Task 内側に流せない一覧があっても_外のスクロールが帯の下まで流す() => WithScroll(inset: 100, (viewer, list, cards) =>
    {
        // 中の ListBox の ScrollViewer は、頼みを受けて「済んだ」にする。外へは届かない作り（それでも外を流す）
        Assert.NotNull(ContentItemsControl.FindScrollViewer(list));
        viewer.ScrollToVerticalOffset(300);
        Settle(viewer);

        var top = Top(cards[1], viewer.Content);
        cards[1].BringIntoView();
        Settle(viewer);

        Assert.Equal(top - 100, viewer.VerticalOffset, 3);
    }, listInside: true);

    [Fact]
    public Task 止めた行の中の部品は_帯の下ではないので流さない() => WithScroll(inset: 100, (viewer, list, cards) =>
    {
        viewer.ScrollToVerticalOffset(300);
        Settle(viewer);

        // 止めた行（IsPinned）は、帯のすぐ下に重ねてある。その中のチェックを押しても、行の下まで流さない
        InsetScrollViewer.SetIsPinned(cards[1], true);
        cards[1].BringIntoView();
        Settle(viewer);

        Assert.Equal(300, viewer.VerticalOffset, 3);
    });

    [Fact]
    public Task マウスで押した最中は_帯の下へも流さない() => WithScroll(inset: 100, (viewer, list, cards) =>
    {
        viewer.ScrollToVerticalOffset(300);
        Settle(viewer);

        var previous = NoScrollOnClick.IsAnyButtonDown;
        NoScrollOnClick.IsAnyButtonDown = () => true;
        try
        {
            cards[1].RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent });
            cards[1].BringIntoView();
            Settle(viewer);

            Assert.Equal(300, viewer.VerticalOffset, 3);
        }
        finally
        {
            NoScrollOnClick.IsAnyButtonDown = previous;
        }
    }, listInside: true);

    [Fact]
    public Task 一覧の上のホイールは_中の一覧で止まらず_外のスクロールを流す() => WithScroll(inset: 0, (viewer, list, cards) =>
    {
        Assert.Equal(0, viewer.VerticalOffset);

        // 実際の入力と同じ順：下りの知らせが済まなければ、上りの知らせを送る
        var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent, Source = cards[0] };
        cards[0].RaiseEvent(preview);
        if (!preview.Handled)
        {
            cards[0].RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.MouseWheelEvent, Source = cards[0] });
        }

        Settle(viewer);
        Assert.True(viewer.VerticalOffset > 0, "ホイールで全体が流れる");
    }, listInside: true, forwardWheel: true);

    [Fact]
    public Task 一覧の上のホイールを外へ渡さないと_中の一覧が受けて全体は流れない() => WithScroll(inset: 0, (viewer, list, cards) =>
    {
        var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent, Source = cards[0] };
        cards[0].RaiseEvent(preview);
        if (!preview.Handled)
        {
            cards[0].RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.MouseWheelEvent, Source = cards[0] });
        }

        Settle(viewer);
        Assert.Equal(0, viewer.VerticalOffset);
    }, listInside: true, forwardWheel: false);

    [Fact]
    public Task 外のスクロールの中の一覧は_先頭に見えていた商品を同じ高さに戻せる() => WithKeyedList(inset: 0, (viewer, list, cards) =>
    {
        viewer.ScrollToVerticalOffset(330);
        Settle(viewer);

        var anchor = ListScrollAnchor.Capture(viewer, list, entry => entry as string);
        Assert.NotNull(anchor);

        // 別の所へ流してから戻す（一覧の見方を切り替えて、流れの位置だけがずれたのと同じ）
        viewer.ScrollToVerticalOffset(0);
        Settle(viewer);
        Assert.True(ListScrollAnchor.Restore(viewer, list, anchor!, entry => entry is string key ? [key] : []));
        Settle(viewer);

        Assert.Equal(330, viewer.VerticalOffset, 1);
    });

    [Fact]
    public Task 覚えた商品が無くなっていたら_戻せないと答える() => WithKeyedList(inset: 0, (viewer, list, cards) =>
    {
        viewer.ScrollToVerticalOffset(330);
        Settle(viewer);

        Assert.False(ListScrollAnchor.Restore(viewer, list, new ListAnchor("無い商品", 0), entry => entry is string key ? [key] : []));
    });

    private const double ViewportHeight = 200;
    private const double CardHeight = 60;

    /// <summary>
    /// 高さ200の <see cref="InsetScrollViewer"/> に、先頭に高さ300の段を置き、その下にカード10枚（高さ60）を並べる。
    /// カードを ListBox（<see cref="CardRowsListBox"/>。中に自分の ScrollViewer を持つ）に入れるか、直に並べるか。
    /// フォーカスと配置を受けられる窓口（出さないポップアップの窓）に載せる
    /// </summary>
    private static Task WithScroll(
        double inset,
        Action<InsetScrollViewer, ItemsControl, Button[]> body,
        bool listInside = false,
        bool forwardWheel = false) => UiThread.Run(() =>
    {
        var cards = Enumerable.Range(0, 10).Select(index => new Button { Height = CardHeight, Content = $"カード{index}" }).ToArray();
        var panel = new StackPanel();
        panel.Children.Add(new Border { Height = 300 });

        ItemsControl list;
        if (listInside)
        {
            var box = new CardRowsListBox { ItemsSource = cards, Padding = new Thickness(0) };
            ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Disabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Disabled);
            VirtualizingPanel.SetIsVirtualizing(box, false);
            if (forwardWheel)
            {
                ForwardWheel.SetIsEnabled(box, true);
            }

            panel.Children.Add(box);
            list = box;
        }
        else
        {
            foreach (var card in cards)
            {
                panel.Children.Add(card);
            }

            list = new ListBox();
        }

        var viewer = new InsetScrollViewer
        {
            Height = ViewportHeight,
            Width = 200,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false,
            TopInset = inset,
            Content = panel,
        };

        using var source = new HwndSource(new HwndSourceParameters("ShopOneScrollTests")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP だけ（出さない）
            ExtendedWindowStyle = 0x00000080, // WS_EX_TOOLWINDOW
            PositionX = -30000,
            PositionY = -30000,
            Width = 1,
            Height = 1,
        })
        {
            RootVisual = viewer,
        };

        Settle(viewer);
        body(viewer, list, cards);
    });

    // 一覧を作る側のテストでは、項目は文字列にして鍵に使う（ListScrollAnchor）
    private static Task WithKeyedList(
        double inset,
        Action<InsetScrollViewer, ItemsControl, string[]> body) => UiThread.Run(() =>
    {
        var keys = Enumerable.Range(0, 10).Select(index => $"key{index}").ToArray();
        var box = new ListBox { ItemsSource = keys, Padding = new Thickness(0) };
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetIsVirtualizing(box, false);
        box.ItemContainerStyle = new Style(typeof(ListBoxItem))
        {
            Setters = { new Setter(FrameworkElement.HeightProperty, CardHeight) },
        };

        var panel = new StackPanel();
        panel.Children.Add(new Border { Height = 300 });
        panel.Children.Add(box);
        var viewer = new InsetScrollViewer { Height = ViewportHeight, Width = 200, CanContentScroll = false, TopInset = inset, Content = panel };

        using var source = new HwndSource(new HwndSourceParameters("ShopOneScrollTests")
        {
            WindowStyle = unchecked((int)0x80000000),
            ExtendedWindowStyle = 0x00000080,
            PositionX = -30000,
            PositionY = -30000,
            Width = 1,
            Height = 1,
        })
        {
            RootVisual = viewer,
        };

        Settle(viewer);
        body(viewer, box, keys);
    });

    /// <summary>流す頼みは並べ直しの後で効くので、並べ直しと、後回しにされた仕事を済ませる。</summary>
    private static void Settle(FrameworkElement element)
    {
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        element.UpdateLayout();
    }

    /// <summary>中身の上からの、部品の上端の位置。</summary>
    private static double Top(FrameworkElement element, object content)
        => element.TransformToAncestor((System.Windows.Media.Visual)content).Transform(new Point(0, 0)).Y;
}
