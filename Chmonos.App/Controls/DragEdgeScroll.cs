using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chmonos.App.Controls;

/// <summary>
/// ドラッグの途中、つかんだ点が欄の上下の端に来たら、その欄を自動で流す（メモ32-③ 2026-10-04・メモ42 2026-10-05）。
/// 前は、並べ替えたい行が見えている範囲の外にあると、つかんだまま手を離してホイールを回し、またつかみ直すしかなかった。
///
/// 端に近いほど速くし、端の外まで出たら最速で頭打ちにする。離す・欄の外へ出る・押し直すと止まる。
/// 並べ替え（<see cref="RowReorder"/>）から使うが、ドラッグを持つ欄ならどこでも使える。
///
/// <para>
/// <b>流すのは描画の1コマごと（<see cref="CompositionTarget.Rendering"/>）で、DragOver は点の位置を覚えるだけ。</b>
/// 前は DragOver が来たときだけ流していて、カクついた（メモ42）。DragOver は描画と揃わず、マウスを止めると間が空き、
/// しかも点の下の行が替わった回は DragOver の代わりに DragLeave／DragEnter が来る（dotnet/wpf の DragDrop.cs）。
/// 台（ViewShot drag-edge-scroll-measure）で DragOver を50msごとに真似ると、画面の60Hzの1コマのうち7〜9割が止まり、
/// まとめて最大44px跳んだ。欄の DragLeave で時計を戻していたので、行の替わり目ごとにも止まり、速さが狙いの約45〜75%に落ちた。
/// ブラウザ（Chromium の AutoscrollController）・Android（AutoScrollHelper・ItemTouchHelper）も、ドラッグの点は覚えるだけで、
/// 流すのは描画の1コマごと・前のコマからの経過時間×速さ（<c>docs/dev/wpf.md</c>「ドラッグ中に端で流す」）。
/// </para>
/// </summary>
public sealed class DragEdgeScroll
{
    /// <summary>端から内側へ、この幅の帯に入ると流れ始める。行（約40px）の1つ分より少し広い。小さな欄では高さの 1/3 まで</summary>
    public const double ZoneHeight = 48;

    /// <summary>最速（px/秒）。1秒で約 15 行。これ以上だと目当ての行を通り過ぎて、止めにくい</summary>
    public const double MaxSpeed = 700;

    /// <summary>
    /// 流れ始めてから最速の割合まで上がる時間（秒）。端の行をつかんだ瞬間に一覧が走り出さないように、0 から上げる。
    /// Chromium はドラッグで端に来てから 0.2秒は流さない。Android の AutoScrollHelper は 0.5秒かけて上げる。
    /// 待つだけの間を置くと「効かない」に見えるので、間は置かずに上げる方を取り、短い方に寄せた
    /// </summary>
    public const double RampUpSeconds = 0.3;

    /// <summary>
    /// 1コマで進める時間の上限（秒）。ほかの処理で描画が止まったあとに、一気に飛ばないようにする。60Hz の3コマ分
    /// </summary>
    public const double MaxFrameSeconds = 0.05;

    /// <summary>
    /// DragOver がこの間来なければ止める（秒）。窓の外へ出たときの DragLeave は点を (0,0) で渡すので（dotnet/wpf の OleDragLeave）、
    /// 欄の外かを見誤ることがあり、その受け止め。マウスを止めていても DragOver は周期で来る（OLE が自分で呼ぶ）ので、
    /// それより十分長くする
    /// </summary>
    public const double StaleSeconds = 1.0;

    private readonly Func<double> _now;
    private ScrollViewer? _scroller;
    private double _position;
    private double _lastDragOver;
    private double _startedAt = double.NaN;
    private double _lastFrame = double.NaN;
    private double _carry;
    private bool _subscribed;
    private bool _movedLastFrame;

    public DragEdgeScroll()
        : this(StartClock())
    {
    }

    private static Func<double> StartClock()
    {
        var clock = Stopwatch.StartNew();
        return () => clock.Elapsed.TotalSeconds;
    }

    /// <param name="now">DragOver の来た時刻を測る時計（秒）。試験は手で進める時計を渡す</param>
    internal DragEdgeScroll(Func<double> now)
    {
        _now = now;
    }

    /// <summary>流している途中か（描画の1コマごとの呼び出しを受けている間）。</summary>
    public bool IsRunning => _subscribed;

    /// <summary>流した分の配置が済んだ後（次のコマの初め）。落とし先の印を、点の下に来た行へ付け直すのに使う。</summary>
    public event Action? Scrolled;

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

    /// <summary>流れ始めてからの時間に応じた、速さに掛ける割合（0〜1）。</summary>
    public static double RampUp(double secondsSinceStart)
        => Math.Clamp(secondsSinceStart / RampUpSeconds, 0, 1);

    /// <summary>
    /// 流す量を画面の画素の整数倍に丸め、端数は次のコマへ持ち越す。
    /// 画素の途中の位置に止めると、文字がコマごとに描き直されて滲みが揺れて見える。持ち越すので合計の速さは変わらない
    /// </summary>
    /// <param name="delta">このコマで流したい量（DIP）</param>
    /// <param name="carry">前のコマから持ち越した端数（DIP）。返すときに書き換える</param>
    /// <param name="pixelsPerDip">表示の倍率（100% で 1、150% で 1.5）</param>
    public static double SnapToPixels(double delta, ref double carry, double pixelsPerDip)
    {
        var total = delta + carry;
        var snapped = Math.Truncate(total * pixelsPerDip) / pixelsPerDip;
        carry = total - snapped;
        return snapped;
    }

    /// <summary>
    /// DragOver のたびに呼ぶ。点の位置と流す欄を覚え、端の帯にいれば流し始める（流すのは描画の1コマごと）。帯の外なら止める。
    /// </summary>
    /// <param name="position">scroller の上端から数えた、つかんだ点の縦の位置</param>
    public void Update(ScrollViewer? scroller, double position)
    {
        _lastDragOver = _now();
        if (scroller is null || Velocity(position, scroller.ViewportHeight) == 0)
        {
            Stop();
            return;
        }

        if (!ReferenceEquals(scroller, _scroller))
        {
            _carry = 0;
        }

        _scroller = scroller;
        _position = position;
        if (!_subscribed)
        {
            _subscribed = true;
            _startedAt = double.NaN;
            _lastFrame = double.NaN;
            _carry = 0;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    /// <summary>止める（離した・欄の外へ出た・ドラッグが終わった）。次に端に来たら、また 0 から上げる。</summary>
    public void Stop()
    {
        if (_subscribed)
        {
            CompositionTarget.Rendering -= OnRendering;
            _subscribed = false;
        }

        _scroller = null;
        _movedLastFrame = false;
        _startedAt = double.NaN;
        _lastFrame = double.NaN;
        _carry = 0;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // 1コマの中で2回以上来ることがある（中身が変わって描き直しが要るとき）。描く予定の時刻が同じなら同じコマ
        var frame = e is RenderingEventArgs args ? args.RenderingTime.TotalSeconds : _now();
        if (frame != _lastFrame)
        {
            Tick(frame);
        }
    }

    /// <summary>
    /// 描画の1コマ。前のコマからの経過時間×速さだけ流す。流した量（px）を返す。
    /// 描画の前に配置は済んでいる（<see cref="CompositionTarget.Rendering"/> は配置の後に来る）ので、今の位置は前のコマで流した後の値
    /// </summary>
    /// <param name="frameSeconds">このコマを描く時刻（秒）</param>
    internal double Tick(double frameSeconds)
    {
        var scroller = _scroller;
        if (scroller is null || _now() - _lastDragOver > StaleSeconds)
        {
            Stop();
            return 0;
        }

        if (_movedLastFrame)
        {
            // 前のコマで流した分の配置は、ここでは済んでいる（Rendering は配置の後に来る）。今の行の位置で知らせる
            _movedLastFrame = false;
            Scrolled?.Invoke();
        }

        var velocity = Velocity(_position, scroller.ViewportHeight);
        if (velocity == 0)
        {
            Stop();
            return 0;
        }

        if (double.IsNaN(_lastFrame))
        {
            // 最初のコマは経過時間が無い（流し始めた時刻をここに置く）
            _startedAt = frameSeconds;
            _lastFrame = frameSeconds;
            return 0;
        }

        var elapsed = Math.Clamp(frameSeconds - _lastFrame, 0, MaxFrameSeconds);
        _lastFrame = frameSeconds;

        var delta = velocity * RampUp(frameSeconds - _startedAt) * elapsed;
        var snapped = SnapToPixels(delta, ref _carry, VisualTreeHelper.GetDpi(scroller).PixelsPerDip);
        var moved = Apply(scroller, snapped);
        if (moved != snapped)
        {
            _carry = 0; // 端まで流し切った。持ち越すと、戻る向きに変えたときに1画素ずれる
        }

        _movedLastFrame = moved != 0;
        return moved;
    }

    /// <summary>
    /// 範囲の中でだけ動かす。動いた量を返す。
    /// <see cref="ScrollViewer.VerticalOffset"/> は次の配置まで前の値を返すので、配置を挟まずに続けて呼ぶと前の分が消える
    /// （台で 10px を4回 → 10px）。1コマに1回だけ呼ぶ
    /// </summary>
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
