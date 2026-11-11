using System.Windows;
using System.Windows.Input;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

public partial class ApiClientApprovalDialog : Window
{
    private readonly ApiClientApprovalViewModel _model;

    public ApiClientApprovalDialog(ApiClientApprovalViewModel model, Window? owner)
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

    /// <summary>
    /// ［拒否］の上でのマウスの押し下げを覚える（クリックの拒否だけを止めるまでの回数に数える。ユーザ判断 2026-11-11）。
    /// タッチとペンもマウスの押し下げとして来る
    /// </summary>
    private void OnDenyPointerDown(object sender, MouseButtonEventArgs e) => _model.NotePointerDown();

    /// <summary>
    /// Esc は許さずに閉じるが、拒否には数えない（2026-11-11）。［拒否］の IsCancel にすると Esc でも Click が走り、
    /// 押した拒否と見分けられない。後で決めようと閉じただけで止める回数が進まないように、ここで分ける。
    /// どのキーでも、マウスの押し下げの印を下ろす（Enter・Space の［拒否］は数えない。押すより先にここを通る）
    /// </summary>
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
