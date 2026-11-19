using System.Windows;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

public partial class BulkTagDialog : Window
{
    public BulkTagDialog(BulkTagDialogViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Owner = Application.Current?.MainWindow;
        Services.DialogFit.Prepare(this);

        // 開いたらすぐ打てるように、最初の欄に止まる（ui-dialogs.md「キー」）
        Loaded += (_, _) => (model.IsRemoving ? RemovalBox : model.IsAddingAttribute ? AttributeBox : TopBox).FocusInputQuietly();
    }

    private void OnCommit(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
