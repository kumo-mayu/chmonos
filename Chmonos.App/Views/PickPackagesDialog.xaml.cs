using System.Windows;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

public partial class PickPackagesDialog : Window
{
    public PickPackagesDialog(PickPackagesDialogViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Owner = Application.Current?.MainWindow;
        Services.DialogFit.Prepare(this);

        // 探す欄から打てるように（何十件の中から名前で探すのが、この窓を開く主な理由）
        Loaded += (_, _) => FilterBox.Focus();
    }

    /// <summary>選ばせる。「送る」なら true（試験は <see cref="PickPackagesDialogViewModel.Intercept"/> で窓を出さずに答える）。</summary>
    public static bool Ask(PickPackagesDialogViewModel model) => PickPackagesDialogViewModel.Ask(model);

    private void OnCommit(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnSkip(object sender, RoutedEventArgs e)
    {
        ((PickPackagesDialogViewModel)DataContext).Skipped = true;
        DialogResult = false;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
