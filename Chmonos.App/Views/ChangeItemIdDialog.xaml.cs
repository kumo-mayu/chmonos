using System.Windows;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

public partial class ChangeItemIdDialog : Window
{
    public ChangeItemIdDialog(ChangeItemIdDialogViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Owner = Application.Current?.MainWindow;
        Services.DialogFit.Prepare(this);
    }

    private void OnMove(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
