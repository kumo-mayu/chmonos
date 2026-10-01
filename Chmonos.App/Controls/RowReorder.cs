using System.Windows;
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

    private Point _pressedAt;
    private ReorderableRow? _pressedRow;

    public RowReorder(FrameworkElement scope, Func<IEnumerable<ReorderableRow>> rows)
    {
        _scope = scope;
        _rows = rows;
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

        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, row, DragDropEffects.Move);
        }
        finally
        {
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

        var target = Resolve(sender, e, out var after);

        ClearIndicators();

        if (target is null)
        {
            e.Effects = DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.Move;
            target.DropBefore = !after;
            target.DropAfter = after;
        }

        e.Handled = true;
    }

    public void OnDragLeave(object sender, DragEventArgs e) => ClearIndicators();

    public void OnDrop(object sender, DragEventArgs e)
    {
        ClearIndicators();

        if (!IsRowDrag(e))
        {
            return;
        }

        var target = Resolve(sender, e, out var after);
        var moved = Dragged(e);

        if (target is not null && moved is not null)
        {
            Dropped?.Invoke(moved, target, after);
        }

        e.Handled = true;
    }

    /// <summary>
    /// 落とす先の行と、その上か下かを返す。行の下半分なら「後ろ」。
    /// 同じ並びのものどうしでしか動かせない（<see cref="ReorderableRow.ReorderGroup"/>。タグのトップとサブは別の並び）。
    /// </summary>
    private ReorderableRow? Resolve(object sender, DragEventArgs e, out bool after)
    {
        after = false;

        var element = sender as IInputElement;
        var hit = element is null
            ? null
            : VisualTreeHelper.HitTest((Visual)sender, e.GetPosition(element))?.VisualHit;

        var target = RowUnder(hit);
        var dragged = Dragged(e);

        if (target is null
            || dragged is null
            || !Equals(dragged.ReorderGroup, target.ReorderGroup)
            || ReferenceEquals(dragged, target))
        {
            return null;
        }

        if (Container(hit) is FrameworkElement container)
        {
            after = e.GetPosition(container).Y > container.ActualHeight / 2;
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
