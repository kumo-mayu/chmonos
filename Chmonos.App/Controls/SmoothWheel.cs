using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Chmonos.App.Controls;

/// <summary>
/// ホイールで一覧を滑らかに流す（検索のカード・リスト。ユーザ指示 2026-11-15「スクロールのアニメーションが無いと
/// 内容が飛んだような印象があって一瞬集中が途切れる」、設定で選べる）。
///
/// WPF の既定は、ホイール1回で決まった量だけ一度に動く。ここでは1回ごとに「行き先」を足し、描画のたびに今の位置を
/// 行き先へ寄せる（指数で近づく：速く回すほど行き先が遠くなり、止めると素直に止まる）。
/// 流す単位が px の一覧（<see cref="VirtualizingPanel.ScrollUnitProperty"/> が Pixel）にだけ効く。行単位の一覧は途中の値を持てない。
/// Ctrl（表示の大きさ）・Shift（横送り）は触らない。
/// </summary>
public static class SmoothWheel
{
    /// <summary>
    /// 1行の高さ（DIP）。WPF の px の一覧は、ホイール1回で「Windows の設定の行数 × 16 DIP」動く。
    /// 滑らかにしても量が変わると使い慣れた手触りが変わるので、既定の量に合わせる
    /// </summary>
    private const double LineDip = 16;

    /// <summary>
    /// 行き先へ寄せる速さの時定数（秒）。0.09 秒だと 1 回のホイールが約 0.2 秒で 90% 届く。
    /// これより短いと飛んだように見え、長いと回し終えてからも動き続けて追いつけない
    /// </summary>
    private const double TauSeconds = 0.09;

    /// <summary>
    /// 画面外の行の高さは見積もりなので、流すたびに全体の高さが変わり、置いた位置が少し丸められる。
    /// その程度（2 DIP まで）は、外から動かされたとはみなさない
    /// </summary>
    private const double ExternalMoveDip = 2;

    /// <summary>
    /// <paramref name="list"/> のホイールを受ける。<paramref name="enabled"/> が偽の間は何もせず、WPF の既定のまま流れる
    /// （設定は回すたびに読む：切り替えてすぐ効かせるため）。
    /// </summary>
    public static void Attach(ItemsControl list, Func<bool> enabled)
    {
        var state = new State(list);
        list.PreviewMouseWheel += (_, e) => state.OnWheel(e, enabled);
    }

    private sealed class State
    {
        private readonly ItemsControl _list;
        private ScrollViewer? _viewer;
        private double _target;
        private double _lastSet;
        private DateTime _last;
        private bool _running;

        public State(ItemsControl list) => _list = list;

        public void OnWheel(MouseWheelEventArgs e, Func<bool> enabled)
        {
            if (e.Handled || !enabled() || Keyboard.Modifiers != ModifierKeys.None
                || VirtualizingPanel.GetScrollUnit(_list) != ScrollUnit.Pixel)
            {
                return;
            }

            var viewer = ContentItemsControl.FindScrollViewer(_list);
            if (viewer is null || viewer.ScrollableHeight <= 0)
            {
                return;
            }

            // 一覧の中の別の流せる部品の上のホイールは、その部品に任せる
            if (e.OriginalSource is DependencyObject source && NearestViewer(source) != viewer)
            {
                return;
            }

            // 外から位置が動かされていたら（つまみのドラッグ・位置を戻す処理）、今の位置から数え直す
            if (!_running || _viewer != viewer || Math.Abs(viewer.VerticalOffset - _lastSet) > ExternalMoveDip)
            {
                _target = viewer.VerticalOffset;
            }

            _viewer = viewer;
            var step = -e.Delta / 120.0 * SystemParameters.WheelScrollLines * LineDip;
            _target = Math.Clamp(_target + step, 0, viewer.ScrollableHeight);
            e.Handled = true;

            if (!_running)
            {
                _running = true;
                _last = DateTime.UtcNow;
                _lastSet = viewer.VerticalOffset;
                CompositionTarget.Rendering += OnRendering;
            }
        }

        private static ScrollViewer? NearestViewer(DependencyObject source)
        {
            for (var node = source; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            {
                if (node is ScrollViewer scroll)
                {
                    return scroll;
                }
            }

            return null;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var viewer = _viewer;
            if (viewer is null || Math.Abs(viewer.VerticalOffset - _lastSet) > ExternalMoveDip)
            {
                Stop();
                return;
            }

            var now = DateTime.UtcNow;
            var seconds = Math.Min((now - _last).TotalSeconds, 0.1);
            _last = now;

            var from = viewer.VerticalOffset;
            var next = from + (_target - from) * (1 - Math.Exp(-seconds / TauSeconds));
            if (Math.Abs(_target - next) < 0.5)
            {
                next = _target;
            }

            viewer.ScrollToVerticalOffset(next);
            _lastSet = next;
            if (next == _target)
            {
                Stop();
            }
        }

        private void Stop()
        {
            CompositionTarget.Rendering -= OnRendering;
            _running = false;
        }
    }
}
