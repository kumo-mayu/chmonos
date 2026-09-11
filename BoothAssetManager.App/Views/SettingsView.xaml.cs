using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

public partial class SettingsView : UserControl
{
    /// <summary>
    /// ショートカットを打ち込む欄の印。窓全体のショートカットはこの欄では働かせない
    /// （Ctrl+F を割り当てようとして検索へ飛ばないため）。
    /// </summary>
    public const string ShortcutCaptureTag = "shortcut-capture";

    public SettingsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 欄を選んでキーを押すと、そのキーに割り当てる（#43）。
    /// 修飾キーだけの段階ではまだ決めない。Backspace・Delete で外す。Tab は次の欄へ（奪うと抜けられない）。
    /// </summary>
    private void OnShortcutKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ShortcutRow row } || DataContext is not SettingsViewModel settings)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None && key is Key.Tab or Key.Escape)
        {
            return;
        }

        e.Handled = true;
        settings.AssignShortcut(row, modifiers == ModifierKeys.None && key is Key.Back or Key.Delete
            ? string.Empty
            : Services.Shortcuts.Format(key, modifiers));
    }
}
