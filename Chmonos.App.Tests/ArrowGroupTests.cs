using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// 並び（札の並び・行の一覧）は Tab で1回だけ入り、中は矢印で移る（ユーザ判断 2026-10-01。<see cref="ArrowGroup"/>）。
///
/// 矢印の行き先・消えた行の後に止まる行は、場所と番号から計算で決まるので <see cref="ArrowStep"/> を直に確かめる。
/// 「並びの中で Tab で止まる物が1つだけか」「どれがそれか」は、決め打ちの大きさの部品を出さない窓口に載せて確かめる。
/// フォーカスを実際に移す動き（Tab・矢印・解除の後）は窓がフォーカスを取れないと確かめられないので、experiments/PeerProbe の focus で見る
/// </summary>
public class ArrowGroupTests
{
    // 札の並び：1段目に札が2枚（それぞれ中に ×）、2段目に札が2枚。× は札の右寄りにある
    private static readonly ArrowSpot[] Chips =
    [
        new(new Rect(0, 0, 100, 20), IsInner: false),   // 0 札A
        new(new Rect(80, 3, 14, 14), IsInner: true),    // 1 Aの ×
        new(new Rect(110, 0, 100, 20), IsInner: false), // 2 札B
        new(new Rect(190, 3, 14, 14), IsInner: true),   // 3 Bの ×
        new(new Rect(0, 26, 100, 20), IsInner: false),  // 4 札C
        new(new Rect(80, 29, 14, 14), IsInner: true),   // 5 Cの ×
        new(new Rect(110, 26, 100, 20), IsInner: false), // 6 札D
        new(new Rect(190, 29, 14, 14), IsInner: true),  // 7 Dの ×
    ];

    [Fact]
    public void 左右は並びの順に1つずつ移り_札とそのバツを順に通る()
    {
        Assert.Equal(1, ArrowStep.Next(Chips, 0, ArrowMove.Next));
        Assert.Equal(2, ArrowStep.Next(Chips, 1, ArrowMove.Next));
        // 段の終わりから次の段の頭へ続く
        Assert.Equal(4, ArrowStep.Next(Chips, 3, ArrowMove.Next));
        Assert.Equal(3, ArrowStep.Next(Chips, 4, ArrowMove.Previous));
    }

    [Fact]
    public void 端では止まり_回り込まない()
    {
        Assert.Null(ArrowStep.Next(Chips, 0, ArrowMove.Previous));
        Assert.Null(ArrowStep.Next(Chips, 7, ArrowMove.Next));
        Assert.Null(ArrowStep.Next(Chips, 0, ArrowMove.Up));
        Assert.Null(ArrowStep.Next(Chips, 6, ArrowMove.Down));
    }

    [Fact]
    public void 上下は隣の段の_横の位置がいちばん近い札へ移り_バツへは降りない()
    {
        Assert.Equal(4, ArrowStep.Next(Chips, 0, ArrowMove.Down));
        Assert.Equal(6, ArrowStep.Next(Chips, 2, ArrowMove.Down));
        // × に止まっていても、行き先は札。Bの ×（右端寄り）の下は D
        Assert.Equal(6, ArrowStep.Next(Chips, 3, ArrowMove.Down));
        // Cの × の上は、横の位置が近い A
        Assert.Equal(0, ArrowStep.Next(Chips, 5, ArrowMove.Up));
    }

    [Fact]
    public void HomeとEndは並びの端へ移る()
    {
        Assert.Equal(0, ArrowStep.Next(Chips, 5, ArrowMove.First));
        Assert.Equal(7, ArrowStep.Next(Chips, 2, ArrowMove.Last));
        Assert.Null(ArrowStep.Next(Chips, 0, ArrowMove.First));
    }

    [Fact]
    public void 行を見える分だけ作る一覧では_上下とHomeEndは行の番号で移る()
    {
        Assert.Equal(4, ArrowStep.Row(3, 400, ArrowMove.Down));
        Assert.Equal(2, ArrowStep.Row(3, 400, ArrowMove.Up));
        Assert.Equal(0, ArrowStep.Row(3, 400, ArrowMove.First));
        Assert.Equal(399, ArrowStep.Row(3, 400, ArrowMove.Last));
        Assert.Null(ArrowStep.Row(0, 400, ArrowMove.Up));
        Assert.Null(ArrowStep.Row(399, 400, ArrowMove.Down));
    }

    // カードの段：1段に同じ役の物（カード）が何枚も並ぶ。3枚の段と、最後の2枚の段
    private static readonly ArrowSpot[] FullRow =
    [
        new(new Rect(0, 0, 100, 150), IsInner: false),
        new(new Rect(114, 0, 100, 150), IsInner: false),
        new(new Rect(228, 0, 100, 150), IsInner: false),
    ];

    private static readonly ArrowSpot[] ShortRow =
    [
        new(new Rect(0, 164, 100, 150), IsInner: false),
        new(new Rect(114, 164, 100, 150), IsInner: false),
    ];

    [Fact]
    public void カードの段の上下は_隣の段の横の位置がいちばん近いカードへ移る()
    {
        // 3列目（中心 278）から下の段へ：2枚しか無い段では、右端の2枚目
        Assert.Equal(1, ArrowStep.InRow(ShortRow, 278, ArrowMove.Down));
        // 2列目（中心 164）から上の段へ：同じ2列目
        Assert.Equal(1, ArrowStep.InRow(FullRow, 164, ArrowMove.Up));
        Assert.Equal(0, ArrowStep.InRow(FullRow, 50, ArrowMove.Up));
    }

    [Fact]
    public void カードの段のHomeとEndは_端の段の端のカードへ移る()
    {
        Assert.Equal(0, ArrowStep.InRow(FullRow, 278, ArrowMove.First));
        Assert.Equal(1, ArrowStep.InRow(ShortRow, 50, ArrowMove.Last));
        Assert.Null(ArrowStep.InRow([], 50, ArrowMove.Down));
    }

    [Fact]
    public Task カードの一覧は_並びになり_Tabは並びに任せる() => UiThread.Run(() =>
    {
        // 検索・ショップ・ショップ一覧・フォルダの右はこの一覧を使う。ListBox の既定（Once）のままだと、並びが選んだ止まり先ではなく一覧が覚えた物へ入る
        var list = new CardRowsListBox();

        Assert.True(ArrowGroup.GetIsEnabled(list));
        Assert.Equal(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(list));
    });

    [Fact]
    public Task 後からできた部品は_並びのTabで止まらない物になる() => UiThread.Run(() =>
    {
        // カードの中身は画面が空いたときに後から作る（DeferredCardHost）。行が作られたときの合わせ直しの後にできるので、そのままでは1枚ずつ Tab で止まる
        using var host = Host.Chips(["A", "B"]);
        var row = (StackPanel)System.Windows.Media.VisualTreeHelper.GetChild(
            (DependencyObject)host.List.ItemContainerGenerator.ContainerFromIndex(1), 0);
        var late = new Button { Width = 20 };
        row.Children.Add(late);
        host.LayoutOnly();
        Assert.True(KeyboardNavigation.GetIsTabStop(late));

        ArrowGroup.Adopt(late);

        Assert.False(KeyboardNavigation.GetIsTabStop(late));
        Assert.Single(ArrowGroup.MembersOf(host.List), KeyboardNavigation.GetIsTabStop);
    });

    [Fact]
    public void 止まっていた行が消えたら_次の行_最後の行なら前の行に止まる()
    {
        // 5行の3行目（番号2）を解除 → 残り4行。番号2には次の行が詰めて来ている
        Assert.Equal(2, ArrowStep.AfterRemoval(2, 4));
        // 最後の行（番号4）を解除 → 残り4行。前の行（番号3）
        Assert.Equal(3, ArrowStep.AfterRemoval(4, 4));
        // 最後の1行を解除 → 止まる行が無い
        Assert.Null(ArrowStep.AfterRemoval(0, 0));
    }

    [Fact]
    public Task 札の並びでTabで止まるのは_先頭の札の1つだけ() => UiThread.Run(() =>
    {
        using var host = Host.Chips(["A", "B", "C"]);

        var members = ArrowGroup.MembersOf(host.List);
        Assert.Equal(6, members.Count); // 札3枚と、それぞれの ×
        Assert.Equal([members[0]], members.Where(KeyboardNavigation.GetIsTabStop));
    });

    [Fact]
    public Task 止まる札が隠れたら_見えている先頭の札がTabで止まる物になる() => UiThread.Run(() =>
    {
        using var host = Host.Chips(["A", "B", "C"]);

        // アバター名で絞ると、合わない札は捨てずに隠す（ChipStrip）
        ((UIElement)host.List.ItemContainerGenerator.ContainerFromIndex(0)).Visibility = Visibility.Collapsed;
        host.Layout();

        var members = ArrowGroup.MembersOf(host.List);
        Assert.Equal(4, members.Count);
        Assert.Equal([members[0]], members.Where(KeyboardNavigation.GetIsTabStop));
        Assert.Equal("B", ((FrameworkElement)members[0]).DataContext);
    });

    [Fact]
    public Task 並びとは別に止まる所と入力欄は_並びに数えず_今までどおりTabで止まる() => UiThread.Run(() =>
    {
        using var host = Host.Chips(["A", "B"], withOutside: true);

        var members = ArrowGroup.MembersOf(host.List);
        Assert.Equal(4, members.Count);
        Assert.All(host.Outside(), element => Assert.True(KeyboardNavigation.GetIsTabStop(element)));
    });

    [Fact]
    public Task 見える分だけ作る一覧では_Tabで入ると見えている先頭の行に止まる() => UiThread.Run(() =>
    {
        using var host = Host.Rows(rows: 200);
        Assert.Equal("0", ((FrameworkElement)ArrowGroup.MembersOf(host.List).Single(KeyboardNavigation.GetIsTabStop)).DataContext);

        // 流してから（マウスのホイールで）入り直すと、上へ戻されずに見えている先頭の行
        host.Scroll!.ScrollToVerticalOffset(Host.RowHeight * 50);
        host.Layout();

        var stop = (FrameworkElement)ArrowGroup.MembersOf(host.List).Single(KeyboardNavigation.GetIsTabStop);
        Assert.Equal("50", stop.DataContext);
        Assert.True(ArrowGroup.MembersOf(host.List).Count < 200);
    });

    /// <summary>決め打ちの大きさの一覧を出さない窓口（親がメッセージ専用）に載せた台。</summary>
    private sealed class Host : IDisposable
    {
        public const double RowHeight = 20;

        private readonly HwndSource _source;
        private readonly Canvas _root = new();

        private Host(ItemsControl list)
        {
            List = list;
            ArrowGroup.SetIsEnabled(list, true);
            _root.Children.Add(list);
            _source = new HwndSource(new HwndSourceParameters("ArrowGroupTests")
            {
                WindowStyle = 0,
                ParentWindow = new IntPtr(-3),
                Width = 1,
                Height = 1,
            })
            {
                RootVisual = _root,
            };
            Layout();
        }

        public ItemsControl List { get; }

        public ScrollViewer? Scroll { get; private init; }

        /// <summary>札（押せる枠）と、その中の ×。札は横に折り返して並ぶ。</summary>
        public static Host Chips(IReadOnlyList<string> names, bool withOutside = false)
        {
            var chip = new FrameworkElementFactory(typeof(Button));
            chip.SetValue(FrameworkElement.WidthProperty, 100.0);
            chip.SetValue(FrameworkElement.HeightProperty, RowHeight);
            var reject = new FrameworkElementFactory(typeof(Button));
            reject.SetValue(FrameworkElement.WidthProperty, 14.0);
            var row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            row.AppendChild(chip);
            row.AppendChild(reject);
            if (withOutside)
            {
                // 対応アバターの「＋ 追加」とその入力欄の形
                var outside = new FrameworkElementFactory(typeof(Button));
                outside.SetValue(ArrowGroup.IsOutsideProperty, true);
                row.AppendChild(outside);
                row.AppendChild(new FrameworkElementFactory(typeof(TextBox)));
            }

            var panel = new FrameworkElementFactory(typeof(WrapPanel));
            var list = new ItemsControl
            {
                Width = 300,
                ItemTemplate = new DataTemplate { VisualTree = row },
                ItemsPanel = new ItemsPanelTemplate(panel),
                ItemsSource = new ObservableCollection<string>(names),
            };
            return new Host(list);
        }

        /// <summary>1行に1つボタンがある、見える分だけ作る一覧（設定の除外したファイルの形）。</summary>
        public static Host Rows(int rows)
        {
            var button = new FrameworkElementFactory(typeof(Button));
            button.SetValue(FrameworkElement.HeightProperty, RowHeight);
            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
            scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            var list = new ItemsControl
            {
                Width = 300,
                Height = RowHeight * 10,
                ItemTemplate = new DataTemplate { VisualTree = button },
                ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel))),
                Template = new ControlTemplate(typeof(ItemsControl)) { VisualTree = scroll },
                ItemsSource = Enumerable.Range(0, rows).Select(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList(),
            };
            VirtualizingPanel.SetIsVirtualizing(list, true);
            VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Pixel);
            var host = new Host(list) { Scroll = ContentItemsControl.FindScrollViewer(list) };
            return host;
        }

        public void Layout()
        {
            _root.UpdateLayout();
            ArrowGroup.RefreshNow(List);
        }

        /// <summary>並べ直すだけで、並びの合わせ直しはしない（後からできた部品を、行が作られた後に足した形にする）。</summary>
        public void LayoutOnly() => _root.UpdateLayout();

        /// <summary>並びとは別に止まる所（IsOutside の付いた物と入力欄）。</summary>
        public List<UIElement> Outside()
        {
            var found = new List<UIElement>();
            Visit(List);
            return found;

            void Visit(DependencyObject node)
            {
                for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
                    if (child is UIElement element && (ArrowGroup.GetIsOutside(element) || element is TextBox))
                    {
                        found.Add(element);
                        continue;
                    }

                    Visit(child);
                }
            }
        }

        public void Dispose() => _source.Dispose();
    }
}
