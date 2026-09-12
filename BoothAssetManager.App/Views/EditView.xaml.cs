using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

public partial class EditView : UserControl
{
    /// <summary>
    /// 画像の一覧に乗ってから切り替えるまでの間を計る（商品ページと同じ動き）。
    /// 下の行へ向かう途中で上の行を横切るので、止まったときだけ切り替える。
    /// </summary>
    private readonly DispatcherTimer _dwellTimer = new();

    private GalleryImage? _pending;

    /// <summary>帯の絵1枚ぶんの幅（絵56＋隙間6）。EditView.xaml の帯の絵の Width と Margin に合わせる。</summary>
    private const double QueueTileStride = 62;

    private EditViewModel? _edit;

    public EditView()
    {
        InitializeComponent();

        _dwellTimer.Tick += OnDwellElapsed;
        Unloaded += (_, _) => _dwellTimer.Stop();
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

    private void OnThumbnailMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GalleryImage image }
            || DataContext is not EditViewModel edit
            || !edit.SwitchOnHover)
        {
            return;
        }

        _pending = image;

        if (edit.HoverDelayMs == 0)
        {
            Apply();
            return;
        }

        // 掃くように動かしている間は乗るたびに測り直すので、止まるまで発火しない
        _dwellTimer.Stop();
        _dwellTimer.Interval = TimeSpan.FromMilliseconds(edit.HoverDelayMs);
        _dwellTimer.Start();
    }

    /// <summary>滞留を待っている最中に離れたら取り消す。</summary>
    private void OnThumbnailMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GalleryImage image } && ReferenceEquals(image, _pending))
        {
            _dwellTimer.Stop();
            _pending = null;
        }
    }

    private void OnDwellElapsed(object? sender, EventArgs e)
    {
        _dwellTimer.Stop();
        Apply();
    }

    private void Apply()
    {
        if (_pending is not null && DataContext is EditViewModel edit)
        {
            edit.HoverImage(_pending);
        }
    }
}
