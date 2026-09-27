using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

public partial class InboxView : UserControl
{
    private InboxViewModel? _viewModel;

    public InboxView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as InboxViewModel);
        Loaded += (_, _) => BringFocusLine();
    }

    private void Attach(InboxViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
            BringFocusLine();
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InboxViewModel.FocusLine))
        {
            BringFocusLine();
        }
    }

    /// <summary>
    /// 頼まれた行まで一覧を送る（ショップの「更新あり」から開いたとき）。
    /// 一覧は仮想化していて、画面の外の行には部品が無いので、板に番号で頼む。
    /// 束を開いた直後は板がまだ行を知らないので、描き終わってから送る
    /// </summary>
    private void BringFocusLine()
    {
        if (_viewModel?.FocusLine is not { } line || !IsLoaded)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_viewModel?.FocusLine != line)
            {
                return;
            }

            var index = _viewModel.Lines.IndexOf(line);
            if (index >= 0 && FindPanel(LinesList) is { } panel)
            {
                panel.BringIndexIntoViewPublic(index);
            }

            _viewModel.FocusLine = null;
        });
    }

    private static VirtualizingStackPanel? FindPanel(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is VirtualizingStackPanel panel)
            {
                return panel;
            }

            if (FindPanel(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
