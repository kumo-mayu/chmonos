using System.Windows;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

public partial class MergeAttributeDialog : Window
{
    public MergeAttributeDialog(MergeAttributeDialogViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Owner = Application.Current?.MainWindow;
        Services.DialogFit.Prepare(this);
    }

    private void OnMerge(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
