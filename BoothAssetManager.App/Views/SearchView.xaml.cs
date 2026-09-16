using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BoothAssetManager.App.Controls;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

/// <summary>
/// 検索画面。カードの上の操作（押す・星・中クリック・なぞる）は、フォルダビューの右側と共通の
/// <see cref="ItemCardResources"/> にある（2026-09-14 にここから移した）。
/// </summary>
public partial class SearchView : UserControl
{
    /// <summary>
    /// 「速く流している」とみなす速さ（DIP/秒）。カードの行はおよそ250〜300DIPなので、1秒に10行前後。
    /// 探して流しているときはこれを超え、読みながら少しずつ送るときは下回る。仮の値で、触って調整する
    /// </summary>
    private const double FastScrollDipPerSecond = 2500;

    /// <summary>スクロールの知らせがこれだけ途切れたら「止まった」とみなす。</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(200);

    private readonly System.Windows.Threading.DispatcherTimer _settleTimer = new() { Interval = SettleDelay };
    private DateTime _lastScrollAt;

    /// <summary>絞り込みの条件をドラッグで並べ替える。中身はタグ・属性の管理と共通（<see cref="RowReorder"/>）。</summary>
    private readonly RowReorder _reorder;

    public SearchView()
    {
        InitializeComponent();

        _reorder = new RowReorder(this, () => Model?.Modules ?? Enumerable.Empty<ReorderableRow>());
        _reorder.Dropped += (moved, target, after) =>
        {
            if (Model is not null && moved is SearchModule from && target is SearchModule to)
            {
                Model.MoveModule(from, to, after);
            }
        };

        _settleTimer.Tick += (_, _) =>
        {
            _settleTimer.Stop();
            if (DataContext is SearchViewModel search)
            {
                search.SetFastScrolling(false);
            }
        };
    }

    private SearchViewModel? Model => DataContext as SearchViewModel;

    /// <summary>
    /// 条件を掴む。**見出しのつまみだけで掴める**ようにしてある——
    /// カードのどこでも掴めると、スライダを動かすたびに並べ替えが始まってしまう。
    /// </summary>
    private void OnModuleHandlePress(object sender, MouseButtonEventArgs e)
        => _reorder.OnPreviewMouseLeftButtonDown(sender, e);

    private void OnModuleHandleMove(object sender, MouseEventArgs e) => _reorder.OnMouseMove(sender, e);

    private void OnModulesDragOver(object sender, DragEventArgs e) => _reorder.OnDragOver(sender, e);

    private void OnModulesDragLeave(object sender, DragEventArgs e) => _reorder.OnDragLeave(sender, e);

    private void OnModulesDrop(object sender, DragEventArgs e) => _reorder.OnDrop(sender, e);

    /// <summary>
    /// 検索履歴の帯をホイールで横に送る。帯はバーを出さない（ユーザ指示 2026-09-16）ので、ほかに送る手段が無い。
    /// 縦には送らない帯なので、縦のホイールをそのまま横に読み替える。
    /// </summary>
    private void OnHistoryMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer strip && strip.ScrollableWidth > 0)
        {
            strip.ScrollToHorizontalOffset(strip.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }

    /// <summary>検索欄へ入り、今の文字を選んだ状態にする（ショートカット「検索欄へ」#43）。そのまま打てば置き換わる。</summary>
    public void FocusQuery()
    {
        QueryBox.Focus();
        QueryBox.SelectAll();
    }

    /// <summary>
    /// 表示幅が変わったら列数を決め直す。
    /// 仮想化のために結果を行に切っているので、折り返し位置はこちらで計算する必要がある。
    /// </summary>
    private void OnResultsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is SearchViewModel search)
        {
            search.SetViewportWidth(e.NewSize.Width);
        }
    }

    /// <summary>
    /// 一覧を下へ読み進めているかを知らせる（U10）。
    /// 先頭を見ているなら、取り込みで増えた商品を黙って入れてよい。下を読んでいる最中なら
    /// 足元を動かさず「押すと反映」の1行にする。
    /// </summary>
    private void OnResultsScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer || DataContext is not SearchViewModel search)
        {
            return;
        }

        search.IsScrolledDown = e.VerticalOffset > 0;

        // 速く流しているかを見る（U12・U27）。間が0.5秒より空いたら、そこから流し始めたとみなして速さは測らない
        var now = DateTime.UtcNow;
        var seconds = (now - _lastScrollAt).TotalSeconds;
        _lastScrollAt = now;

        if (e.VerticalChange != 0 && seconds is > 0 and < 0.5
            && Math.Abs(e.VerticalChange) / seconds > FastScrollDipPerSecond)
        {
            search.SetFastScrolling(true);
        }

        _settleTimer.Stop();
        _settleTimer.Start();
    }
}
