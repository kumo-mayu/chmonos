using System.Windows.Controls;
using System.Windows.Input;
using BoothAssetManager.App.Controls;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

public partial class AttributeManageView : UserControl
{
    private readonly RowReorder _reorder;

    public AttributeManageView()
    {
        InitializeComponent();

        _reorder = new RowReorder(this, () => Model?.Rows ?? Enumerable.Empty<ReorderableRow>());
        _reorder.Dropped += (moved, target, after) =>
        {
            if (Model is not null && moved is AttributeMasterRow from && target is AttributeMasterRow to)
            {
                _ = Model.MoveAsync(from, to, after);
            }
        };
    }

    private AttributeManageViewModel? Model => DataContext as AttributeManageViewModel;

    private void OnRowsPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _reorder.OnPreviewMouseLeftButtonDown(sender, e);

    private void OnRowsMouseMove(object sender, MouseEventArgs e) => _reorder.OnMouseMove(sender, e);

    private void OnRowsDragOver(object sender, System.Windows.DragEventArgs e) => _reorder.OnDragOver(sender, e);

    private void OnRowsDragLeave(object sender, System.Windows.DragEventArgs e) => _reorder.OnDragLeave(sender, e);

    private void OnRowsDrop(object sender, System.Windows.DragEventArgs e) => _reorder.OnDrop(sender, e);
}
