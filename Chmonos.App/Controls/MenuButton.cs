using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

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
}
