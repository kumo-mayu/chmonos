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

    /// <summary>
    /// 送っていた所へ返すのを諦めるまで。カードが出来上がるのを待つが、
    /// 待ち続けると（絞り込みが変わって届かない場合に）ユーザの操作を覚え直せないままになる
    /// </summary>
    private static readonly TimeSpan RestoreGiveUp = TimeSpan.FromSeconds(5);

    private readonly System.Windows.Threading.DispatcherTimer _settleTimer = new() { Interval = SettleDelay };
    private DateTime _lastScrollAt;

    /// <summary>送っていた所へ返している最中か。戻したぶんの知らせを覚え直さないための印。</summary>
    private bool _restoringScroll;

    /// <summary>一覧が出来上がったか。出来上がる前に届く「先頭にいる」で、覚えていた位置を潰さないための印。</summary>
    private bool _ready;

    private double _restoreTarget;
    private DateTime _restoreUntil;

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

        _settleTimer.Tick += (_, _) => SettleFastScrolling();

        // 落ち着き待ちの間に画面を移ると、時計が鳴る頃には DataContext が外れていて解除が素通りしていた。
        // 印は絵の読み手（ThumbnailLoader）に共有なので、残るとほかの画面の絵まで粗いまま止まる。離れるときに下ろす
        Unloaded += (_, _) => SettleFastScrolling();
    }

    /// <summary>速く流している印を立てた検索の画面。DataContext が外れた後でも印を下ろせるように、立てた相手を覚える。</summary>
    private SearchViewModel? _fastScrolling;

    private void SettleFastScrolling()
    {
        _settleTimer.Stop();
        if (_fastScrolling is { } search)
        {
            _fastScrolling = null;
            search.SetFastScrolling(false);
        }
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

        // 戻している最中は、覚え直しも速さの判定もしない。まだ実体化していない行のせいで
        // 小さく丸められた値を覚えてしまうと、戻し切る前に目標そのものが壊れる
        if (_restoringScroll)
        {
            ContinueRestore((ScrollViewer)e.OriginalSource, e);
            return;
        }

        // **画面を作り直している途中の知らせは覚えない。**行き来のたびに View は作り直され、
        // その途中に「先頭にいる（0）」という知らせが Loaded より前に届く。
        // これを覚えてしまうと、送っていた位置が毎回 0 で潰れる（最初にそう書いて、戻らなかった）
        if (!_ready)
        {
            return;
        }

        if (sender is ListView)
        {
            search.ListScrollOffset = e.VerticalOffset;
        }
        else if (sender is ListBox)
        {
            search.CardScrollOffset = e.VerticalOffset;
        }

        // 速く流しているかを見る（U12・U27）。間が0.5秒より空いたら、そこから流し始めたとみなして速さは測らない
        var now = DateTime.UtcNow;
        var seconds = (now - _lastScrollAt).TotalSeconds;
        _lastScrollAt = now;

        if (e.VerticalChange != 0 && seconds is > 0 and < 0.5
            && Math.Abs(e.VerticalChange) / seconds > FastScrollDipPerSecond)
        {
            search.SetFastScrolling(true);
            _fastScrolling = search;
        }

        _settleTimer.Stop();
        _settleTimer.Start();
    }

    /// <summary>
    /// 送っていた所へ返す（ユーザ指示 2026-09-21：「検索に戻る時など、スクロール位置も保存しておかないといけない」）。
    ///
    /// 条件も並びも ViewModel に残るのに、足元だけ先頭へ戻っていた。
    /// 主画面の `ContentControl` が行き来のたびに View を作り直すので、
    /// スクロール位置は View と一緒に捨てられる（<see cref="SearchViewModel.CardScrollOffset"/>）。
    ///
    /// **一度で決まらない。**一覧は仮想化していて、カードは4枚ずつ後から作られる（<see cref="DeferredCardHost"/>）。
    /// 読み込んだ直後は実体化した行のぶんしか高さが無いので、送れる範囲が目標より手前で止まる。
    /// 短い間に何度やり直しても、その間は高さが伸びていないので届かない（最初にそう書いて、戻らなかった）。
    /// **高さが伸びた知らせ（<c>ExtentHeightChange</c>）に乗せて送り直す。**
    /// 伸びなくなったら諦める（絞り込みが変わって件数が減っていれば、そもそも届かない）ので、
    /// <see cref="RestoreGiveUp"/> を過ぎたら印を降ろして普通の覚え直しに戻す。
    /// </summary>
    private void OnResultsLoaded(object sender, RoutedEventArgs e)
    {
        var search = Model;

        // 覚えていた位置は、印を立てる**前に**読む。立てた後だと、2つの一覧の Loaded の間に
        // 届いた「先頭にいる」で潰れたものを読むことになる
        var wanted = sender is ListView ? search?.ListScrollOffset ?? 0 : search?.CardScrollOffset ?? 0;

        // ここから先の知らせは、画面が出来上がった後の本物
        _ready = true;

        if (sender is not FrameworkElement list || list.Visibility != Visibility.Visible
            || search is null || wanted <= 0)
        {
            return;
        }

        _restoreTarget = wanted;
        _restoringScroll = true;
        _restoreUntil = DateTime.UtcNow + RestoreGiveUp;

        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            () =>
            {
                if (FindChild<ScrollViewer>(list) is { } viewer)
                {
                    viewer.ScrollToVerticalOffset(_restoreTarget);
                }
                else
                {
                    _restoringScroll = false;
                }
            });
    }

    /// <summary>高さが伸びたら、その分だけ目標へ近づける。届いたか、諦める時刻を過ぎたら印を降ろす。</summary>
    private void ContinueRestore(ScrollViewer viewer, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.VerticalOffset - _restoreTarget) <= 1 || DateTime.UtcNow > _restoreUntil)
        {
            _restoringScroll = false;
            return;
        }

        // 伸びていないのに送り直しても同じ所で止まるだけ。伸びた知らせのときだけ送る
        if (e.ExtentHeightChange != 0)
        {
            viewer.ScrollToVerticalOffset(_restoreTarget);
        }
    }

    private static T? FindChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T found)
            {
                return found;
            }

            if (FindChild<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }
}
