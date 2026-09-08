using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

/// <summary>
/// タグの管理。
///
/// 並べ替えのドラッグだけをここに置く。マウスの位置から「どの行の上か下か」を
/// 決める処理はビューの仕事で、ViewModelに持たせても検証できないため。
/// </summary>
public partial class TagManageView : UserControl
{
    /// <summary>この距離を超えて動いたらドラッグ開始。クリックと区別する。</summary>
    private static readonly double DragThreshold = SystemParameters.MinimumHorizontalDragDistance;

    private Point _pressedAt;
    private ReorderableRow? _pressedRow;

    public TagManageView()
    {
        InitializeComponent();
    }

    private TagManageViewModel? Model => DataContext as TagManageViewModel;

    private void OnRowsPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedAt = e.GetPosition(this);
        _pressedRow = RowUnder(e.OriginalSource as DependencyObject);
    }

    private void OnRowsMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedRow is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var moved = e.GetPosition(this) - _pressedAt;
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

    private void OnRowsDragOver(object sender, DragEventArgs e)
    {
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

    private void OnRowsDragLeave(object sender, DragEventArgs e) => ClearIndicators();

    private void OnRowsDrop(object sender, DragEventArgs e)
    {
        ClearIndicators();

        var target = Resolve(sender, e, out var after);
        if (target is null || Model is null)
        {
            return;
        }

        if (e.Data.GetData(typeof(TagTopRow)) is TagTopRow movedTop && target is TagTopRow targetTop)
        {
            _ = Model.MoveTopAsync(movedTop, targetTop, after);
        }
        else if (e.Data.GetData(typeof(TagSubRow)) is TagSubRow movedSub && target is TagSubRow targetSub)
        {
            _ = Model.MoveSubAsync(movedSub, targetSub, after);
        }

        e.Handled = true;
    }

    /// <summary>
    /// 落とす先の行と、その上か下かを返す。行の下半分なら「後ろ」。
    /// 同じ種類どうしでしか動かせない（トップとサブは別の並び）。
    /// </summary>
    private ReorderableRow? Resolve(object sender, DragEventArgs e, out bool after)
    {
        after = false;

        var element = sender as IInputElement;
        var hit = element is null
            ? null
            : (VisualTreeHelper.HitTest((Visual)sender, e.GetPosition(element))?.VisualHit);

        var target = RowUnder(hit);
        if (target is null)
        {
            return null;
        }

        var dragged = e.Data.GetData(typeof(TagTopRow)) as object ?? e.Data.GetData(typeof(TagSubRow));
        if (dragged is null || dragged.GetType() != target.GetType() || ReferenceEquals(dragged, target))
        {
            return null;
        }

        if (Container(hit) is FrameworkElement container)
        {
            after = e.GetPosition(container).Y > container.ActualHeight / 2;
        }

        return target;
    }

    private static ReorderableRow? RowUnder(DependencyObject? source)
        => (Container(source) as FrameworkElement)?.DataContext as ReorderableRow;

    /// <summary>行の器を探す。トップはListBoxItem、サブはItemsControlが生む器。</summary>
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
        if (Model is null)
        {
            return;
        }

        foreach (var row in Model.Tops)
        {
            row.ClearDropIndicator();
        }

        foreach (var row in Model.Subs)
        {
            row.ClearDropIndicator();
        }
    }
}
