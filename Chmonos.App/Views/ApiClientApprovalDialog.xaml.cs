using System.Windows;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

public partial class ApiClientApprovalDialog : Window
{
    public ApiClientApprovalDialog(ApiClientApprovalViewModel model, Window? owner)
    {
        InitializeComponent();
        DataContext = model;

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

    private void OnDeny(object sender, RoutedEventArgs e) => DialogResult = false;
}
