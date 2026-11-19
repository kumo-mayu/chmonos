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
        // 「改変に追加して送る」の窓の「複製…」からも開くので、今手前にある窓の上に出す（主の窓に付けると、選ぶ窓の後ろの真ん中に出る）
        Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive && window != this)
            ?? Application.Current?.MainWindow;
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
