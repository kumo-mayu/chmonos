using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

public partial class ItemView : UserControl
{
    /// <summary>
    /// サムネイルに乗ってから切り替えるまでの間を計る。
    /// 一覧が複数行になると下の行へ向かう途中で上の行を横切るので、
    /// 「通り過ぎた」だけでは切り替えず、止まったときだけ切り替える。
    /// </summary>
    private readonly DispatcherTimer _dwellTimer = new();

    private GalleryImage? _pending;

    public ItemView()
    {
        InitializeComponent();

        _dwellTimer.Tick += OnDwellElapsed;
        Unloaded += (_, _) => _dwellTimer.Stop();
    }

    private void OnThumbnailMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GalleryImage image }
            || DataContext is not ItemViewModel item
            || !item.SwitchOnHover)
        {
            return;
        }

        _pending = image;

        if (item.HoverDelayMs == 0)
        {
            Apply();
            return;
        }

        // 掃くように動かしている間は乗るたびに測り直すので、止まるまで発火しない
        _dwellTimer.Stop();
        _dwellTimer.Interval = TimeSpan.FromMilliseconds(item.HoverDelayMs);
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
        if (_pending is not null && DataContext is ItemViewModel item)
        {
            item.HoverImage(_pending);
        }
    }
}
