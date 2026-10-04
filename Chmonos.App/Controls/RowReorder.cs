using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Controls;

/// <summary>
/// 一覧の行をドラッグで並べ替えるための共通処理。
///
/// マウスの位置から「どの行の上か下か」を決めるのはビューの仕事で、
/// ViewModelに持たせても検証できない。タグの管理と属性の管理で同じものが要るので、
/// 画面ごとに書かずにここへ寄せる。
///
/// 落とし先の線は行のプロパティで出す（Adornerを使わずテンプレートに1本足すだけで済む）。
/// </summary>
public sealed class RowReorder
{
    /// <summary>この距離を超えて動いたらドラッグ開始。クリックと区別する。</summary>
    private static readonly double DragThreshold = SystemParameters.MinimumHorizontalDragDistance;

    private readonly FrameworkElement _scope;
    private readonly Func<IEnumerable<ReorderableRow>> _rows;
    private readonly Func<ScrollViewer?>? _scroller;
    private readonly DragEdgeScroll _edgeScroll = new();

    private Point _pressedAt;
    private ReorderableRow? _pressedRow;

    // 最後の DragOver の欄・点・運んでいる行。端で流れて点の下の行が替わったら、これで落とし先の線を付け直す
    // （点を止めたまま流れている間は DragOver が描画と揃わずに来るので、それを待つと線が遅れて跳ぶ）
    private Visual? _overSender;
    private Point _overPoint;
    private ReorderableRow? _overDragged;

    /// <param name="scroller">端で流す欄。渡さなければ、つかんだ点の下から辿って決める（<see cref="DragEdgeScroll.FindScroller"/>）。並びが欄の外の流し枠の中にあるときに渡す</param>
    public RowReorder(FrameworkElement scope, Func<IEnumerable<ReorderableRow>> rows, Func<ScrollViewer?>? scroller = null)
    {
        _scope = scope;
        _rows = rows;
        _scroller = scroller;
        _edgeScroll.Scrolled += () => ShowIndicatorAtLastPoint();
    }

    /// <summary>並べ替えが確定したとき。<c>after</c> は落とし先の行の後ろかどうか。</summary>
    public event Action<ReorderableRow, ReorderableRow, bool>? Dropped;

    public void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedAt = e.GetPosition(_scope);
        _pressedRow = RowUnder(e.OriginalSource as DependencyObject);
    }

    public void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedRow is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var moved = e.GetPosition(_scope) - _pressedAt;
        if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold)
        {
            return;
        }

        var row = _pressedRow;
        _pressedRow = null;
        StopDrag();

        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, row, DragDropEffects.Move);
        }
        finally
        {
            // 離した・Esc で取り消した・窓の外で離した、のどれでもここへ戻る。流れを確実に止める
            StopDrag();
            ClearIndicators();
        }
    }

    /// <summary>
    /// 外から来た物（ファイル・URL）は**ここの仕事ではない**ので、印も付けず、handled にもしない。
    /// 窓まで上げれば、中身で行き先が決まる（`ui-rules.md`「落とすは窓全体で受ける」・B5：
    /// 一覧の上だけ落とせず、タグや属性の画面では窓の端でしか受け付けなかった）。
    /// </summary>
    private static bool IsRowDrag(DragEventArgs e) => Dragged(e) is not null;

    public void OnDragOver(object sender, DragEventArgs e)
    {
        if (!IsRowDrag(e))
        {
            ClearIndicators();
            return;
        }

        ScrollAtEdge(sender, e);

        _overSender = sender as Visual;
        _overPoint = e.GetPosition((IInputElement)sender);
        _overDragged = Dragged(e);

        var target = ShowIndicatorAtLastPoint();
        e.Effects = target is null ? DragDropEffects.None : DragDropEffects.Move;
        e.Handled = true;
    }

    /// <summary>
    /// DragLeave は、欄の中で点の下の部品（行）が替わるたびにも来る（WPF は替わった回に DragOver の代わりに
    /// DragLeave と DragEnter を出し、それが欄まで上がる。dotnet/wpf の DragDrop.cs OleDragOver）。
    /// 端で流していると行が次々に点の下を通るので、そこで止めるとコマごとに流れが途切れる（メモ42）。
    /// 本当に欄の外へ出たときだけ止める
    /// </summary>
    public void OnDragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement element
            && StillInside(e.KeyStates, e.GetPosition(element), element.ActualWidth, element.ActualHeight))
        {
            return;
        }

        StopDrag();
        ClearIndicators();
    }

    /// <summary>
    /// 欄の中の部品が替わっただけか。窓の外へ出たときの DragLeave は、キーの状態を空・点を (0,0) で渡す（dotnet/wpf の OleDragLeave）ので、
    /// 左のボタンを押した印が無ければ外とみなす
    /// </summary>
    /// <param name="point">欄の左上から数えた点</param>
    internal static bool StillInside(DragDropKeyStates keys, Point point, double width, double height)
        => (keys & DragDropKeyStates.LeftMouseButton) != 0
            && point.X >= 0 && point.Y >= 0 && point.X < width && point.Y < height;

    /// <summary>つかんだ点が欄の上下の端なら流す（メモ32-③）。流すのは描画の1コマごと（<see cref="DragEdgeScroll"/>）。</summary>
    private void ScrollAtEdge(object sender, DragEventArgs e)
    {
        var hit = sender is Visual visual ? VisualTreeHelper.HitTest(visual, e.GetPosition((IInputElement)sender))?.VisualHit : null;
        var scroller = _scroller?.Invoke() ?? DragEdgeScroll.FindScroller(hit ?? sender as DependencyObject, (DependencyObject)sender);
        _edgeScroll.Update(scroller, scroller is null ? 0 : e.GetPosition(scroller).Y);
    }

    private void StopDrag()
    {
        _edgeScroll.Stop();
        _overSender = null;
        _overDragged = null;
    }

    public void OnDrop(object sender, DragEventArgs e)
    {
        StopDrag();
        ClearIndicators();

        if (!IsRowDrag(e))
        {
            return;
        }

        var target = Resolve(sender as Visual, e.GetPosition((IInputElement)sender), Dragged(e), out var after);
        var moved = Dragged(e);

        if (target is not null && moved is not null)
        {
            Dropped?.Invoke(moved, target, after);
        }

        e.Handled = true;
    }

    /// <summary>最後の DragOver の点の下の行に落とし先の線を付ける。付けた行を返す。</summary>
    private ReorderableRow? ShowIndicatorAtLastPoint()
    {
        var target = Resolve(_overSender, _overPoint, _overDragged, out var after);

        // 全部を消してから付けると、同じ行の線が1回の中で消えて付き直し、行ごとに並べ直しが起きる。付ける行は触らない
        foreach (var row in _rows())
        {
            if (!ReferenceEquals(row, target))
            {
                row.ClearDropIndicator();
            }
        }

        if (target is not null)
        {
            target.DropBefore = !after;
            target.DropAfter = after;
        }

        return target;
    }

    /// <summary>
    /// 落とす先の行と、その上か下かを返す。行の下半分なら「後ろ」。
    /// 同じ並びのものどうしでしか動かせない（<see cref="ReorderableRow.ReorderGroup"/>。タグのトップとサブは別の並び）。
    /// </summary>
    /// <param name="point">sender の左上から数えた点</param>
    private static ReorderableRow? Resolve(Visual? sender, Point point, ReorderableRow? dragged, out bool after)
    {
        after = false;

        var hit = sender is null ? null : VisualTreeHelper.HitTest(sender, point)?.VisualHit;
        var target = RowUnder(hit);

        if (target is null
            || dragged is null
            || !Equals(dragged.ReorderGroup, target.ReorderGroup)
            || ReferenceEquals(dragged, target))
        {
            return null;
        }

        if (Container(hit) is FrameworkElement container && sender is not null)
        {
            after = sender.TransformToDescendant(container)?.Transform(point).Y > container.ActualHeight / 2;
        }

        return target;
    }

    private static ReorderableRow? Dragged(DragEventArgs e)
    {
        foreach (var format in e.Data.GetFormats())
        {
            if (e.Data.GetData(format) is ReorderableRow row)
            {
                return row;
            }
        }

        return null;
    }

    private static ReorderableRow? RowUnder(DependencyObject? source)
        => (Container(source) as FrameworkElement)?.DataContext as ReorderableRow;

    /// <summary>行の器を探す。ListBoxItemでも、ItemsControlが生む器でも同じように辿れる。</summary>
    private static DependencyObject? Container(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement element && element.DataContext is ReorderableRow)
            {
                // テンプレートの中まで下がっているので、行の一番外側まで上がる
                while (element.Parent is FrameworkElement parent && parent.DataContext == element.DataContext)
                {
                    element = parent;
                }

                return element;
            }

            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return null;
    }

    private void ClearIndicators()
    {
        foreach (var row in _rows())
        {
            row.ClearDropIndicator();
        }
    }
}
