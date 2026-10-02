using System.Windows.Controls;
using System.Windows.Input;
using Chmonos.App.Controls;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

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
                Model.MoveAsync(from, to, after).Forget();
            }
        };
    }

    private AttributeManageViewModel? Model => DataContext as AttributeManageViewModel;

    /// <summary>
    /// 一覧の見える幅から、商品を並べられる幅までの差（XAML の枠：商品を囲む札の線 1×2・札の中の余白 20×2・
    /// 開いた印の線までの 11・線 1・線から中身までの 19）。前はこの幅の WrapPanel に並べていたので、同じ幅で段に切る
    /// </summary>
    private const double ItemsChrome = 1 * 2 + 20 * 2 + 11 + 1 + 19;

    /// <summary>右の一覧の幅が変わったら、段の数を決め直す（段を仮想化の単位にしているため）。</summary>
    private void OnDetailScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // 中の入力欄（メモ・探す欄）の ScrollViewer からも上がってくるので、一覧そのものの物だけを見る
        if (e.ViewportWidthChange != 0
            && e.OriginalSource is ScrollViewer { TemplatedParent: var owner }
            && ReferenceEquals(owner, sender))
        {
            Model?.SetItemsWidth(e.ViewportWidth - ItemsChrome);
        }
    }

    private void OnRowsPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => RunIfManual(() => _reorder.OnPreviewMouseLeftButtonDown(sender, e));

    private void OnRowsMouseMove(object sender, MouseEventArgs e) => RunIfManual(() => _reorder.OnMouseMove(sender, e));

    /// <summary>「候補の並べ替え」のときだけ、行をつかんで動かせる（メモ10-⑤ 2026-10-02）。ほかの並べ方ではつかみも出さない。</summary>
    private void RunIfManual(Action grab)
    {
        if (Model?.SortsManually == true)
        {
            grab();
        }
    }

    private void OnRowsDragOver(object sender, System.Windows.DragEventArgs e) => _reorder.OnDragOver(sender, e);

    private void OnRowsDragLeave(object sender, System.Windows.DragEventArgs e) => _reorder.OnDragLeave(sender, e);

    private void OnRowsDrop(object sender, System.Windows.DragEventArgs e) => _reorder.OnDrop(sender, e);
}
