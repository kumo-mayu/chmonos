using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>
/// 検索画面：絞り込みの条件の見出しのメニュー（「…」・右クリック・Shift+F10）と、止まり直し（2026-10-01）。
/// </summary>
public partial class SearchView
{
    private SearchViewModel? _focusSource;

    /// <summary>
    /// 条件の「…」：枠の右クリックのメニューと同じ物を「…」の下に開く（1つのメニューを共有する・D8）。
    /// 閉じたら置き場を戻す。戻さないと、次に枠を右クリックしたときも「…」の下に出る。
    /// </summary>
    private void OnModuleMenuClick(object sender, RoutedEventArgs e)
        => OpenMenuBelow(sender as FrameworkElement);

    /// <summary>
    /// パネルのメニューの「絞り込みを折りたたむ」（メモ2-③）。畳むと見出しの行ごと「…」が消え、止まり先が窓へ落ちるので、
    /// 開け閉めのつまみに止まり直す（次の Enter で開き直せる）。
    /// </summary>
    private void OnCollapsePanelMenuClick(object sender, RoutedEventArgs e)
        => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => FilterPanelToggle?.Focus());

    private static void OpenMenuBelow(FrameworkElement? button)
    {
        if (button is null || FindOwnerMenu(button) is not { } menu)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.Closed += Reset;
        menu.IsOpen = true;

        static void Reset(object sender, RoutedEventArgs args)
        {
            var closed = (ContextMenu)sender;
            closed.Closed -= Reset;
            closed.ClearValue(ContextMenu.PlacementTargetProperty);
            closed.ClearValue(ContextMenu.PlacementProperty);
        }
    }

    /// <summary>いちばん近い、メニューを持つ祖先（条件の枠・パネルの見出しの行）のメニュー。</summary>
    private static ContextMenu? FindOwnerMenu(DependencyObject from)
    {
        for (var node = VisualTreeHelper.GetParent(from); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement { ContextMenu: { } menu })
            {
                return menu;
            }
        }

        return null;
    }

    /// <summary>
    /// 札「除く」を押して除くをやめた。札は消えるので、同じ枠の「…」に止まり直す（`ui-input.md`「止まっていた行が消えたら」）。
    /// 消えた札にフォーカスが残ると、次の Tab が画面の先頭から始まる。
    /// </summary>
    private void OnExcludedTagClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SearchModule module })
        {
            FocusModuleMenuSoon(module);
        }
    }

    /// <summary>検索の側から「この条件の『…』へ止まり直して」と頼まれたとき（並べ替え・外す）。</summary>
    private void OnModuleFocusRequested(SearchModule? module)
    {
        // マウスで外した・動かしたときも、止まり先が消えて窓へ落ちるのは同じなので止まり直す。
        // ほかの欄に止まっていたとき（動かした後に Tab で離れた等）は奪わない
        if (Keyboard.FocusedElement is UIElement { IsVisible: true } focused && IsInside(focused) && !IsInsideModules(focused))
        {
            return;
        }

        if (module is null)
        {
            // 条件が1つも無くなった。「＋ 条件を追加」はメニューの項目で、止まるとメニューの操作に入ってしまうので、
            // 見出しの行の「条件をクリア」に止まる
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => ClearFiltersButton?.Focus());
            return;
        }

        FocusModuleMenuSoon(module);
    }

    /// <summary>枠が作り直されるのを待ってから、その条件の「…」に止まる。</summary>
    private void FocusModuleMenuSoon(SearchModule module)
        => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (ModulesList.ItemContainerGenerator.ContainerFromItem(module) is DependencyObject container
                && FindNamed(container, "ModuleMenu") is UIElement button)
            {
                button.Focus();
            }
        });

    private bool IsInside(DependencyObject element)
    {
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, this))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsInsideModules(DependencyObject element)
    {
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, ModulesList))
            {
                return true;
            }
        }

        return false;
    }

    private static FrameworkElement? FindNamed(DependencyObject parent, string name)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement { Name: var found } element && found == name)
            {
                return element;
            }

            if (FindNamed(child, name) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    /// <summary>検索の ViewModel の頼みを受ける相手を付け替える（View は持ち回すので、DataContext が替わるたび）。</summary>
    private void HookModuleFocus(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_focusSource is not null)
        {
            _focusSource.ModuleFocusRequested -= OnModuleFocusRequested;
            _focusSource.ModuleAdded -= OnModuleAdded;
            _focusSource.SavedRowFocusRequested -= OnSavedRowFocusRequested;
            _focusSource.SavedApplied -= CloseSavedPopup;
        }

        _focusSource = e.NewValue as SearchViewModel;
        if (_focusSource is not null)
        {
            _focusSource.ModuleFocusRequested += OnModuleFocusRequested;
            _focusSource.ModuleAdded += OnModuleAdded;
            _focusSource.SavedRowFocusRequested += OnSavedRowFocusRequested;
            _focusSource.SavedApplied += CloseSavedPopup;
        }
    }

    /// <summary>
    /// メニューから条件を足した：その条件へ画面を送り、中の最初の入力欄（無ければ見出しのチェック）に止まる（案の §2）。
    /// 設定で同じ種類のそばに足すと、足した条件が画面の外（途中）に入ることがある。
    /// </summary>
    private void OnModuleAdded(SearchModule module)
        => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (ModulesList.ItemContainerGenerator.ContainerFromItem(module) is not FrameworkElement container)
            {
                return;
            }

            container.BringIntoView();
            var target = FindNamed(container, "ModuleBody") is { } body ? FirstInput(body) : null;
            (target ?? FindNamed(container, "ModuleEnabled"))?.Focus();
        });

    /// <summary>条件の中身の最初の入力欄（文字の欄・選ぶ欄）。</summary>
    private static FrameworkElement? FirstInput(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is TextBox or ComboBox && child is FrameworkElement { IsVisible: true, IsEnabled: true, Focusable: true } input)
            {
                return input;
            }

            if (FirstInput(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }
}
