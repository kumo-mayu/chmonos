using System.Windows;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

public partial class PickPackagesDialog : Window
{
    public PickPackagesDialog(PickPackagesDialogViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Owner = Application.Current?.MainWindow;
        Services.DialogFit.Prepare(this);
    }

    /// <summary>選ばせる。「送る」なら true（チェックは各候補の <see cref="PackageChoiceRow.IsChecked"/> に残る）。</summary>
    public static bool Ask(PickPackagesDialogViewModel model) => new PickPackagesDialog(model).ShowDialog() == true;

    private void OnCommit(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
