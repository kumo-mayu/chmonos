using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

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
        DataContextChanged += OnDataContextChanged;
    }

    private SettingsViewModel? _watched;

    /// <summary>
    /// 非表示にした商品の商品ページへ行って戻ると、流した位置が失われていた（open.md 50）。設定の画面は開くたびに作り直されるので、
    /// 商品ページへ移る直前の位置を静的な値に預け、戻って読み終えたときに1回だけ戻す。
    /// 読み終えるまで待つのは、行が入る前は画面が短く、その位置まで流せないため
    /// </summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_watched is not null)
        {
            _watched.OpeningItem -= RememberScroll;
            _watched.PropertyChanged -= OnSettingsChanged;
        }

        _watched = e.NewValue as SettingsViewModel;
        if (_watched is not null)
        {
            _watched.OpeningItem += RememberScroll;
            _watched.PropertyChanged += OnSettingsChanged;
        }
    }

    private void RememberScroll() => SettingsViewModel.PendingScrollOffset = PageScroll.VerticalOffset;

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsViewModel.IsLoading) || sender is not SettingsViewModel { IsLoading: false }
            || SettingsViewModel.PendingScrollOffset is not { } offset)
        {
            return;
        }

        SettingsViewModel.PendingScrollOffset = null;
        Dispatcher.BeginInvoke(() =>
        {
            PageScroll.UpdateLayout();
            PageScroll.ScrollToVerticalOffset(offset);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
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
