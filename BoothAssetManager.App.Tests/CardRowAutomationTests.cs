using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Markup;
using BoothAssetManager.App.Controls;
using BoothAssetManager.App.Tests.Support;

namespace BoothAssetManager.App.Tests;

/// <summary>
/// カードの一覧の「段」が、読み上げ・自動操作（UI Automation）の木に出ないこと（ユーザ判断 2026-09-30）。
/// 段は窓の幅で決まる並べ方の都合の入れ物で、前は型の名前（行の ToString()）で読まれていた。
/// ここは窓口（AutomationPeer）の木を直に見る。相手の側から見た木と「押す」が届くことは <c>experiments/PeerProbe</c> で確かめる。
/// </summary>
public class CardRowAutomationTests
{
    private const string RowTemplate =
        """
        <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                      xmlns:controls='clr-namespace:BoothAssetManager.App.Controls;assembly=BoothAssetManager.App'>
            <controls:CardRowItems ItemsSource='{Binding}'>
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <StackPanel Orientation='Horizontal' />
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Button Content='{Binding}' Width='60' Height='40' />
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </controls:CardRowItems>
        </DataTemplate>
        """;

    private static readonly string[][] Rows = [["カード1", "カード2", "カード3"], ["カード4", "カード5"]];

    [Fact]
    public Task 段の部品は_操作できる部品として出ず_Tabでも止まらない() => UiThread.Run(() =>
    {
        var row = new CardRowItems { ItemsSource = Rows[0] };

        // 窓口を返さないと、WPF は既定の窓口（行を型の名前で出す ItemsControl の物）を代わりに作る。自前の窓口で印だけ外す
        var peer = UIElementAutomationPeer.CreatePeerForElement(row);

        Assert.NotNull(peer);
        Assert.False(peer.IsControlElement());
        Assert.False(peer.IsContentElement());
        Assert.Equal("", peer.GetName());
        Assert.False(row.Focusable);
    });

    [Fact]
    public Task カードの一覧は_段を挟まずに_カードを直下に並びの順で出す() => UiThread.Run(() =>
    {
        var list = new CardRowsListBox { ItemsSource = Rows, ItemTemplate = (DataTemplate)XamlReader.Parse(RowTemplate) };
        Arrange(list);

        var peer = UIElementAutomationPeer.CreatePeerForElement(list);
        var children = peer!.GetChildren();

        Assert.Equal(AutomationControlType.List, peer.GetAutomationControlType());
        Assert.Equal(["カード1", "カード2", "カード3", "カード4", "カード5"], children.Select(child => child.GetName()));
        Assert.All(children, child => Assert.Equal(AutomationControlType.Button, child.GetAutomationControlType()));
    });

    [Fact]
    public Task カードの一覧は_流す操作を渡し_選ぶ操作は渡さない() => UiThread.Run(() =>
    {
        var list = new CardRowsListBox { ItemsSource = Rows, ItemTemplate = (DataTemplate)XamlReader.Parse(RowTemplate) };
        Arrange(list);

        var peer = UIElementAutomationPeer.CreatePeerForElement(list)!;

        Assert.NotNull(peer.GetPattern(PatternInterface.Scroll));

        // 段は選べない（行は止まれず、見た目も持たない）。選ぶ物が無い一覧に「選ぶ」を持たせない
        Assert.Null(peer.GetPattern(PatternInterface.Selection));
    });

    [Fact]
    public Task 管理の画面の形では_段は操作できる部品でない入れ物として残り_中の部品はその下に出る() => UiThread.Run(() =>
    {
        // タグ・属性の管理は、行ごとに型の違う一覧（ContentItemsControl）の中に段を並べる。
        // こちらの一覧は WPF の既定の集め方なので、段の窓口は木に残る。相手の既定の見方（操作できる部品だけ）では飛ばされる
        var list = new ContentItemsControl { ItemsSource = Rows, ItemTemplate = (DataTemplate)XamlReader.Parse(RowTemplate) };
        Arrange(list);

        var rows = UIElementAutomationPeer.CreatePeerForElement(list)!.GetChildren();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.False(row.IsControlElement()));
        Assert.Equal(
            ["カード1", "カード2", "カード3", "カード4", "カード5"],
            rows.SelectMany(row => row.GetChildren()).Select(child => child.GetName()));
    });

    private static void Arrange(FrameworkElement element)
    {
        // 親の無い部品は、初期化の終わりを告げないと既定の型（テーマの見た目）が当たらず、中身が1つも作られない
        element.BeginInit();
        element.EndInit();
        element.Measure(new Size(400, 300));
        element.Arrange(new Rect(0, 0, 400, 300));
        element.UpdateLayout();
    }
}
