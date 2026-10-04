using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chmonos.App.Tests.Support;
using Xunit;

namespace Chmonos.App.Tests;

/// <summary>
/// 押せないチェック・ラジオボタンの文字が、押せるときと違う色（薄い色）で描かれること（メモ27-①）。
/// 中身に TextBlock を直に置くと、App.xaml の TextBlock の既定の型が色を固定し、押せないのに濃いまま見えていた
/// </summary>
public class DisabledToggleTextTests
{
    public enum ToggleKind { Check, Radio }

    private static SolidColorBrush? TextColorOf(ToggleKind kind, bool enabled, bool plainContent)
    {
        ContentControl part = kind == ToggleKind.Check ? new CheckBox() : new RadioButton();
        part.Content = plainContent ? "文字" : new TextBlock { Text = "文字", FontSize = 12 };
        part.IsEnabled = enabled;
        var host = new Grid();
        host.Children.Add(part);
        host.Measure(new Size(300, 100));
        host.Arrange(new Rect(0, 0, 300, 100));
        host.UpdateLayout();
        return Descendants(part).OfType<TextBlock>().First(text => text.Text == "文字").Foreground as SolidColorBrush;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    [Theory]
    [InlineData(ToggleKind.Check, true)]
    [InlineData(ToggleKind.Check, false)]
    [InlineData(ToggleKind.Radio, true)]
    [InlineData(ToggleKind.Radio, false)]
    public Task 押せないと文字の色が変わる_直に置いた文字も文字列の中身も(ToggleKind kind, bool plainContent) => UiThread.Run(() =>
    {
        var enabled = TextColorOf(kind, enabled: true, plainContent);
        var disabled = TextColorOf(kind, enabled: false, plainContent);
        Assert.NotNull(enabled);
        Assert.NotNull(disabled);
        Assert.NotEqual(enabled.Color, disabled.Color);
    });
}
