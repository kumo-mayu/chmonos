using System.Windows;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>改変を作る・複製するときに名前を聞く小窓。</summary>
public partial class ModificationNameDialog : Window
{
    public ModificationNameDialog(ModificationNameDialogViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Owner = Application.Current?.MainWindow;
        Services.DialogFit.Prepare(this);

        // 名前の欄に止まり、入っている名前（複製は元の名前）を全部選んでおく。そのまま打てば置き換わり、Enter で決まる
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void OnCommit(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
