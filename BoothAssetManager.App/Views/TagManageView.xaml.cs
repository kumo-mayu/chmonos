using System.Windows.Controls;
using System.Windows.Input;
using BoothAssetManager.App.Controls;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

/// <summary>
/// タグの管理。並べ替えのドラッグだけを持ち、実処理は <see cref="RowReorder"/> に寄せている。
/// </summary>
public partial class TagManageView : UserControl
{
    private readonly RowReorder _reorder;

    public TagManageView()
    {
        InitializeComponent();

        _reorder = new RowReorder(this, Rows);
        _reorder.Dropped += (moved, target, after) =>
        {
            if (Model is null)
            {
                return;
            }

            if (moved is TagTopRow movedTop && target is TagTopRow targetTop)
            {
                Model.MoveTopAsync(movedTop, targetTop, after).Forget();
            }
            else if (moved is TagSubRow movedSub && target is TagSubRow targetSub)
            {
                Model.MoveSubAsync(movedSub, targetSub, after).Forget();
            }
        };
    }

    private TagManageViewModel? Model => DataContext as TagManageViewModel;

    private IEnumerable<ReorderableRow> Rows()
        => Model is null ? [] : Model.Tops.Cast<ReorderableRow>().Concat(Model.Subs);

    private void OnRowsPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _reorder.OnPreviewMouseLeftButtonDown(sender, e);

    private void OnRowsMouseMove(object sender, MouseEventArgs e) => _reorder.OnMouseMove(sender, e);

    private void OnRowsDragOver(object sender, System.Windows.DragEventArgs e) => _reorder.OnDragOver(sender, e);

    private void OnRowsDragLeave(object sender, System.Windows.DragEventArgs e) => _reorder.OnDragLeave(sender, e);

    private void OnRowsDrop(object sender, System.Windows.DragEventArgs e) => _reorder.OnDrop(sender, e);
}
