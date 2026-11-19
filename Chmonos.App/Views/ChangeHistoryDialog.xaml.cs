using System.Windows;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

public partial class ChangeHistoryDialog : Window
{
    public ChangeHistoryDialog(ChangeHistoryViewModel model)
    {
        InitializeComponent();
        DataContext = model;

        // 主の窓が無い所（窓を出さずに描く台）では、最初に作った窓＝自分が主の窓になるので、持ち主にしない
        if (Application.Current?.MainWindow is { } main && main != this)
        {
            Owner = main;
        }

        // 左の一覧と右の差分はそれぞれ自分で流す。送る入れ物で包むと一覧の高さが決まらず、履歴の多い商品で行を全部作る
        Services.DialogFit.Prepare(this, scrollsItself: true);

        // 開いたら選んだ件（いちばん新しい物）にフォーカスを置き、上下のキーで履歴を送れるようにする
        Loaded += (_, _) =>
        {
            if (EntryList.SelectedIndex >= 0
                && EntryList.ItemContainerGenerator.ContainerFromIndex(EntryList.SelectedIndex) is UIElement row)
            {
                row.Focus();
            }
        };
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
