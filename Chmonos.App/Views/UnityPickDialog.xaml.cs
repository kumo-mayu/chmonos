using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>
/// 「Unityで選択」の木の窓。キーの動きの決め事は <see cref="UnityPickDialogViewModel"/> にあり、ここは押されたキーを渡して、選んだ行へ止まりを移すだけ。
/// </summary>
public partial class UnityPickDialog : Window
{
    private readonly UnityPickDialogViewModel _model;

    public UnityPickDialog(UnityPickDialogViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;

        // 主の窓が無い所（窓を出さずに描く台）では、最初に作った窓＝自分が主の窓になるので、持ち主にしない
        if (Application.Current?.MainWindow is { } main && main != this)
        {
            Owner = main;
        }

        // 開け閉めで行を組み直すと行の部品が作り直され、止まりが一覧の外へ落ちる。選んだ行へ戻さないと、続けて矢印で動かせない
        model.Rows.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(FocusSelected, DispatcherPriority.Loaded);
        Loaded += (_, _) => FocusSelected();
        Services.DialogFit.Prepare(this);
    }

    private void FocusSelected()
    {
        if (_model.Selected is not { } row)
        {
            List.Focus();
            return;
        }

        List.ScrollIntoView(row);
        List.UpdateLayout();
        if (List.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem item)
        {
            item.Focus();
        }
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Right:
                _model.Right();
                break;
            case Key.Left:
                _model.Left();
                break;
            case Key.Enter:
                // 葉なら決める。フォルダなら開け閉め（決めるボタンは押せないので、Enter が何も起こさないままにしない）
                if (_model.Activate())
                {
                    DialogResult = true;
                }

                break;
            default:
                return;
        }

        e.Handled = true;
        FocusSelected();
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 三角を続けて押したときは三角が開け閉めしているので、行のダブルクリックとしてもう一度開け閉めしない
        if (e.OriginalSource is DependencyObject source && IsInsideButton(source))
        {
            return;
        }

        if (sender is ListBoxItem { DataContext: UnityPickRow row })
        {
            _model.Selected = row;
            if (_model.Activate())
            {
                DialogResult = true;
            }

            e.Handled = true;
        }
    }

    private static bool IsInsideButton(DependencyObject source)
    {
        // 文字の中の部品（Run など）は見た目の木に無く、親をたどれない。行の中の三角は見た目の木の部品なので、それだけ見ればよい
        for (var at = source; at is System.Windows.Media.Visual and not ListBoxItem; at = System.Windows.Media.VisualTreeHelper.GetParent(at))
        {
            if (at is Button)
            {
                return true;
            }
        }

        return false;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_model.CanPick)
        {
            DialogResult = true;
        }
    }
}
