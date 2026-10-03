using System.Windows;
using System.Windows.Controls;

namespace Chmonos.App;

/// <summary>ナビの項目が入り切らないとき、上下に続きがあることを知らせる帯（ユーザ指示 2026-10-03）。</summary>
public partial class MainWindow
{
    /// <summary>
    /// どちらの向きに続きがあるか。送りの位置は端数を持つことがある（拡大率・画素の丸め）ので、1未満の差は端に着いているとみなす。
    /// 一番下まで送ったら下の印が消える。入り切るとき（流せる量が0）はどちらも出ない
    /// </summary>
    internal static (bool Above, bool Below) NavScrollHint(double offset, double scrollable)
        => (scrollable >= 1 && offset >= 1, scrollable >= 1 && scrollable - offset >= 1);

    private void OnNavScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var (above, below) = NavScrollHint(NavScroller.VerticalOffset, NavScroller.ScrollableHeight);
        // 重ねて出すだけなので、見えても消えても項目は動かない
        NavMoreAbove.Visibility = above ? Visibility.Visible : Visibility.Collapsed;
        NavMoreBelow.Visibility = below ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnNavMoreClick(object sender, RoutedEventArgs e)
    {
        // 帯の高さの分は項目に重なっているので、見える高さから帯の分を引いた量ずつ送る
        var page = Math.Max(NavScroller.ViewportHeight - 2 * NavMoreAbove.Height, 40);
        var target = sender == NavMoreAbove ? NavScroller.VerticalOffset - page : NavScroller.VerticalOffset + page;
        NavScroller.ScrollToVerticalOffset(target);
    }
}
