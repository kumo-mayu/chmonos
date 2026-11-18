using System.Windows;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

public partial class ExclusionListDialog : Window
{
    public ExclusionListDialog(ExclusionListViewModel model)
    {
        InitializeComponent();
        List.DataContext = model;

        // 主の窓が無い所（窓を出さずに描く台）では、最初に作った窓＝自分が主の窓になるので、持ち主にしない
        if (Application.Current?.MainWindow is { } main && main != this)
        {
            Owner = main;
        }

        // 一覧は自分で流す（部品の中の仮想化した一覧）。DialogFit の送る入れ物で包むと一覧の高さが決まらず、数千件の行を全部作ってしまう
        Services.DialogFit.Prepare(this, scrollsItself: true);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
