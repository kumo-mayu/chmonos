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

        // 打つ欄が1つの窓は、開いたらすぐ打てるようにその欄に止まる（ui-dialogs.md「キー」）。
        // 入っている ID があれば全部選び、打てば置き換わるようにする
        Loaded += (_, _) =>
        {
            IdBox.Focus();
            IdBox.SelectAll();
        };
    }

    private void OnMove(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
