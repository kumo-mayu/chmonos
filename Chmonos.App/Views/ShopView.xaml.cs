using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>ショップ1件の画面。カードとリストの上の操作は <see cref="ItemCardResources"/> が持つ（検索画面と同じ）。</summary>
public partial class ShopView : UserControl
{
    public ShopView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // カードの大きさ（一覧の右下のスライダー）が変わったら列を割り直す。一覧の幅は変わらないので SizeChanged は来ない。
        // 知らせは静的なので、出ている間だけ聞く（離れた画面を掴んだままにしない）
        Loaded += (_, _) =>
        {
            // Loaded は出し直すたびに来る。2重に聞かないよう、外してから付ける
            Services.CardMetrics.Changed -= OnCardSizeChanged;
            Services.CardMetrics.Changed += OnCardSizeChanged;
        };
        Unloaded += (_, _) => Services.CardMetrics.Changed -= OnCardSizeChanged;
    }

    private void OnCardSizeChanged() => (DataContext as ShopViewModel)?.RelayoutForCardSize();

    /// <summary>一覧の幅が変わったら列数を決め直す（行を仮想化の単位にしているため）。</summary>
    private void OnListSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is ShopViewModel shop)
        {
            shop.SetViewportWidth(e.NewSize.Width);
        }
    }

    /// <summary>縮めた行の高さ（ShopView.xaml の縮めた行の Height と揃える）。</summary>
    private const double CompactHeaderHeight = 52;

    /// <summary>
    /// 商品の一覧が流れた。上の段を縮める・戻すは ViewModel が決める（メモ7-⑤）。
    /// 見るのは今出ている一覧そのものの ScrollViewer だけ——リストの見出しの行も自分の ScrollViewer を持ち、
    /// 縦の位置がいつも0なので、それを拾うと縮めた直後に戻してしまう
    /// </summary>
    private void OnListScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (DataContext is not ShopViewModel shop
            || e.OriginalSource is not ScrollViewer scroll
            || !ReferenceEquals(scroll.TemplatedParent, VisibleList(shop)))
        {
            return;
        }

        // 詰め切ると、上の段（バナー・見出し・メモ）が縮めた行1本になる。詰める高さの上限はその差
        if (!shop.IsHeaderCompact)
        {
            _fullHeaderHeight = ShopHeader.ActualHeight;
        }

        shop.NoteListScrolled(scroll.VerticalOffset, scroll.ScrollableHeight, _fullHeaderHeight - CompactHeaderHeight);
        ApplyHeaderShrink(shop);
    }

    // 元の段の高さ。縮めた行に切り替えた後は測れないので、切り替える前の値を持つ
    private double _fullHeaderHeight;

    private void OnHeaderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is ShopViewModel shop)
        {
            if (!shop.IsHeaderCompact)
            {
                _fullHeaderHeight = e.NewSize.Height;
            }

            ApplyHeaderShrink(shop);
        }
    }

    /// <summary>
    /// 詰めた高さを段に当てる。入れ物の高さを詰めた分だけ低くし、中身は上へずらして下端をそろえる（上から切り落ちる）。
    /// 縮めた行に替わった後は元の高さに任せる
    /// </summary>
    private void ApplyHeaderShrink(ShopViewModel shop)
    {
        if (shop.IsHeaderCompact || shop.HeaderShrink <= 0 || _fullHeaderHeight <= 0)
        {
            ShopHeaderClip.ClearValue(HeightProperty);
            ShopHeader.Margin = new Thickness(0);
            return;
        }

        ShopHeaderClip.Height = Math.Max(0, _fullHeaderHeight - shop.HeaderShrink);
        ShopHeader.Margin = new Thickness(0, -shop.HeaderShrink, 0, 0);
    }

    // ---- 戻ったときの一覧の位置（ユーザ判断 2026-09-28） ----
    // 同じ型の画面が続くと View は使い回されるので、Loaded ではなく DataContext の付け替えで結び直す

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ShopViewModel old)
        {
            old.ListReady -= OnListReady;
            old.AnchorReader = null;
        }

        if (e.NewValue is ShopViewModel shop)
        {
            shop.AnchorReader = () => ListScrollAnchor.Capture(VisibleList(shop), KeyOf);
            shop.ListReady += OnListReady;

            // 裏の読み込みが View より先に済むことがある（商品の少ない店だと一瞬）
            if (shop.IsListReady)
            {
                ScheduleRestore();
            }
        }
    }

    /// <summary>カードかリストか、今出ている方の一覧（隠れている方は位置を持たない）。</summary>
    private ItemsControl VisibleList(ShopViewModel shop) => shop.IsListMode ? ItemList : CardList;

    private void OnListReady(object? sender, EventArgs e) => ScheduleRestore();

    /// <summary>
    /// 位置は一覧を組み終えてから当てる。組んだ直後は、幅から列数を決めて行を切り直す分がまだ済んでいない
    /// （開いた直後は1列で組まれる）。Loaded の優先度で待つと、配置の処理が先に全部済む。
    /// </summary>
    private void ScheduleRestore()
    {
        if (!IsLoaded)
        {
            Loaded += RestoreWhenLoaded;
            return;
        }

        Dispatcher.InvokeAsync(Restore, DispatcherPriority.Loaded);
    }

    private void RestoreWhenLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= RestoreWhenLoaded;
        ScheduleRestore();
    }

    private void Restore()
    {
        if (DataContext is ShopViewModel { IsListReady: true } shop && shop.TakePendingAnchor() is { } anchor)
        {
            ListScrollAnchor.Restore(VisibleList(shop), anchor, KeysOf);
        }
    }

    // カードの行は先頭のカード、リストの行は商品そのものを鍵にする。鍵はどちらも商品の ID なので、
    // 離れている間に見方を切り替えても（ショップ画面として覚えている）同じ商品を探し当てられる
    private static string? KeyOf(object entry) => entry switch
    {
        CardRow row when row.Cards.OfType<ItemCardViewModel>().FirstOrDefault() is { } first => first.Item.Id,
        ItemCardViewModel card => card.Item.Id,
        _ => null,
    };

    private static IEnumerable<string> KeysOf(object entry) => entry switch
    {
        CardRow row => row.Cards.OfType<ItemCardViewModel>().Select(card => card.Item.Id),
        ItemCardViewModel card => [card.Item.Id],
        _ => [],
    };
}
