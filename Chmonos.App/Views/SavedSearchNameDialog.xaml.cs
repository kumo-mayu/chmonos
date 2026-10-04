using System.Windows;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>保存した検索の名前を決める小窓（今の検索を保存・名前を変更）。</summary>
public partial class SavedSearchNameDialog : Window
{
    public SavedSearchNameDialog(SavedSearchNameDialogViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Owner = Application.Current?.MainWindow;
        Services.DialogFit.Prepare(this);

        // 名前の欄に止まり、入れて始めた要約を全部選んでおく。そのまま打てば置き換わり、Enter ならそのまま保存できる
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void OnCommit(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
