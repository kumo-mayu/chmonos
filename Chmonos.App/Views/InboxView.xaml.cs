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

        // 一覧の中の ScrollViewer は見方を切り替えるまで作られないので、上がってくる知らせで受ける
        ArchiveList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnArchiveScrolled));
    }

    /// <summary>
    /// 保管した通知を流して終わりに近づいたら、続きの100件を読む（ユーザ案 2026-11-10「流したら続きを読む」）。
    /// 終わりに着いてから読むと待ちが見えるので、画面1枚ぶん手前で読み始める。
    /// 中身の高さが変わったときにも呼ばれるので、最初の100件が画面に収まって流せないときも、そのまま続きを読む
    /// </summary>
    private void OnArchiveScrolled(object sender, ScrollChangedEventArgs e)
    {
        // 行の中の部品（選べる文字など）の ScrollViewer からも上がってくる。それは位置 0・高さが中身と同じなので、
        // 受けると行が作られるたびに続きを読み、月を丸ごと読むのと同じになる。一覧そのものの ScrollViewer だけを見る
        if (e.OriginalSource is not ScrollViewer scroll || !ReferenceEquals(scroll.TemplatedParent, ArchiveList))
        {
            return;
        }

        if (_viewModel?.Archive is { HasMore: true, IsLoadingMore: false } archive
            && e.VerticalOffset + e.ViewportHeight * 2 >= e.ExtentHeight)
        {
            archive.LoadMoreAsync().Forget();
        }
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
