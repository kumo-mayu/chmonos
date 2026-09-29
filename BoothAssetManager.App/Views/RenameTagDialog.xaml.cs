using System.Windows;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

public partial class RenameTagDialog : Window
{
    public RenameTagDialog(RenameTagDialogViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Owner = Application.Current?.MainWindow;
        Services.DialogFit.Prepare(this);
    }

    private void OnCommit(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
