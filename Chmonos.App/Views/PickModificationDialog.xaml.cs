using System.Windows;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

public partial class PickModificationDialog : Window
{
    public PickModificationDialog(PickModificationDialogViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Owner = Application.Current?.MainWindow;
        Services.DialogFit.Prepare(this);
    }

    private void OnCommit(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
