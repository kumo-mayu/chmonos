using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Chmonos.App.Controls;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 読み上げ・自動操作に渡る名前（UI Automation）。部品の窓口（AutomationPeer）を直に作って、渡る名前を確かめる。
/// </summary>
public class AutomationNameTests
{
    private static string NameOf(UIElement element) => UIElementAutomationPeer.CreatePeerForElement(element).GetName();

    // ---- 「_」が消えない ----

    [Theory]
    [InlineData("file_000.pngを開く")]
    [InlineData("a_b_c.zipをこの商品から外す")]
    [InlineData("_先頭.zipを開く")]
    [InlineData("末尾_")]
    [InlineData("二重__の名前")]
    [InlineData("印の無い名前")]
    public Task 中身が文字のボタンに付けた名前は_下線が消えずに渡る(string name) => UiThread.Run(() =>
    {
        AutomationNames.Register();
        var button = new Button { Content = "開く ▾" };
        AutomationProperties.SetName(button, name);

        Assert.Equal(name, NameOf(button));
    });

    [Fact]
    public Task 名前を中身より先に付けても_読み込まれた時点で下線が消えない形になる() => UiThread.Run(() =>
    {
        AutomationNames.Register();
        var button = new Button();
        AutomationProperties.SetName(button, "file_000.pngを開く");
        button.Content = "開く ▾";

        // 画面に載ったときに来る知らせ。結び付けで中身が後から届く行と同じ順
        button.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

        Assert.Equal("file_000.pngを開く", NameOf(button));
    });

    [Fact]
    public Task 中身が文字でないボタンの名前は_そのまま渡る() => UiThread.Run(() =>
    {
        AutomationNames.Register();
        var button = new Button { Content = new TextBlock { Text = "絵" } };
        AutomationProperties.SetName(button, "file_000.pngを開く");

        Assert.Equal("file_000.pngを開く", NameOf(button));
    });

    [Fact]
    public Task チェックとメニューの項目に付けた名前も_下線が消えない() => UiThread.Run(() =>
    {
        AutomationNames.Register();
        var check = new CheckBox { Content = "選ぶ" };
        AutomationProperties.SetName(check, "my_file.zipを選ぶ");
        var item = new MenuItem { Header = "外す" };
        AutomationProperties.SetName(item, "my_tag を外す");

        Assert.Equal("my_file.zipを選ぶ", NameOf(check));
        Assert.Equal("my_tag を外す", NameOf(item));
    });

    [Fact]
    public Task 名前を付けていないボタンは_中身の文字が下線ごと名前になり_中身が替われば名前も替わる() => UiThread.Run(() =>
    {
        AutomationNames.Register();
        var button = new Button { Content = "my_folder を商品として登録" };

        Assert.Equal("my_folder を商品として登録", NameOf(button));

        // 一覧の行は使い回される。前の行の名前が残らないこと
        button.Content = "other_folder_2 を商品として登録";
        Assert.Equal("other_folder_2 を商品として登録", NameOf(button));

        button.Content = "下線の無い名前";
        Assert.Equal("下線の無い名前", NameOf(button));
    });

    // ---- 開閉の三角（ExpandToggle）----

    [Fact]
    public Task 開閉の三角は_押すと起きることを名前にし_開いているかを状態でも渡す() => UiThread.Run(() =>
    {
        var toggle = new ExpandToggle { IsChecked = false, Subject = "ローカルファイル" };
        var peer = UIElementAutomationPeer.CreatePeerForElement(toggle);
        var state = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));

        Assert.Equal(ExpandCollapseState.Collapsed, state.ExpandCollapseState);
        Assert.Equal("ローカルファイルを開く", peer.GetName());

        // 開いていても「開く」と読まれていた。状態で名前が替わる（商品ページのバリエーションの三角と同じ言い方）
        toggle.IsChecked = true;
        Assert.Equal(ExpandCollapseState.Expanded, state.ExpandCollapseState);
        Assert.Equal("ローカルファイルを折りたたむ", peer.GetName());

        // 行が使い回されて、開閉する物が替わったとき
        toggle.Subject = "作り物の小分類の商品";
        Assert.Equal("作り物の小分類の商品を折りたたむ", peer.GetName());

        // 「切り替える」も今までどおり持つ（確かめの道具が使っている）
        Assert.NotNull(peer.GetPattern(PatternInterface.Toggle));
    });

    [Theory]
    [InlineData(true, "商品説明を折りたたむ", "商品説明を開く")]
    [InlineData(false, "商品説明を開く", "商品説明を折りたたむ")]
    public Task 畳む欄の見出しの押す所は_欄に付けた名前から_今押すと起きることを名前にする(bool expanded, string now, string afterToggle) => UiThread.Run(() =>
    {
        // アプリの型（TriangleExpander・暗黙の Expander）と同じ結び方。欄の名前は Expander に付け、型の中の押す所が継ぐ。
        // 前は欄の名前がそのまま押す所の名前で、開いていても畳んでいても「商品説明」と読まれた
        var expander = NewExpander("AutomationProperties.Name='商品説明'");
        expander.IsExpanded = expanded;
        expander.ApplyTemplate();
        var toggle = (ExpandToggle)expander.Template.FindName("HeaderSite", expander);
        var peer = UIElementAutomationPeer.CreatePeerForElement(toggle);

        Assert.Equal(now, peer.GetName());

        expander.IsExpanded = !expanded;
        Assert.Equal(afterToggle, peer.GetName());

        // 欄そのものの名前は替えない（欄は「開閉」の状態を自分で持つ）
        Assert.Equal("商品説明", UIElementAutomationPeer.CreatePeerForElement(expander).GetName());
    });

    [Fact]
    public Task 名前を付けていない畳む欄の見出しの押す所は_名前を作らない() => UiThread.Run(() =>
    {
        // 見出しが文字だけの欄（「消したもの 3 件」など）は、見出しの文字がそのまま読まれる。空の欄の名前から「を開く」だけの名前を作らない
        var expander = NewExpander(string.Empty);
        expander.ApplyTemplate();
        var toggle = (ExpandToggle)expander.Template.FindName("HeaderSite", expander);

        Assert.Equal(string.Empty, AutomationProperties.GetName(toggle));
    });

    private static Expander NewExpander(string attributes) => (Expander)System.Windows.Markup.XamlReader.Parse(
        $$$"""
        <Expander xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                  xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                  xmlns:controls='clr-namespace:Chmonos.App.Controls;assembly=Chmonos'
                  {{{attributes}}}>
            <Expander.Template>
                <ControlTemplate TargetType='Expander'>
                    <controls:ExpandToggle x:Name='HeaderSite'
                        Subject='{Binding Path=(AutomationProperties.Name), RelativeSource={RelativeSource TemplatedParent}}'
                        IsChecked='{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}' />
                </ControlTemplate>
            </Expander.Template>
        </Expander>
        """);

    [Fact]
    public Task 開閉の三角を_開く_畳むで動かすと_結び付けた先の値も変わる() => UiThread.Run(() =>
    {
        var source = new ExpandState();
        var toggle = new ExpandToggle();
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(ExpandState.IsExpanded)) { Source = source, Mode = BindingMode.TwoWay });
        var state = (IExpandCollapseProvider)UIElementAutomationPeer.CreatePeerForElement(toggle).GetPattern(PatternInterface.ExpandCollapse);

        state.Expand();
        Assert.True(source.IsExpanded);

        // 開いている物をもう一度「開く」でも、畳まれない（押すと切り替わるボタンとの違い）
        state.Expand();
        Assert.True(source.IsExpanded);

        state.Collapse();
        Assert.False(source.IsExpanded);
    });

    [Fact]
    public Task 押せない三角は_開く操作を断る() => UiThread.Run(() =>
    {
        var toggle = new ExpandToggle { IsEnabled = false };
        var state = (IExpandCollapseProvider)UIElementAutomationPeer.CreatePeerForElement(toggle).GetPattern(PatternInterface.ExpandCollapse);

        Assert.Throws<ElementNotEnabledException>(state.Expand);
        Assert.NotEqual(true, toggle.IsChecked);
    });

    private sealed class ExpandState
    {
        public bool IsExpanded { get; set; }
    }

    // ---- 候補の行（InvokableListBox）----

    [Fact]
    public Task 候補の行は_押すで決まる知らせを上げる() => UiThread.Run(async () =>
    {
        var list = new InvokableListBox { ItemsSource = new[] { "候補A", "候補B" }, Width = 200, Height = 100 };
        var invoked = new List<object>();
        list.RowInvoked += invoked.Add;

        // 行の部品を作らせる（窓には載せない）
        list.Measure(new Size(200, 100));
        list.Arrange(new Rect(0, 0, 200, 100));
        list.UpdateLayout();

        var rows = UIElementAutomationPeer.CreatePeerForElement(list).GetChildren()
            .Where(peer => peer.GetAutomationControlType() == AutomationControlType.ListItem)
            .ToList();
        Assert.Equal(2, rows.Count);

        var press = Assert.IsAssignableFrom<IInvokeProvider>(rows[1].GetPattern(PatternInterface.Invoke));
        press.Invoke();

        // 呼び出しを返してから動かす（押した先が窓を出しても、呼んだ側を待たせない）
        Assert.Empty(invoked);
        await list.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.Equal("候補B", Assert.Single(invoked));

        // 「選ぶ」も今までどおり持つ
        Assert.NotNull(rows[0].GetPattern(PatternInterface.SelectionItem));
    });

    // ---- フォルダの木の開け閉めの印 ----

    [Fact]
    public void 開閉の名前は_商品ページのバリエーションの三角と同じ言い方()
    {
        Assert.Equal(ItemViewModel.ToggleName("バリエーション", expanded: true), ExpandToggle.NameFor("バリエーション", isExpanded: true));
        Assert.Equal(ItemViewModel.ToggleName("バリエーション", expanded: false), ExpandToggle.NameFor("バリエーション", isExpanded: false));
    }

    [Theory]
    [InlineData("作り物のフォルダ", false, "作り物のフォルダを開く")]
    [InlineData("作り物のフォルダ", true, "作り物のフォルダを折りたたむ")]
    public void フォルダの開け閉めの印は_押すと起きることを名前にする(string name, bool isExpanded, string expected)
    {
        Assert.Equal(expected, FolderViewRow.ToggleNameOf(name, isExpanded));
    }

    [Theory]
    [InlineData("a_b", true, "a__b")]
    [InlineData("a_b", false, "a_b")]
    [InlineData("ab", true, "ab")]
    public void 消される部品のときだけ_下線を重ねる(string name, bool losesMarker, string expected)
    {
        Assert.Equal(expected, AutomationNames.Escape(name, losesMarker));
    }
}
