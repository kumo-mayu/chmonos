using System.Windows;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>ファイルを持たない「BOOTHに無い商品」を作るときに名前を聞く小窓。</summary>
public partial class LocalItemNameDialog : Window
{
    public LocalItemNameDialog(LocalItemNameDialogViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Owner = Application.Current?.MainWindow;
        Services.DialogFit.Prepare(this);

        // 開いたらすぐ打てるように名前の欄に止める。Enter で決まる
        Loaded += (_, _) => NameBox.Focus();
    }

    private void OnCommit(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
