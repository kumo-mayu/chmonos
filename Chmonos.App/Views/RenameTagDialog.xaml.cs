using System.Windows;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

public partial class RenameTagDialog : Window
{
    public RenameTagDialog(RenameTagDialogViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Owner = Application.Current?.MainWindow;
        Services.DialogFit.Prepare(this);

        // 打つ欄が1つの窓は、開いたらすぐ打てるようにその欄に止まる（ui-dialogs.md「キー」）。
        // 欄は空で始まる（今の名前は見出しに出している）ので、全部を選ぶ物は無い
        Loaded += (_, _) => NameBox.FocusInput();
    }

    private void OnCommit(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
