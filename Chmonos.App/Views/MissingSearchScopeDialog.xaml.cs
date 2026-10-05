using System.Windows;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

public partial class MissingSearchScopeDialog : Window
{
    public MissingSearchScopeDialog(MissingSearchScopeViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        // 主の窓が無い所（窓を出さずに描く台）では、最初に作った窓＝自分が主の窓になるので、持ち主にしない
        if (Application.Current?.MainWindow is { } main && main != this)
        {
            Owner = main;
        }

        Services.DialogFit.Prepare(this);
    }

    private void OnSearch(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
