using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>
/// 編集画面。画像・対応アバター・手元のファイルの欄は商品ページと共有する部品
/// （<see cref="ItemGalleryPanel"/>・<see cref="ItemAvatarsPanel"/>・<see cref="ItemFilesPanel"/>）で、
/// 一覧に乗せて切り替える動きも部品の側が持つ。
/// </summary>
public partial class EditView : UserControl
{
    /// <summary>帯の絵1枚ぶんの幅（絵56＋隙間6）。EditView.xaml の帯の絵の Width と Margin に合わせる。</summary>
    private const double QueueTileStride = 62;

    private EditViewModel? _edit;

    public EditView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_edit is not null)
        {
            _edit.PropertyChanged -= OnEditPropertyChanged;
        }

        _edit = DataContext as EditViewModel;
        if (_edit is not null)
        {
            _edit.PropertyChanged += OnEditPropertyChanged;
        }
    }

    /// <summary>
    /// 帯を作り直したら、済んだ5件＋今の商品が左端に来る所まで送る（ユーザ判断）。
    /// 並べ直した直後は大きさがまだ決まっていないので、描き終えてから送る。
    /// 最後の方では送れる量が尽き、最後の絵が右端で止まる（1件目が左端で止まるのと揃う）
    /// </summary>
    private void OnEditPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EditViewModel.QueueFirstVisibleIndex) || _edit is null)
        {
            return;
        }

        var first = _edit.QueueFirstVisibleIndex;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (QueueStrip.Template.FindName("StripScroll", QueueStrip) is ScrollViewer scroll)
            {
                scroll.ScrollToHorizontalOffset(first * QueueTileStride);
            }
        });
    }

    /// <summary>
    /// 上の帯の上でだけ、ホイールを横送りにする（ユーザ指示）。
    /// 帯の外では今までどおり縦に流れる。帯は上のバーの中なので、縦に流す物は元々無い。
    /// </summary>
    private void QueueStrip_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (QueueStrip.Template.FindName("StripScroll", QueueStrip) is not ScrollViewer scroll)
        {
            return;
        }

        scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta);
        e.Handled = true;
    }
}
