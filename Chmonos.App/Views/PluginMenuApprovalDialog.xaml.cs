using System.Windows;
using System.Windows.Input;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>項目の宣言の許しの窓（作法は <see cref="ApiClientApprovalDialog"/> と同じ。plan 44 の②）。</summary>
public partial class PluginMenuApprovalDialog : Window
{
    private readonly PluginMenuApprovalViewModel _model;

    public PluginMenuApprovalDialog(PluginMenuApprovalViewModel model, Window? owner)
    {
        InitializeComponent();
        DataContext = _model = model;

        // 主の窓が最小化されていると、持ち主にした窓も一緒に隠れる。そのときは持ち主にせず、タスクバーに出して気付けるようにする
        if (owner is { WindowState: not WindowState.Minimized } && owner != this)
        {
            Owner = owner;
        }
        else
        {
            ShowInTaskbar = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Services.DialogFit.Prepare(this);
        Loaded += (_, _) => DenyButton.Focus();
    }

    private void OnApprove(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnDeny(object sender, RoutedEventArgs e)
    {
        _model.Deny();
        DialogResult = false;
    }

    /// <summary>［拒否］の上でのマウスの押し下げを覚える（クリックの拒否だけを止めるまでの回数に数える。許しの窓と同じ）。</summary>
    private void OnDenyPointerDown(object sender, MouseButtonEventArgs e) => _model.NotePointerDown();

    /// <summary>Esc は許さずに閉じ、拒否には数えない。どのキーでもマウスの押し下げの印を下ろす（許しの窓と同じ）。</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        _model.NoteKey();
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            DialogResult = false;
            return;
        }

        base.OnPreviewKeyDown(e);
    }
}
