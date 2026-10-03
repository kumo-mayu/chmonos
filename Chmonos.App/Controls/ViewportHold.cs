using System.Windows;
using System.Windows.Controls;

namespace Chmonos.App.Controls;

/// <summary>
/// 外の ScrollViewer の中に置いた、仮想化しない一覧の行の中身を、見えている辺りだけ作る（ショップの中：上の段と一覧が1本のスクロール）。
///
/// 1本のスクロールにするため一覧の仮想化を外すと、300件の店でカード300枚を全部作り、開いて落ち着くまで 0.7秒 → 約2.5秒、
/// メモリ（プライベート）が約110MB増えた（2026-10-03・ViewShot の台・幅1100）。
/// 一覧が外の ScrollViewer の中にあると、WPF の仮想化は「どこが見えているか」を知らないので効かない（中の板は高さの制限なしで測られる）。
/// 行の入れ物は全部作る（軽い。中身の無い行は高さだけを持つ）が、中身（カードの段・リストの列）は見えている辺りの行だけ作り、
/// 遠く離れた行の中身は捨てる——仮想化した一覧と同じくらいの量だけ作ることになる。
///
/// 行の高さは中身の有無で変わらない（カードの段は枠の高さを別に持ち、リストの行は高さの下限が絵の大きさを決める）ので、
/// 作る・捨てるで流した位置も見ている商品も動かない。
///
/// 行を「控える」印（<see cref="IsHeldProperty"/>）は継がれる値で、一覧に付けると全部の行が控えになり、見えている辺りの行だけ偽にする。
/// カード（<see cref="DeferredCardHost"/>）・カードの段（ShopView）・リストの行の型（ItemListRowStyle）がこの印を見る。
/// 付けない一覧（検索）では何も変わらない。
/// </summary>
public sealed class ViewportHold
{
    /// <summary>真の間は、中身を作らない（作ってあれば捨てる）。一覧に付けて継がせ、見えている辺りの行だけ偽にする。</summary>
    public static readonly DependencyProperty IsHeldProperty = DependencyProperty.RegisterAttached(
        "IsHeld", typeof(bool), typeof(ViewportHold),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnIsHeldChanged));

    public static bool GetIsHeld(DependencyObject element) => (bool)element.GetValue(IsHeldProperty);

    public static void SetIsHeld(DependencyObject element, bool value) => element.SetValue(IsHeldProperty, value);

    /// <summary>
    /// その行の控えを今すぐ外す（キーボードで、見えている所から遠い行へ移るとき。<see cref="ArrowGroup"/> の End）。
    /// 控えていなければ偽。外した行は、次に流したとき遠ければまた控えに戻る
    /// </summary>
    public static bool Release(DependencyObject container)
    {
        if (!GetIsHeld(container))
        {
            return false;
        }

        container.SetValue(IsHeldProperty, false);
        return true;
    }

    private static void OnIsHeldChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is DeferredCardHost host)
        {
            host.OnHeldChanged((bool)e.NewValue);
        }
    }

    /// <summary>
    /// 見えている所から上下1画面の行の中身を作る（仮想化した一覧が先に作っておく量と同じ。WPF の既定は上下1画面）。
    /// 捨てるのは上下2画面より外——境の辺りで少し行き来しただけで、作っては捨てるを繰り返さないように
    /// </summary>
    private const double MakeMargin = 0.25;
    private const double KeepMargin = 1;

    private readonly ScrollViewer _viewer;
    private readonly FrameworkElement _content;

    /// <param name="viewer">全体を流す ScrollViewer。</param>
    /// <param name="content">その中身（行の位置を、流れの位置に左右されない中身の上からの距離で測る）。</param>
    public ViewportHold(ScrollViewer viewer, FrameworkElement content)
    {
        _viewer = viewer;
        _content = content;
    }

    /// <summary>今見えている辺りの行の控えを外し、遠く離れた行を控えに戻す。並べ終えた後（位置が測れる時）に呼ぶ。</summary>
    public void Update(ItemsControl list)
    {
        // 隠れている一覧は位置を測れないので、そのままにする。作ってある分は持ち続ける（仮想化した一覧も、隠れた間は前の行を持ったまま）。
        // 隠したときに捨てると、カード⇄リストを切り替えて戻るたびに見えている分を作り直し、切り替えが約50ms遅くなった
        var viewport = _viewer.ViewportHeight;
        if (!list.IsVisible || viewport <= 0)
        {
            return;
        }

        var top = _viewer.VerticalOffset;
        var bottom = top + viewport;
        for (var index = 0; index < list.Items.Count; index++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container
                || !container.IsVisible
                || !_content.IsAncestorOf(container))
            {
                continue;
            }

            var y = container.TransformToAncestor(_content).Transform(new Point(0, 0)).Y;
            var end = y + container.ActualHeight;
            if (end >= top - (viewport * MakeMargin) && y <= bottom + (viewport * MakeMargin))
            {
                Release(container);
            }
            else if ((end < top - (viewport * KeepMargin) || y > bottom + (viewport * KeepMargin)) && IsReleased(container))
            {
                Hold(container);
            }
        }
    }

    private static bool IsReleased(DependencyObject container) => container.ReadLocalValue(IsHeldProperty) is false;

    private static void Hold(FrameworkElement container)
    {
        // キーボードで止まっている行は捨てない（中身を捨てるとフォーカスが窓の根へ落ちる）
        if (!container.IsKeyboardFocusWithin)
        {
            container.ClearValue(IsHeldProperty);
        }
    }
}
