using System.Windows.Controls;
using System.Windows.Input;
using Chmonos.App.Controls;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

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

    /// <summary>
    /// 一覧の見える幅から、開いた小分類の中の商品を並べられる幅までの差（XAML の枠：小分類を囲む札の線 1×2・
    /// 小分類の行の余白 20×2・字下げ 23・開いた印の線 1・線から中身までの 16）。前はこの幅の WrapPanel に並べていた
    /// </summary>
    private const double ItemsChrome = 1 * 2 + 20 * 2 + 23 + 1 + 16;

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

    private IEnumerable<ReorderableRow> Rows()
        => Model is null ? [] : Model.Tops.Cast<ReorderableRow>().Concat(Model.Subs);

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
