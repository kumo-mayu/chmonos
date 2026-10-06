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

        // 閉じた後も探し続けると、誰も見ない結果のために BOOTH へ問い合わせ続ける
        Closed += (_, _) => model.StopSearchCommand.Execute(null);
    }

    private void OnMove(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
