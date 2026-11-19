using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索のカードは、中身を画面のスレッドが空いた時に4枚ずつ作る（<see cref="DeferredCardHost"/>）。
/// 上へ流すたびに WPF の一覧が一番下の行の入れ物を作り直すので、待たせると1コマだけ中身の無い枠が見え、末尾の行が点滅する。
/// 2026-11-10 に「前に作った商品は4枚までその場で作る」で直したが、1行が5枚以上だと5枚目から待たされ、点滅が残っていた（2026-11-19）。
/// </summary>
public class DeferredCardHostChurnTests
{
    /// <summary>広い窓・小さいカードの1行。4枚の枠を超える数</summary>
    private const int Columns = 8;

    [Fact]
    public Task 外された行と同じ商品の入れ物は_5枚以上でも待たせずに作る() => WithRow((panel, items) =>
    {
        // 一覧が行を作り直すのと同じに、同じ仕事の中で入れ物を外して、同じ商品の新しい入れ物を入れる
        var hosts = Replace(panel, items);
        Layout(panel);

        Assert.Equal(Columns, hosts.Count(host => host.IsRealized));
    });

    [Fact]
    public Task 後の仕事で入ってきた前に見た商品は_今どおり4枚までその場で作る() => WithRow((panel, items) =>
    {
        // 前に見た行を速く戻すときは、外したのとは別の仕事で入ってくる。まとめて作ると固まるので、ここは4枚までのまま
        panel.Children.Clear();
        Settle(panel);
        var hosts = Add(panel, items);
        Layout(panel);

        Assert.Equal(4, hosts.Count(host => host.IsRealized));

        // 残りは順番待ちで後から作られる
        Settle(panel);
        Assert.Equal(Columns, hosts.Count(host => host.IsRealized));
    });

    /// <summary>出さない窓に1行ぶんのカードの入れ物を並べ、全部作り終えてから本体を走らせる。</summary>
    private static Task WithRow(Action<StackPanel, object[]> body) => UiThread.Run(() =>
    {
        var items = Enumerable.Range(0, Columns).Select(_ => new object()).ToArray();
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Width = 800, Height = 100 };
        using var source = new HwndSource(new HwndSourceParameters("DeferredCardHostChurnTests")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP だけ（出さない）
            ExtendedWindowStyle = 0x00000080, // WS_EX_TOOLWINDOW
            PositionX = -30000,
            PositionY = -30000,
            Width = 1,
            Height = 1,
        })
        {
            RootVisual = panel,
        };

        var first = Add(panel, items);
        Settle(panel);
        Assert.All(first, host => Assert.True(host.IsRealized));

        body(panel, items);
    });

    private static List<DeferredCardHost> Replace(StackPanel panel, object[] items)
    {
        panel.Children.Clear();
        return Add(panel, items);
    }

    private static List<DeferredCardHost> Add(StackPanel panel, object[] items)
    {
        var hosts = items.Select(item => new DeferredCardHost
        {
            DataContext = item,
            Width = 100,
            RealTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(TextBlock)) },
        }).ToList();
        foreach (var host in hosts)
        {
            panel.Children.Add(host);
        }

        return hosts;
    }

    /// <summary>並べ直しと、外した・入れたの知らせ（Loaded の優先度）までを済ませる。順番待ち（Background）はまだ走らせない。</summary>
    private static void Layout(FrameworkElement element)
    {
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
    }

    /// <summary>順番待ちまで全部済ませる。</summary>
    private static void Settle(FrameworkElement element)
    {
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        element.UpdateLayout();
    }
}
