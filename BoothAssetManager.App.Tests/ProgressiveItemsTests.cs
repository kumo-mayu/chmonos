using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using BoothAssetManager.App.Controls;
using BoothAssetManager.App.Tests.Support;

namespace BoothAssetManager.App.Tests;

/// <summary>
/// 商品の説明の見出しを、見えている分はその場で・画面の外の分は後から足す仕組み（<see cref="ProgressiveItems"/>）。
///
/// 「何行までが最初の配置に入るか」「いつ全部をその場で足すか」は大きさから計算で決まるので、決め打ちの大きさの部品で確かめる。
/// 配置を回すには部品がどこかに載っている必要があるので、出さない窓口（親がメッセージ専用）に載せる。窓は出ない。
/// 見た目（最初の1コマが前と同じか）は tools/ViewShot の item-page-long の場面で比べる
/// </summary>
public class ProgressiveItemsTests
{
    // 流せる画面の高さ 200・一覧の上に高さ 100 の物・1行の高さ 50。
    // 見える範囲 200 ＋ 先読み 300 ＝ 500 まで足すので、一覧の下端が 500 に届く8行（100 + 50×8）が最初の配置に入る
    private const int RowHeight = 50;
    private const int Rows = 40;
    private const int FirstRows = 8;

    [Fact]
    public Task 最初の配置では_見える範囲と先読みの分までを足し_残りは後から足す() => UiThread.Run(async () =>
    {
        using var host = new Host();
        host.Bind(Numbers(Rows));
        host.Layout();

        Assert.Equal(FirstRows, host.List.Items.Count);
        Assert.Equal(Rows - FirstRows, ProgressiveItems.PendingCount(host.List));

        await Idle();

        // 順も元のまま
        Assert.Equal(Numbers(Rows), host.List.Items.Cast<object>());
        Assert.Equal(0, ProgressiveItems.PendingCount(host.List));
    });

    [Fact]
    public Task 最初の配置に入る行は_先読みの分を足した数と合う() => UiThread.Run(() =>
    {
        // 先読みの量を変えたら、上の決め打ちの数（8行）も変わる。数の出どころを式で残す
        Assert.Equal(
            FirstRows,
            (int)Math.Ceiling((Host.ViewportHeight + ProgressiveItems.LookAhead - Host.HeaderHeight) / RowHeight));
    });

    [Fact]
    public Task 足している途中でも_足し切る命令で全部が並ぶ() => UiThread.Run(() =>
    {
        using var host = new Host();
        host.Bind(Numbers(Rows));
        host.Layout();
        Assert.True(host.List.Items.Count < Rows);

        // 画面内検索が探す前に呼ぶ道
        Assert.True(ProgressiveItems.FeedAllNow(host.Root));
        host.Layout();

        Assert.Equal(Numbers(Rows), host.List.Items.Cast<object>());
        Assert.Equal(Rows, host.RealizedRows);

        // 足す物が無ければ、並べ直さなくてよいと分かる
        Assert.False(ProgressiveItems.FeedAllNow(host.Root));
    });

    [Fact]
    public Task 足し切る命令は_渡した範囲の外の一覧には触らない() => UiThread.Run(() =>
    {
        using var host = new Host();
        using var other = new Host();
        host.Bind(Numbers(Rows));
        other.Bind(Numbers(Rows));
        host.Layout();
        other.Layout();

        ProgressiveItems.FeedAllNow(host.Root);

        Assert.Equal(Rows, host.List.Items.Count);
        Assert.Equal(FirstRows, other.List.Items.Count);

        // 後の試験へ、足している途中の一覧を残さない
        ProgressiveItems.FeedAllNow();
    });

    [Fact]
    public Task 見えていない一覧は_分けずに全部足す() => UiThread.Run(() =>
    {
        // 畳んだ欄の中。並べないので手間が掛からず、開いたときに1回で出る（前と同じ動き）
        using var host = new Host();
        host.List.Visibility = Visibility.Collapsed;
        host.Bind(Numbers(Rows));
        host.Layout();

        Assert.Equal(Rows, host.List.Items.Count);
        Assert.Equal(0, ProgressiveItems.PendingCount(host.List));
    });

    [Fact]
    public Task 見える範囲に収まる一覧は_最初の配置で全部足す() => UiThread.Run(() =>
    {
        // 説明の短い商品。後から足す物が無いので、開く時間も出る絵も前と変わらない
        using var host = new Host();
        host.Bind(Numbers(3));
        host.Layout();

        Assert.Equal(3, host.List.Items.Count);
        Assert.Equal(0, ProgressiveItems.PendingCount(host.List));
    });

    [Fact]
    public Task 途中で元が替わったら_前の分は足さない() => UiThread.Run(async () =>
    {
        // フォルダビューで、足している途中に別の商品を選んだとき。前の商品の見出しが混ざらない
        using var host = new Host();
        host.Bind(Numbers(Rows));
        host.Layout();

        var next = Enumerable.Range(1000, 12).Cast<object>().ToList();
        host.Bind(next);
        host.Layout();
        await Idle();

        Assert.Equal(next, host.List.Items.Cast<object>());
    });

    [Fact]
    public Task 流した位置が残る画面で元が替わったら_分けずに全部足す() => UiThread.Run(async () =>
    {
        // 主の窓で商品から商品へ移ったとき。一覧を短く始めると、流せる長さが足りずに位置が手前へ詰められる
        using var host = new Host();
        host.Bind(Numbers(Rows));
        host.Layout();
        await Idle();
        host.Scroller.ScrollToVerticalOffset(1200);
        host.Layout();
        Assert.Equal(1200, host.Scroller.VerticalOffset);

        host.Bind(Enumerable.Range(1000, Rows).Cast<object>().ToList());

        Assert.Equal(Rows, host.List.Items.Count);
        host.Layout();
        Assert.Equal(1200, host.Scroller.VerticalOffset);
    });

    [Fact]
    public Task 先頭へ戻ると印の付いた画面では_流した位置が残っていても分けて足す() => UiThread.Run(async () =>
    {
        // フォルダビューで商品を選び直したとき。先頭へ戻す命令は次の配置まで効かないので、印で伝える
        using var host = new Host();
        host.Bind(Numbers(Rows));
        host.Layout();
        await Idle();
        host.Scroller.ScrollToVerticalOffset(1200);
        host.Layout();

        host.Scroller.ScrollToHome();
        ProgressiveItems.SetReturnsToTop(host.Scroller, true);
        host.Bind(Enumerable.Range(1000, Rows).Cast<object>().ToList());
        host.Layout();

        Assert.Equal(0, host.Scroller.VerticalOffset);
        // 前の位置（1200）の下まで作らない。先頭から見える分だけ
        Assert.Equal(FirstRows, host.List.Items.Count);

        await Idle();
        Assert.Equal(Rows, host.List.Items.Count);
        Assert.Equal(0, host.Scroller.VerticalOffset);
    });

    [Fact]
    public Task 後から足している間_見えている行は動かない() => UiThread.Run(async () =>
    {
        using var host = new Host();
        host.Bind(Numbers(Rows));
        host.Layout();

        var before = host.RowTops();
        await Idle();
        var after = host.RowTops();

        // 最初の配置で出た行の位置は、全部足した後も同じ（足すのは末尾だけ）
        Assert.Equal(before, after.Take(before.Count));
    });

    private static List<object> Numbers(int count) => Enumerable.Range(0, count).Cast<object>().ToList();

    private static async Task Idle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

    /// <summary>流せる画面の中に、高さの決まった物と一覧を縦に置いた台。出さない窓口に載せる。</summary>
    private sealed class Host : IDisposable
    {
        public const double ViewportHeight = 200;
        public const double HeaderHeight = 100;

        private readonly HwndSource _source;

        public Host()
        {
            var row = new FrameworkElementFactory(typeof(Border));
            row.SetValue(FrameworkElement.HeightProperty, (double)RowHeight);
            List = new ItemsControl { ItemTemplate = new DataTemplate { VisualTree = row } };

            var stack = new StackPanel();
            stack.Children.Add(new Border { Height = HeaderHeight });
            stack.Children.Add(List);

            Scroller = new ScrollViewer
            {
                Width = 300,
                Height = ViewportHeight,
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                Content = stack,
            };

            // Canvas は子を欲しがるだけの大きさで並べる（窓口の大きさ 1×1 に関係なく、決めた大きさで並ぶ）
            Root = new Canvas();
            Root.Children.Add(Scroller);

            // 親がメッセージ専用（HWND_MESSAGE）の窓口は、画面にもタスクバーにも出ない
            _source = new HwndSource(new HwndSourceParameters("ProgressiveItemsTests")
            {
                WindowStyle = 0,
                ParentWindow = new IntPtr(-3),
                Width = 1,
                Height = 1,
            })
            {
                RootVisual = Root,
            };
        }

        public Canvas Root { get; }

        public ScrollViewer Scroller { get; }

        public ItemsControl List { get; }

        public void Bind(List<object> source) => ProgressiveItems.SetSource(List, source);

        public void Layout() => Root.UpdateLayout();

        /// <summary>行の部品が作られた数（足しただけで並べていない行は数えない）。</summary>
        public int RealizedRows => Enumerable.Range(0, List.Items.Count)
            .Count(index => List.ItemContainerGenerator.ContainerFromIndex(index) is not null);

        /// <summary>作られている行の、流せる画面の中での上端。</summary>
        public List<double> RowTops() => Enumerable.Range(0, List.Items.Count)
            .Select(index => List.ItemContainerGenerator.ContainerFromIndex(index))
            .OfType<FrameworkElement>()
            .Select(container => container.TranslatePoint(new Point(0, 0), Scroller).Y)
            .ToList();

        public void Dispose() => _source.Dispose();
    }
}
