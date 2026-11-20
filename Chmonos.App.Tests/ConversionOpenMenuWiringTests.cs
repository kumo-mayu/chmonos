using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;

namespace Chmonos.App.Tests;

/// <summary>
/// 「変換」の右クリックの「開く」（ユーザ判断 2026-11-20「共通素体から共通素体のページに飛べた方が良い」）が、XAML で行の命令に結ばれていること。
/// 結び付けの名前を書き違えても組み立ては通り、メニューは出るのに押して何も起きない（CardMenuWiringTests と同じ心配）ので、
/// 項目の結び付けの道を XAML から取り、行の型の公開の性質に届くかを見る
/// </summary>
public class ConversionOpenMenuWiringTests
{
    private static string CommandPath(MenuItem item)
        => BindingOperations.GetBinding(item, MenuItem.CommandProperty)?.Path.Path ?? string.Empty;

    private static void AssertResolves(Type type, string path)
        => Assert.True(type.GetProperty(path, BindingFlags.Public | BindingFlags.Instance) is not null, $"{type.Name}.{path} が無い");

    [Fact]
    public Task 組の行と元の見出しの右クリックは_元と先と見出しの開くに結ぶ() => UiThread.Run(() =>
    {
        var view = new AvatarsView();

        var rowMenu = (ContextMenu)view.Resources["ConversionPairRowMenu"];
        var rowItems = rowMenu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(["元を開く", "先を開く"], rowItems.Select(item => (string)item.Header));
        Assert.Equal(["OpenFromCommand", "OpenToCommand"], rowItems.Select(CommandPath));
        rowItems.ForEach(item => AssertResolves(typeof(ConversionPairRow), CommandPath(item)));

        var groupItem = Assert.Single(((ContextMenu)view.Resources["ConversionPairGroupMenu"]).Items.OfType<MenuItem>());
        Assert.Equal("開く", groupItem.Header);
        AssertResolves(typeof(ConversionPairGroup), CommandPath(groupItem));

        // 行の型にメニューを付け、見出しの時は見出しのメニューに差し替える
        var style = (Style)view.Resources["ConversionPairRowStyle"];
        Assert.Same(rowMenu, style.Setters.OfType<Setter>().Single(setter => setter.Property == FrameworkElement.ContextMenuProperty).Value);
        var header = style.Triggers.OfType<DataTrigger>().Single();
        Assert.Same(
            view.Resources["ConversionPairGroupMenu"],
            header.Setters.OfType<Setter>().Single(setter => setter.Property == FrameworkElement.ContextMenuProperty).Value);
        return Task.CompletedTask;
    });

    [Fact]
    public Task つなぎ先の札の右クリックの開くは_口がある時だけ出し_行の命令に結ぶ() => UiThread.Run(() =>
    {
        var panel = new ItemConversionPanel();
        var list = Descendants(panel).OfType<ItemsControl>()
            .Single(control => control.ItemTemplate?.DataType as Type == typeof(ConversionTargetRow));
        var row = (DependencyObject)list.ItemTemplate.LoadContent();
        var chip = Descendants(row).OfType<Border>().Single(border => AutomationProperties.GetAutomationId(border) == "ItemConversionTarget");

        var open = chip.ContextMenu!.Items.OfType<MenuItem>().First();
        Assert.Equal("開く", open.Header);
        Assert.Equal("OpenCommand", CommandPath(open));
        AssertResolves(typeof(ConversionTargetRow), CommandPath(open));
        Assert.Equal("HasOpen", BindingOperations.GetBinding(open, UIElement.VisibilityProperty)?.Path.Path);
        AssertResolves(typeof(ConversionTargetRow), "HasOpen");
        return Task.CompletedTask;
    });

    /// <summary>論理の木と画面の木の両方をたどる（テンプレートの中身は組み立て前なので論理の木だけにある）。</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        var stack = new Stack<DependencyObject>([root]);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            yield return current;
            foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
            {
                stack.Push(child);
            }

            if (current is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
                {
                    stack.Push(VisualTreeHelper.GetChild(current, i));
                }
            }
        }
    }
}
