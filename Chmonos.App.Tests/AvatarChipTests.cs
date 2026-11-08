using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;

namespace Chmonos.App.Tests;

/// <summary>
/// 対応アバターの札（<see cref="AvatarChip"/>）は、普段は自分で描き、乗せたとき・止まったときに中身を作る。
/// 描いた札と作った札の大きさが違うと、乗せた瞬間に札が伸び縮みして並びが組み変わるので、大きさが同じことを確かめる。
/// </summary>
public class AvatarChipTests
{
    private static readonly string[] Names = ["作り物A", "とても長い作り物のアバターの名前", "Fake Avatar 2"];

    [Fact]
    public Task 描いた札は_作った札と同じ大きさ() => UiThread.Run(async () =>
    {
        var panel = new ItemAvatarsPanel();
        var content = (DataTemplate)panel.Resources["AvatarChipContent"];
        foreach (var unconfirmed in new[] { false, true })
        {
            foreach (var name in Names)
            {
                var row = Row(name, unconfirmed);
                var chip = Chip(content, row);
                var made = new Border
                {
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(9, 3, 9, 3),
                    DataContext = row,
                    Child = (UIElement)content.LoadContent(),
                };
                var host = new Grid();
                host.Children.Add(chip);
                host.Children.Add(made);
                chip.HorizontalAlignment = HorizontalAlignment.Left;
                made.HorizontalAlignment = HorizontalAlignment.Left;
                await BindingsApplied();
                host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                Assert.False(chip.IsRealized);
                Assert.Equal(made.DesiredSize.Width, chip.DesiredSize.Width, 2);
                Assert.Equal(made.DesiredSize.Height, chip.DesiredSize.Height, 2);
            }
        }
    });

    [Fact]
    public Task 中身を作っても_札の大きさは変わらない() => UiThread.Run(async () =>
    {
        var panel = new ItemAvatarsPanel();
        var chip = Chip((DataTemplate)panel.Resources["AvatarChipContent"], Row("作り物A", unconfirmed: true));
        var host = new Grid();
        host.Children.Add(chip);
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var before = chip.DesiredSize;

        chip.Realize();
        await BindingsApplied();
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.True(chip.IsRealized);
        Assert.NotNull(chip.Child);
        Assert.Equal(before.Width, chip.DesiredSize.Width, 2);
        Assert.Equal(before.Height, chip.DesiredSize.Height, 2);
    });

    [Fact]
    public Task 描いただけの札も_名前付きの押せるボタンとして読める() => UiThread.Run(async () =>
    {
        var opened = 0;
        var panel = new ItemAvatarsPanel();
        var row = Row("作り物A", unconfirmed: false, open: () => opened++);
        var chip = Chip((DataTemplate)panel.Resources["AvatarChipContent"], row);

        var peer = UIElementAutomationPeer.CreatePeerForElement(chip);

        Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
        Assert.Equal("作り物Aを開く", peer.GetName());
        Assert.False(chip.IsRealized);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        await UiThread.Until(() => opened == 1, "押すと開く");
        Assert.Equal(1, opened);
    });

    [Fact]
    public Task 札の並びは_描いた札を1枚ずつ数え_作った札の確認と外すもTabで止まらない物になる() => UiThread.Run(async () =>
    {
        var panel = new ItemAvatarsPanel();
        var chip = new FrameworkElementFactory(typeof(AvatarChip));
        chip.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        chip.SetValue(Border.PaddingProperty, new Thickness(9, 3, 9, 3));
        chip.SetValue(UIElement.FocusableProperty, true);
        chip.SetValue(AvatarChip.ContentTemplateProperty, panel.Resources["AvatarChipContent"]);
        var list = new ItemsControl
        {
            Width = 400,
            ItemTemplate = new DataTemplate { VisualTree = chip },
            ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel))),
            ItemsSource = new[] { Row("作り物A", true), Row("作り物B", true), Row("作り物C", true) },
        };
        ArrowGroup.SetIsEnabled(list, true);
        var root = new Canvas();
        root.Children.Add(list);
        using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("AvatarChipTests")
        {
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3),
            Width = 1,
            Height = 1,
        })
        { RootVisual = root };
        root.UpdateLayout();
        ArrowGroup.RefreshNow(list);

        // 描いただけの札は中に部品が無い：札3枚だけが止まり先
        var members = ArrowGroup.MembersOf(list);
        Assert.Equal(3, members.Count);
        Assert.All(members, member => Assert.IsType<AvatarChip>(member));
        Assert.Single(members, KeyboardNavigation.GetIsTabStop);

        // 乗せた・止まった札は中身を作る。✓ と × が並びに入り、Tab で止まるのは今までどおり1つだけ
        var second = (AvatarChip)members[1];
        second.Realize();
        await BindingsApplied();
        root.UpdateLayout();
        members = ArrowGroup.MembersOf(list);
        Assert.Equal(5, members.Count);
        Assert.Single(members, KeyboardNavigation.GetIsTabStop);
        Assert.True(KeyboardNavigation.GetIsTabStop(members[0]));
    });

    /// <summary>テンプレートから作った部品の結び付けは、画面のスレッドが次に空いたときに値が入る（アプリでは配置より先に済む）。</summary>
    private static async Task BindingsApplied()
        => await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);

    private static AvatarRow Row(string name, bool unconfirmed, Action? open = null) => new()
    {
        ItemId = "90000001",
        Name = name,
        SourceText = "説明文",
        IsUnconfirmed = unconfirmed,
        OpenCommand = new RelayCommand(_ => open?.Invoke()),
    };

    private static AvatarChip Chip(DataTemplate content, AvatarRow row) => new()
    {
        BorderThickness = new Thickness(1),
        Padding = new Thickness(9, 3, 9, 3),
        ContentTemplate = content,
        DataContext = row,
    };
}
