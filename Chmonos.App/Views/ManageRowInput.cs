using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>
/// 管理の画面（タグ・属性）のリストの行に、カードと同じ中クリック（BOOTH を開く）を付ける
/// （ユーザ判断 2026-11-18：リストの行にもカードと同じ右クリックと中クリック）。
/// 行の型（<c>ManageItemTemplate</c>）は共有の資源にあり、<c>OnCardMouseDown</c> の EventSetter は付けられないので、画面ごと受ける
/// </summary>
internal static class ManageRowInput
{
    /// <summary>
    /// 大きさの知らせの付け外し（画面を出している間だけ聞く）と、中クリックの受け取りを付ける。
    /// <paramref name="attach"/>・<paramref name="detach"/>・<paramref name="openBooth"/> は、そのときの ViewModel に向ける
    /// </summary>
    public static void Hook(FrameworkElement view, Action attach, Action detach, Action<TagItemRow> openBooth)
    {
        view.Loaded += (_, _) => attach();
        view.Unloaded += (_, _) => detach();
        view.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle)
            {
                return;
            }

            for (var node = e.OriginalSource as DependencyObject; node is not null; node = Parent(node))
            {
                if (node is Button { DataContext: TagItemRow row })
                {
                    openBooth(row);
                    e.Handled = true;
                    return;
                }
            }
        };
    }

    private static DependencyObject? Parent(DependencyObject node)
        => node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
}
