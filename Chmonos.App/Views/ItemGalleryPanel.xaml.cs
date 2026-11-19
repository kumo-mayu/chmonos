using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>
/// 画像の欄（ギャラリー）。商品ページと編集画面で同じ部品を使う（ユーザ判断：画像の追加などの編集は両方で同等にする）。
/// 改変の写真もこの部品で出す（ユーザ指示 2026-09-13）。DataContext は <see cref="IGalleryHost"/>
/// </summary>
public partial class ItemGalleryPanel : UserControl
{
    /// <summary>
    /// サムネイルに乗ってから切り替えるまでの間を計る。
    /// 一覧が複数行になると下の行へ向かう途中で上の行を横切るので、
    /// 「通り過ぎた」だけでは切り替えず、止まったときだけ切り替える。
    /// </summary>
    private readonly DispatcherTimer _dwellTimer = new();

    private GalleryImage? _pending;

    public ItemGalleryPanel()
    {
        InitializeComponent();

        _dwellTimer.Tick += OnDwellElapsed;
        Unloaded += (_, _) => _dwellTimer.Stop();
    }

    /// <summary>
    /// 小さな絵の並びを畳む・広げる。覚えるのは持ち主（<see cref="IGalleryHost.GalleryTilesFolded"/>）で、
    /// 商品を替えても、次に起動しても保たれる。畳んでも「＋」の枠は残す（絵を足す入口なので）
    /// </summary>
    private void OnToggleTiles(object sender, RoutedEventArgs e)
    {
        if (DataContext is IGalleryHost host)
        {
            host.GalleryTilesFolded = !host.GalleryTilesFolded;
        }
    }

    /// <summary>大きな絵の高さの下限。前の決まった高さで、狭い欄では今までと同じ見え方にする。</summary>
    internal const double BigImageMinHeight = 420;

    /// <summary>
    /// 大きな絵の高さの上限。1080p の画面で窓を最大にした時の中身の高さ（約 1,000）の7割ほどで、
    /// 絵の下の小さな絵の段が見える所に残るようにした。これより高くすると、絵を送るたびに流して小さな絵を探すことになる
    /// </summary>
    internal const double BigImageMaxHeight = 720;

    /// <summary>
    /// 欄の幅から大きな絵の高さを決める（ユーザ判断 2026-11-20：右の欄の幅を広げたら絵も大きくなる方が画面を広く使える）。
    /// 幅の 3/4 にする。BOOTH の商品の絵は正方形と横長が多く、正方形の絵は幅の 3/4 の高さで左右に少し余白が出るくらいに収まる。
    /// 前は高さが 420 に決まっていて、幅を広げても縦長・正方形の絵は大きくならなかった
    /// </summary>
    internal static double BigImageHeight(double width)
        => double.IsFinite(width) && width > 0
            ? Math.Clamp(Math.Round(width * 3 / 4), BigImageMinHeight, BigImageMaxHeight)
            : BigImageMinHeight;

    /// <summary>
    /// 欄の幅が変わったら大きな絵の高さを合わせる。高さを変えても幅は変わらないので、ここへ戻って来ても同じ値で止まる
    /// </summary>
    private void OnCardSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            var height = BigImageHeight(e.NewSize.Width);
            if (BigImage.Height != height)
            {
                BigImage.Height = height;
            }
        }
    }

    private void OnThumbnailMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GalleryImage image }
            || DataContext is not IGalleryHost item
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
        if (_pending is not null && DataContext is IGalleryHost item)
        {
            item.HoverImage(_pending);
        }
    }
}
