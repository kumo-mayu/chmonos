using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chmonos.App.Controls;

/// <summary>
/// ドラッグの途中、つかんだ点が欄の上下の端に来たら、その欄を自動で流す（メモ32-③ 2026-10-04）。
/// 前は、並べ替えたい行が見えている範囲の外にあると、つかんだまま手を離してホイールを回し、またつかみ直すしかなかった。
///
/// 端に近いほど速くし、端の外まで出たら最速で頭打ちにする。離す・欄の外へ出る・押し直すと止まる
/// （流すのは DragOver が来たときだけ。DragOver は動かさなくても周期で来るので、端に置いたままでも流れ続ける）。
/// 並べ替え（<see cref="RowReorder"/>）から使うが、ドラッグを持つ欄ならどこでも使える。
/// </summary>
public sealed class DragEdgeScroll
{
    /// <summary>端から内側へ、この幅の帯に入ると流れ始める。行（約40px）の1つ分より少し広い。小さな欄では高さの 1/3 まで</summary>
    public const double ZoneHeight = 48;

    /// <summary>最速（px/秒）。1秒で約 15 行。これ以上だと目当ての行を通り過ぎて、止めにくい</summary>
    public const double MaxSpeed = 700;

    /// <summary>時計の間隔の上限。ほかの処理で間が空いたあとに、一気に飛ばないようにする</summary>
    private const double MaxStepSeconds = 0.1;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastSeconds = double.NaN;

    /// <summary>
    /// 端からの近さに応じた速さ（px/秒）。負は上へ、正は下へ。帯の外は 0。
    /// </summary>
    /// <param name="position">欄の上端から数えた、つかんだ点の縦の位置</param>
    /// <param name="extent">欄の見える高さ</param>
    public static double Velocity(double position, double extent)
    {
        if (extent <= 0)
        {
            return 0;
        }

        var zone = Math.Min(ZoneHeight, extent / 3);
        if (position < zone)
        {
            return -MaxSpeed * Math.Min(1, (zone - position) / zone);
        }

        if (position > extent - zone)
        {
            return MaxSpeed * Math.Min(1, (position - (extent - zone)) / zone);
        }

        return 0;
    }

    /// <summary>止める（次に来た DragOver を最初の1回として扱い、間の時間で飛ばない）。</summary>
    public void Reset() => _lastSeconds = double.NaN;

    /// <summary>
    /// DragOver のたびに呼ぶ。端の帯にいる間、前の呼びからの経過時間ぶんだけ流す。流した量（px）を返す。
    /// </summary>
    /// <param name="position">scroller の上端から数えた、つかんだ点の縦の位置</param>
    public double Update(ScrollViewer? scroller, double position)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var elapsed = double.IsNaN(_lastSeconds) ? 0 : Math.Min(now - _lastSeconds, MaxStepSeconds);
        _lastSeconds = now;

        return scroller is null ? 0 : Apply(scroller, Velocity(position, scroller.ViewportHeight) * elapsed);
    }

    /// <summary>範囲の中でだけ動かす。動いた量を返す。</summary>
    public static double Apply(ScrollViewer scroller, double delta)
    {
        if (delta == 0 || scroller.ScrollableHeight <= 0)
        {
            return 0;
        }

        var before = scroller.VerticalOffset;
        var next = Math.Clamp(before + delta, 0, scroller.ScrollableHeight);
        scroller.ScrollToVerticalOffset(next);
        return next - before;
    }

    /// <summary>
    /// つかんだ点（from）から並びの欄（scope）までの間にある、縦に流せる ScrollViewer のうち一番外側を返す。
    /// 欄の中の小さな入れ子の流し枠（積んだ値の一覧など）ではなく、並び全体を持つ側を流したいので、外側を取る。
    /// 間に無ければ、欄の外の祖先から探す
    /// </summary>
    public static ScrollViewer? FindScroller(DependencyObject? from, DependencyObject scope)
    {
        ScrollViewer? outermost = null;
        var current = from;
        var insideScope = true;
        while (current is not null)
        {
            if (current is ScrollViewer { ScrollableHeight: > 0 } scroller && (insideScope || outermost is null))
            {
                outermost = scroller;
                if (!insideScope)
                {
                    return outermost;
                }
            }

            if (ReferenceEquals(current, scope))
            {
                insideScope = false;
                if (outermost is not null)
                {
                    return outermost;
                }
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return outermost;
    }
}
