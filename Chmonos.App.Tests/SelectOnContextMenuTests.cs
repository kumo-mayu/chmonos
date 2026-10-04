using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Xunit;

namespace Chmonos.App.Tests;

/// <summary>
/// タグ・属性の管理の左の一覧の右クリック（メモ35）：メニューを開く前にその行が選ばれること。
/// ListBox は右クリックでは選ばないので、選ばないと、メニューの項目（右の欄と同じ命令）が別の行に効く。
/// </summary>
public class SelectOnContextMenuTests
{
    private static void RaiseOpening(FrameworkElement target)
    {
        var args = (ContextMenuEventArgs)typeof(ContextMenuEventArgs)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(ctor => ctor.GetParameters().Length == 4)
            .Invoke([target, true, 0.0, 0.0]);
        args.RoutedEvent = FrameworkElement.ContextMenuOpeningEvent;
        target.RaiseEvent(args);
    }

    [Fact]
    public Task メニューを開く前に_その行が選ばれる() => UiThread.Run(() =>
    {
        var first = new ListBoxItem { Content = "1" };
        var second = new ListBoxItem { Content = "2" };
        SelectOnContextMenu.SetIsEnabled(first, true);
        SelectOnContextMenu.SetIsEnabled(second, true);
        var list = new ListBox();
        list.Items.Add(first);
        list.Items.Add(second);
        list.SelectedItem = first;

        RaiseOpening(second);

        Assert.True(second.IsSelected);
        Assert.False(first.IsSelected);
    });

    [Fact]
    public Task 付けていない行は_開いても選ばれない() => UiThread.Run(() =>
    {
        var plain = new ListBoxItem { Content = "1" };
        var list = new ListBox();
        list.Items.Add(plain);

        RaiseOpening(plain);

        Assert.False(plain.IsSelected);
    });
}
