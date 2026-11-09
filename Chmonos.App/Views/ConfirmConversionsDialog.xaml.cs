using System.Windows;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>「推した通りにまとめて決める」の窓。答えは <see cref="ConfirmConversionsDialogViewModel.Ask"/> で受ける。</summary>
public partial class ConfirmConversionsDialog : Window
{
    public ConfirmConversionsDialog(ConfirmConversionsDialogViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Owner = Application.Current?.MainWindow;
        Services.DialogFit.Prepare(this);
    }

    private void OnCommit(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
