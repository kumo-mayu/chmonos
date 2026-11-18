using System.Windows;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

public partial class MoveSubDialog : Window
{
    public MoveSubDialog(MoveSubDialogViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Owner = Application.Current?.MainWindow;
        Services.DialogFit.Prepare(this);

        // 打つ欄が1つの窓は、開いたらすぐ打てるようにその欄に止まる（ui-dialogs.md「キー」）
        Loaded += (_, _) => TargetBox.FocusInput();
    }

    private void OnMove(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
