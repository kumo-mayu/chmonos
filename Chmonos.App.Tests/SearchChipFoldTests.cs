using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索の左の条件で積んだ札は、2行を超えたら2行で切り、2行目の右に「ほか n 件」、広げた後は「折りたたむ」
/// （ユーザ判断 2026-11-20「チップのままで、折り畳めるようにしよう。一覧できることも大切だ。」・<see cref="ChipWrapPanel"/>・<see cref="ChipFold"/>）。
/// 隠す数は並べた幅で決まるので、幅を決め打った札で確かめる（字の幅に頼ると環境で数が変わる）
/// </summary>
public class SearchChipFoldTests
{
    private const double Reserve = 72;

    // 幅50の札は幅200の欄に1行4枚。2行目は右に72を空けるので2枚まで
    private static Size[] Chips(int count) => Enumerable.Repeat(new Size(50, 20), count).ToArray();

    [Fact]
    public void 二行に収まる間は_全部を並べてボタンを出さない()
    {
        var layout = ChipWrapPanel.Lay(Chips(8), 200, 2, Reserve, expanded: false);

        Assert.False(layout.Overflows);
        Assert.Equal(0, layout.Hidden);
        Assert.All(layout.Places, place => Assert.NotNull(place));
        Assert.Equal(40, layout.Size.Height);
    }

    [Fact]
    public void 二行を超えると_二行目の右を空けて_入らない札から後を隠す()
    {
        var layout = ChipWrapPanel.Lay(Chips(10), 200, 2, Reserve, expanded: false);

        Assert.True(layout.Overflows);
        Assert.Equal(4, layout.Hidden); // 1行目4枚・2行目2枚で6枚が見え、残り4枚
        Assert.Equal(6, layout.Places.Count(place => place is not null));
        Assert.All(layout.Places.Skip(6), place => Assert.Null(place)); // 並びの順を崩さない
        Assert.Equal(40, layout.Size.Height);
        Assert.Equal(200, layout.Size.Width); // 右端にボタンを重ねるので幅いっぱい
    }

    [Fact]
    public void 長い札が二行目に入らないときは_二行目を空けて隠す()
    {
        Size[] sizes = [new(190, 20), new(190, 20), new(50, 20)];

        var layout = ChipWrapPanel.Lay(sizes, 200, 2, Reserve, expanded: false);

        Assert.Equal(2, layout.Hidden);
        Assert.Equal(40, layout.Size.Height); // ボタンを置く2行目の高さは取る
    }

    [Fact]
    public void 広げると全部を並べ_折りたたむのボタンが残る()
    {
        var layout = ChipWrapPanel.Lay(Chips(10), 200, 2, Reserve, expanded: true);

        Assert.True(layout.Overflows);
        Assert.Equal(0, layout.Hidden);
        Assert.All(layout.Places, place => Assert.NotNull(place));
        Assert.Equal(60, layout.Size.Height); // 3行目は札2枚で、右にボタンが入る
    }

    [Fact]
    public void 広げて最後の行にボタンの空きが無ければ_一行足す()
    {
        var layout = ChipWrapPanel.Lay(Chips(12), 200, 2, Reserve, expanded: true);

        Assert.Equal(80, layout.Size.Height);
    }

    [Theory]
    [InlineData(false, 4, "ほか 4 件")]
    [InlineData(true, 0, "折りたたむ")]
    public void ボタンの文言(bool expanded, int hidden, string expected)
        => Assert.Equal(expected, ChipFold.TextOf(expanded, hidden));

    [Fact]
    public Task 並べると_隠した数と溢れが状態へ書かれ_広げた後も折りたたむが残る() => UiThread.Run(() =>
    {
        using var host = new Host(10);

        Assert.True(host.Fold.Overflows);
        Assert.Equal(4, host.Fold.HiddenCount);
        Assert.Equal("ほか 4 件", host.Fold.ToggleText);

        host.Fold.ToggleCommand.Execute(null);
        host.Layout();

        Assert.True(host.Fold.IsExpanded);
        Assert.True(host.Fold.Overflows);
        Assert.Equal(0, host.Fold.HiddenCount);
        Assert.Equal("折りたたむ", host.Fold.ToggleText);
    });

    [Fact]
    public Task 隠した札とその中の部品は押せず_矢印の並びにも入らない() => UiThread.Run(() =>
    {
        using var host = new Host(10);

        var members = ArrowGroup.MembersOf(host.List);
        Assert.Equal(12, members.Count); // 見える札6枚と、それぞれの ×
        Assert.All(Enumerable.Range(6, 4), index => Assert.False(host.Container(index).IsEnabled));
        Assert.All(members, member => Assert.True(member.IsEnabled));
        Assert.Single(members, KeyboardNavigation.GetIsTabStop);
    });

    [Fact]
    public Task 広げて奥の札に止まった後に畳んでも_Tabで止まるのは見えている札() => UiThread.Run(async () =>
    {
        using var host = new Host(10);
        host.Fold.IsExpanded = true;
        host.Layout();

        // 載せた時・広げた時に頼まれた合わせ直しを先に済ませる（残っていると、畳んだ後の確かめに混ざる）
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        var last = host.Chip(9);
        last.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, last) { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
        Assert.True(KeyboardNavigation.GetIsTabStop(last));

        // 並びの合わせ直しは呼ばず、パネルが配置の後に頼むのを待つ（頼まないと、覚えた止まり先が押せない札のまま残り、Tab が並びを飛ばす）
        host.Fold.IsExpanded = false;
        host.LayoutOnly();
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);

        var stop = Assert.Single(ArrowGroup.MembersOf(host.List), KeyboardNavigation.GetIsTabStop);
        Assert.True(stop.IsEnabled);
        Assert.False(last.IsEnabled);
    });

    [Fact]
    public Task 人が足して札が隠れるときは広げ_隠れないときは畳んだまま() => UiThread.Run(() =>
    {
        using var host = new Host(8);
        Assert.False(host.Fold.Overflows);

        // 2行に収まる間に足した：広げる物が無いので畳んだまま（後で溢れても、足すまでは畳む）
        host.Fold.RevealAdded();
        host.Layout();
        Assert.False(host.Fold.IsExpanded);
        Assert.False(host.Fold.RevealRequested);

        // 足した札が「ほか n 件」に入る：広げて見せる
        host.Fold.Chips.Add(new ListChip("x", "x"));
        host.Fold.RevealAdded();
        host.Layout();
        Assert.True(host.Fold.IsExpanded);
        Assert.Equal(0, host.Fold.HiddenCount);
    });

    [Fact]
    public Task 状態を戻すときは足しても広げない() => UiThread.Run(() =>
    {
        var module = new ListModule(SearchModuleKind.Shop, allowsAnd: false, "ショップで絞り込む", "", (_, _, _, _) => true);
        for (var i = 0; i < 20; i++)
        {
            module.AddKey($"shop{i}");
        }

        var state = module.Save();
        module.Clear();
        module.Load(state);

        Assert.Equal(20, module.Chips.Count);
        Assert.False(module.Fold.RevealRequested);
        Assert.False(module.Fold.IsExpanded);
    });

    [Fact]
    public void 候補から足すと広げる印が付き_空にすると畳んだ姿へ戻る()
    {
        var module = new ListModule(SearchModuleKind.Shop, allowsAnd: false, "ショップで絞り込む", "", (_, _, _, _) => true);
        module.SetSuggestions([("作り物のショップ", "shop1")]);

        module.AddCommand.Execute("作り物のショップ");
        Assert.True(module.Fold.RevealRequested);

        module.Fold.IsExpanded = true;
        module.Clear();
        Assert.False(module.Fold.IsExpanded);
        Assert.False(module.Fold.RevealRequested);
    }

    [Fact]
    public void ユーザータグの小分類も_足すと広げる印が付く()
    {
        var tags = new UserTagModule();
        tags.SetMasters([("衣装", (IReadOnlyList<string>)["上着", "靴"])]);
        var top = tags.AddTop("衣装")!;

        top.AddCommand.Execute(UserTagTopRow.NoSubText);
        Assert.False(top.Fold.RevealRequested); // 「小分類なし」は先頭に入り、いつも見える

        top.AddCommand.Execute("上着");
        Assert.True(top.Fold.RevealRequested);
    }

    [Fact]
    public Task ほかn件のボタンは_矢印の並びに入らず_Tabで止まる() => UiThread.Run(() =>
    {
        // 札の並びの中で Tab が止まるのは1つだけなので、ボタンが並びに入ると、隠した札を出す手段にキーボードで届かない
        using var host = new Host(10);

        Assert.DoesNotContain(host.Toggle, ArrowGroup.MembersOf(host.List));
        Assert.True(host.Toggle.IsEnabled);
        Assert.True(host.Toggle.Focusable);
        Assert.True(KeyboardNavigation.GetIsTabStop(host.Toggle));
    });

    /// <summary>幅200の札の並び（札は幅50・中に ×）を出さない窓口に載せた台。並べ方は検索の画面の FoldingChipList と同じ。</summary>
    private sealed class Host : IDisposable
    {
        private readonly HwndSource _source;
        private readonly Canvas _root = new();

        public Host(int count)
        {
            Fold = new ChipFold(new System.Collections.ObjectModel.ObservableCollection<ListChip>(
                Enumerable.Range(0, count).Select(index => new ListChip($"k{index}", $"札{index}"))));

            var chip = new FrameworkElementFactory(typeof(Button));
            chip.SetValue(FrameworkElement.WidthProperty, 36.0);
            chip.SetValue(FrameworkElement.HeightProperty, 20.0);
            var remove = new FrameworkElementFactory(typeof(Button));
            remove.SetValue(FrameworkElement.WidthProperty, 14.0);
            var row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            row.AppendChild(chip);
            row.AppendChild(remove);

            var panel = new FrameworkElementFactory(typeof(ChipWrapPanel));
            panel.SetBinding(ChipWrapPanel.IsExpandedProperty, new Binding(nameof(ChipFold.IsExpanded)));
            panel.SetBinding(ChipWrapPanel.HiddenCountProperty, new Binding(nameof(ChipFold.HiddenCount)));
            panel.SetBinding(ChipWrapPanel.OverflowsProperty, new Binding(nameof(ChipFold.Overflows)));
            panel.SetBinding(ChipWrapPanel.RevealRequestedProperty, new Binding(nameof(ChipFold.RevealRequested)));
            List = new ContentItemsControl
            {
                Width = 200,
                DataContext = Fold,
                ItemTemplate = new DataTemplate { VisualTree = row },
                ItemsPanel = new ItemsPanelTemplate(panel),
                Template = new ControlTemplate(typeof(ContentItemsControl)) { VisualTree = FoldTemplate() },
            };
            List.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(ChipFold.Chips)));
            ArrowGroup.SetIsEnabled(List, true);
            _root.Children.Add(List);
            _source = new HwndSource(new HwndSourceParameters("SearchChipFoldTests")
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

        public ChipFold Fold { get; }

        public ContentItemsControl List { get; }

        /// <summary>検索の画面の FoldingChipList と同じ形：並びの上に「ほか n 件」のボタンを重ねる。</summary>
        private static FrameworkElementFactory FoldTemplate()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            var toggle = new FrameworkElementFactory(typeof(Button), "Toggle");
            toggle.SetValue(FrameworkElement.WidthProperty, 50.0);
            toggle.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            toggle.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom);
            grid.AppendChild(toggle);
            return grid;
        }

        public Button Toggle => (Button)List.Template.FindName("Toggle", List);

        public UIElement Container(int index) => (UIElement)List.ItemContainerGenerator.ContainerFromIndex(index);

        /// <summary>札の中の、名前の側のボタン（並びの止まり先）。</summary>
        public Button Chip(int index)
            => (Button)((StackPanel)System.Windows.Media.VisualTreeHelper.GetChild(Container(index), 0)).Children[0];

        /// <summary>並べ直し、矢印の並びの止まり先を合わせる（アプリでは配置の後に自分で合わせる）。</summary>
        public void Layout()
        {
            LayoutOnly();
            ArrowGroup.RefreshNow(List);
        }

        public void LayoutOnly()
        {
            _root.UpdateLayout();
            _root.UpdateLayout();
        }

        public void Dispose() => _source.Dispose();
    }
}
