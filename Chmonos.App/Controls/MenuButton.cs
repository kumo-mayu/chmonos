using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Chmonos.App.Controls;

/// <summary>
/// 「開く ▾」「Unity ▾」のような、押すと下に選択肢を出すボタン。選択肢はボタンの ContextMenu に置き、左クリックでも開く
/// （右クリックでも同じ物が出る）。改変の画面と商品ページで同じ開き方にするため1か所に置く
/// </summary>
public static class MenuButton
{
    /// <summary>Click に渡す。ボタンの真下に、ボタンと同じ DataContext で ContextMenu を開く。</summary>
    public static void OpenBelow(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.DataContext = button.DataContext;
        menu.IsOpen = true;
    }

    /// <summary>
    /// PreviewKeyDown に渡す。プルダウンと同じく ↓・Alt+↓ でも開く（Enter・Space はボタンの Click で開く）。
    /// プルダウンの顔のボタン（検索の表示順）は、前はプルダウンで ↓ で開けたので、替えても同じキーで開けるようにする
    /// </summary>
    public static void OpenOnArrowDown(object sender, KeyEventArgs e)
    {
        if (OpensMenu(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers))
        {
            OpenBelow(sender, e);
            e.Handled = true;
        }
    }

    internal static bool OpensMenu(Key key, ModifierKeys modifiers)
        => key == Key.Down && modifiers is ModifierKeys.None or ModifierKeys.Alt;

    /// <summary>
    /// ContextMenu の Opened に渡す。印の付いた項目に止まって開く（プルダウンが今の選びを光らせて開くのと同じ）。
    /// 止まらないと、キーで開いたときに ↓ で先頭から数えて今の項目を探すことになる。子の中の項目に印があるときは、印の付いた親に止まる
    /// </summary>
    public static void FocusChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        // 開いた瞬間はまだ項目の部品が作られていないことがあるので、並べ終えてから止まる
        menu.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            foreach (var item in menu.Items)
            {
                if (menu.ItemContainerGenerator.ContainerFromItem(item) is MenuItem { IsChecked: true } checkedItem)
                {
                    checkedItem.Focus();
                    return;
                }
            }
        });
    }
}
