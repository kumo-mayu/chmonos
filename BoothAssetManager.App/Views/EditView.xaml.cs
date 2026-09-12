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

    public EditView()
    {
        InitializeComponent();

        _dwellTimer.Tick += OnDwellElapsed;
        Unloaded += (_, _) => _dwellTimer.Stop();
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
