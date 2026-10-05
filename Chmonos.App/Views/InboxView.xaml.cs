using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

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
                AlignToTop(index);
            }

            _viewModel.FocusLine = null;
        });
    }

    /// <summary>
    /// 送った行を画面のいちばん上に合わせる。送るだけだと行は画面の端に来て、見出しだけ見えて中身が下に隠れることがある。
    /// 一覧は <c>ScrollUnit=Pixel</c> なので、行の今の位置を測って、その分だけ流す。
    /// 送った直後は行の部品がまだ無いことがあるので、配置し直してから測る
    /// </summary>
    private void AlignToTop(int index)
    {
        if (LinesList.ScrollHost is not { } scroll)
        {
            return;
        }

        LinesList.UpdateLayout();
        if (LinesList.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container)
        {
            return;
        }

        var top = container.TransformToAncestor(scroll).Transform(new Point(0, 0)).Y - scroll.Padding.Top;
        if (Math.Abs(top) >= 1)
        {
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + top);
        }
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
