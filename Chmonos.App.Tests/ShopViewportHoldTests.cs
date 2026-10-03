using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// ショップの中は上の段と商品の一覧が1本のスクロールで、一覧の WPF の仮想化が効かない。
/// 行の入れ物は全部作るが、中身は見えている辺りの行だけ作り、遠く離れた行の中身は捨てる（<see cref="ViewportHold"/>。2026-10-03）。
/// 300件の店で全部作ると、開いて落ち着くまで約2.5秒・メモリが約110MB多くかかった。
/// </summary>
public class ShopViewportHoldTests
{
    // 高さ200の窓に、先頭に高さ300の段、その下に高さ60の行を30行。行 i の上端は 300 + 60i
    private const double ViewportHeight = 200;
    private const double RowHeight = 60;
    private const int RowCount = 30;

    [Fact]
    public Task 一番上では_見えている辺りに行が無いので_どの行も作らない() => WithList((viewer, hold, list) =>
    {
        hold.Update(list);

        // 作るのは見えている所（0〜200）から上下に画面の 1/4（50）まで。行は 300 から
        Assert.Empty(Released(list));
    });

    [Fact]
    public Task 流すと_見えている辺りの行だけ控えを外す() => WithList((viewer, hold, list) =>
    {
        Scroll(viewer, 300);
        hold.Update(list);

        // 作る範囲は 250〜550。行0（300〜360）から行4（540〜600）まで
        Assert.Equal([0, 1, 2, 3, 4], Released(list));
    });

    [Fact]
    public Task 遠く離れた行は_控えに戻す() => WithList((viewer, hold, list) =>
    {
        Scroll(viewer, 300);
        hold.Update(list);
        Scroll(viewer, 1500);
        hold.Update(list);

        // 残すのは上下1画面（1300〜1900）まで。前に外した行0〜4（300〜600）は遠いので戻し、今の辺り（1450〜1750）の行19〜24を外す
        Assert.Equal([19, 20, 21, 22, 23, 24], Released(list));
    });

    [Fact]
    public Task 少し流しただけなら_外した行を控えに戻さない() => WithList((viewer, hold, list) =>
    {
        Scroll(viewer, 300);
        hold.Update(list);
        Scroll(viewer, 500);
        hold.Update(list);

        // 行0（300〜360）は作る範囲（450〜750）の外だが、残す範囲（300〜900）にかかる。境で作っては捨てるを繰り返さない
        Assert.Contains(0, Released(list));
    });

    [Fact]
    public Task 控えを外すのは控えている行だけ() => WithList((viewer, hold, list) =>
    {
        var row = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(0);
        Assert.True(ViewportHold.Release(row));
        Assert.False(ViewportHold.Release(row));

        // 控えを付けていない一覧（検索）の行は、外す物が無い
        Assert.False(ViewportHold.Release(new Border()));
    });

    [Fact]
    public Task カードは_控えている間は作らず_外すと作り_また控えると捨てる() => WithList((viewer, hold, list) =>
    {
        var hosts = Hosts(list);
        Settle(viewer);
        Assert.All(hosts, host => Assert.False(host.IsRealized));

        Scroll(viewer, 300);
        hold.Update(list);
        Settle(viewer);
        Assert.Equal([0, 1, 2, 3, 4], Realized(hosts));

        Scroll(viewer, 1500);
        hold.Update(list);
        Settle(viewer);
        Assert.Equal([19, 20, 21, 22, 23, 24], Realized(hosts));
    }, withCards: true);

    [Fact]
    public Task 控えの印を付けない一覧のカードは_今までどおり全部作る() => WithList((viewer, hold, list) =>
    {
        var hosts = Hosts(list);
        Settle(viewer);
        Assert.All(hosts, host => Assert.True(host.IsRealized));
    }, withCards: true, held: false);

    private static int[] Released(ItemsControl list)
        => [.. Enumerable.Range(0, list.Items.Count).Where(index =>
            !ViewportHold.GetIsHeld(list.ItemContainerGenerator.ContainerFromIndex(index)))];

    private static int[] Realized(IReadOnlyList<DeferredCardHost> hosts)
        => [.. Enumerable.Range(0, hosts.Count).Where(index => hosts[index].IsRealized)];

    private static List<DeferredCardHost> Hosts(ItemsControl list)
        => [.. Enumerable.Range(0, list.Items.Count).Select(index =>
            FindHost((DependencyObject)list.ItemContainerGenerator.ContainerFromIndex(index))
            ?? throw new InvalidOperationException($"行{index}にカードの入れ物がありません。"))];

    private static DeferredCardHost? FindHost(DependencyObject parent)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is DeferredCardHost host)
            {
                return host;
            }

            if (FindHost(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// 高さ200の <see cref="InsetScrollViewer"/> に、先頭に高さ300の段、その下に仮想化しない一覧（高さ60の行を30行）を置く。
    /// カードを入れるときは、行の中身を <see cref="DeferredCardHost"/> にする（検索・ショップのカードと同じ、後から作る入れ物）
    /// </summary>
    private static Task WithList(
        Action<InsetScrollViewer, ViewportHold, ListBox> body,
        bool withCards = false,
        bool held = true) => UiThread.Run(() =>
    {
        var list = new ListBox
        {
            ItemsSource = Enumerable.Range(0, RowCount).Select(index => $"商品{index}").ToArray(),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            ItemContainerStyle = new Style(typeof(ListBoxItem))
            {
                Setters =
                {
                    new Setter(FrameworkElement.HeightProperty, RowHeight),
                    new Setter(Control.PaddingProperty, new Thickness(0)),
                },
            },
        };
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetIsVirtualizing(list, false);
        if (held)
        {
            ViewportHold.SetIsHeld(list, true);
        }

        if (withCards)
        {
            var host = new FrameworkElementFactory(typeof(DeferredCardHost));
            host.SetValue(DeferredCardHost.RealTemplateProperty, new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(TextBlock)) });
            list.ItemTemplate = new DataTemplate { VisualTree = host };
        }

        var panel = new StackPanel();
        panel.Children.Add(new Border { Height = 300 });
        panel.Children.Add(list);
        var viewer = new InsetScrollViewer
        {
            Height = ViewportHeight,
            Width = 200,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false,
            Content = panel,
        };

        using var source = new HwndSource(new HwndSourceParameters("ShopViewportHoldTests")
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
        body(viewer, new ViewportHold(viewer, panel), list);
    });

    private static void Scroll(ScrollViewer viewer, double offset)
    {
        viewer.ScrollToVerticalOffset(offset);
        viewer.UpdateLayout();
    }

    /// <summary>並べ直しと、後回しにされた仕事（カードを作る順番待ちは Background）を済ませる。</summary>
    private static void Settle(FrameworkElement element)
    {
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        element.UpdateLayout();
    }
}
