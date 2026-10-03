using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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

        // 一覧の行の中身は、見えている辺りだけ作る（ViewportHold）。行ができた・入れ替わった・見方が替わったら、並べ終えた所で当て直す
        _hold = new Controls.ViewportHold(ShopScroll, ScrollBody);
        foreach (var list in new ItemsControl[] { CardList, ItemList })
        {
            list.ItemContainerGenerator.StatusChanged += (_, _) => ScheduleHoldUpdate();
            list.ItemContainerGenerator.ItemsChanged += (_, _) => ScheduleHoldUpdate();
            list.IsVisibleChanged += (_, _) => ScheduleHoldUpdate();
        }

        // 1行の見出しの上のホイールも、全体を流す（見出しは流す中身の外に重ねてあるので、そのままでは受け手が無い）
        CompactBar.MouseWheel += (_, e) =>
        {
            ShopScroll.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = CompactBar,
            });
            e.Handled = true;
        };

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

    /// <summary>一覧の幅が変わったら列数を決め直す（行を仮想化の単位にしているため）。幅は全体を流す入れ物の幅（スクロールバーを含む）。</summary>
    private void OnShopAreaSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is ShopViewModel shop)
        {
            shop.SetViewportWidth(e.NewSize.Width);
        }
    }

    // ---- 行の中身を見えている辺りだけ作る ----

    private readonly Controls.ViewportHold _hold;
    private bool _holdPending;

    /// <summary>行の位置は並べ終えてからしか測れない。次の並べ終わり（描く前）に1回当てる。</summary>
    private void ScheduleHoldUpdate()
    {
        if (_holdPending)
        {
            return;
        }

        _holdPending = true;
        LayoutUpdated += OnLayoutUpdatedForHold;
    }

    private void OnLayoutUpdatedForHold(object? sender, EventArgs e)
    {
        LayoutUpdated -= OnLayoutUpdatedForHold;
        _holdPending = false;
        UpdateHold();
    }

    /// <summary>出ている方の一覧の、見えている辺りの控えを外し、遠い行を控えに戻す。</summary>
    private void UpdateHold()
    {
        if (DataContext is ShopViewModel shop)
        {
            _hold.Update(VisibleList(shop));
        }
    }

    /// <summary>
    /// リストの商品は、リストが出ているときに渡す（結び付けにせず、ここで渡す）。仮想化しない一覧は、一度並べられた後は隠れていても、
    /// 項目が来るたびに全部の行を作る（カードで開いた300件の店で、出ていないリストの行を150作っていた。開いた最初の配置ではまだ隠れていない）。
    /// 隠れている間に商品が替わったら（開いた直後の読み込み・絞り）外し、次に出たときに渡し直す。
    /// 替わらない間は渡したままにして、カード⇄リストの切り替えのたびに行を作り直さない
    /// </summary>
    private void UpdateListSource()
    {
        if (DataContext is not ShopViewModel shop)
        {
            ItemList.ItemsSource = null;
        }
        else if (shop.IsListMode)
        {
            if (!ReferenceEquals(ItemList.ItemsSource, shop.ListItems))
            {
                ItemList.ItemsSource = shop.ListItems;
            }
        }
        else if (ItemList.ItemsSource is not null && !ReferenceEquals(ItemList.ItemsSource, shop.ListItems))
        {
            ItemList.ItemsSource = null;
        }
    }

    private void OnShopPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShopViewModel.ListItems) or nameof(ShopViewModel.IsListMode))
        {
            UpdateListSource();
        }
    }

    // ---- 1行の見出しと、止める行（全体が流れたときに合わせる） ----

    private void OnShopScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdatePinned();
        UpdateHold();

        // 流せる長さを残していた分は、人が流して「縮めても飛ばない」所へ来たら手放す
        if (!_restoring && !_carryPending && e.VerticalChange != 0)
        {
            ReleaseKeptLength();
        }
    }

    private void OnScrollBodySizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePinned();
        ScheduleHoldUpdate();
    }

    private void OnStickyRowSizeChanged(object sender, SizeChangedEventArgs e) => UpdatePinned();

    private bool _stuck;

    /// <summary>
    /// 1行の見出しを出すか・「このショップの商品」の行をどれだけ下げるかを、今の流れの位置から当てる。
    /// どちらも<b>重ねるだけ</b>で、流す中身の大きさは変えない——出入りで中身が動くと、見ていた所が跳ねる
    /// </summary>
    private void UpdatePinned()
    {
        if (DataContext is not ShopViewModel shop)
        {
            return;
        }

        var offset = ShopScroll.VerticalOffset;

        // 名前の段の下端は、流す中身の上からの位置（流れの位置に左右されない）。まだ並んでいない間は測れない
        double nameBottom;
        try
        {
            nameBottom = NameBorder.TransformToAncestor(ScrollBody).Transform(new Point(0, NameBorder.ActualHeight)).Y;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        shop.NoteScrolled(offset, nameBottom);

        // 行が本来いる位置は、並べ終えた枠で見る（下げた分を含めない）
        var rowTop = LayoutInformation.GetLayoutSlot(StickyRow).Top;
        var shift = ShopViewModel.StickyShift(offset, rowTop, ShopViewModel.CompactHeaderHeight);
        StickyShift.Y = shift;

        // 止まっている間だけ、下の商品との境の線を引く。流れてくる商品が行の下に隠れて、境が分からなくなるため
        var stuck = shift > 0;
        if (stuck != _stuck)
        {
            _stuck = stuck;
            if (stuck)
            {
                StickyRow.SetResourceReference(Border.BorderBrushProperty, "Border");
            }
            else
            {
                StickyRow.BorderBrush = Brushes.Transparent;
            }
        }

        // 見出しの右は、スクロールバーの幅だけ空ける
        CompactBar.Margin = new Thickness(0, 0, Math.Max(0, ShopScroll.ActualWidth - ShopScroll.ViewportWidth), 0);

        // 矢印キーで移ったカードを、重なった帯の下まで流す（InsetScrollViewer）
        ShopScroll.TopInset = (shop.IsHeaderCompact ? ShopViewModel.CompactHeaderHeight : 0) + (stuck ? StickyRow.ActualHeight : 0);
    }

    // ---- 見方・絞り・列数が変わっても、見ていた商品を同じ高さに残す（ユーザ判断 2026-10-03：画面を跳ねさせない） ----
    // 一覧は全体のスクロールの中に1つだけ置いているので、カード⇄リスト（高さがまるで違う）や絞りで中身の高さが変わると、
    // 流れの位置はそのままでも見ている物が別の物へ飛ぶ。変える直前に先頭に見えていた商品とそのずれを控え、
    // 並べ直した後（描く前）に同じ高さへ戻す。中身が縮んで流せる長さが足りなくなると戻せないので、見方・絞りが変わるときは
    // 一覧の入れ物の高さを今のまま残しておく（人が流して、縮めても飛ばない所へ来たら手放す）

    private ListAnchor? _carry;
    private double _carryOffset;
    private bool _carryPending;
    private bool _restoring;

    private void OnListAboutToChange(bool viewChanges)
    {
        if (_carryPending || DataContext is not ShopViewModel shop)
        {
            // 続けて変わるときは、最初に控えた物を使う（2回目に読むと、1回目で跳ねた後の位置を控えてしまう）
            return;
        }

        _carry = ListScrollAnchor.Capture(ShopScroll, VisibleList(shop), KeyOf);
        _carryOffset = ShopScroll.VerticalOffset;
        if (viewChanges && _carryOffset > 0 && ListHost.ActualHeight > ListHost.MinHeight)
        {
            ListHost.MinHeight = ListHost.ActualHeight;
        }

        // 並べ直しは描く直前に済む。それより先（DataBind）に戻せば、跳ねた1コマが画面に出ない
        _carryPending = true;
        Dispatcher.InvokeAsync(RestoreCarry, DispatcherPriority.DataBind);
    }

    private void RestoreCarry()
    {
        _carryPending = false;
        if (DataContext is not ShopViewModel shop)
        {
            return;
        }

        _restoring = true;
        try
        {
            ShopScroll.UpdateLayout();
            if (_carry is not { } anchor)
            {
                ShopScroll.ScrollToVerticalOffset(_carryOffset);
            }
            else if (!ListScrollAnchor.Restore(ShopScroll, VisibleList(shop), anchor, KeysOf))
            {
                // 見ていた商品が絞りで無くなった。商品の先頭（止めた行のすぐ下）へ。上の段が見えているときはそのまま
                var listTop = LayoutInformation.GetLayoutSlot(StickyRow).Top - ShopViewModel.CompactHeaderHeight;
                ShopScroll.ScrollToVerticalOffset(Math.Min(_carryOffset, Math.Max(0, listTop)));
            }

            // 流れた知らせ（ScrollChanged）は次の配置で来る。戻している間のうちに出し切る
            ShopScroll.UpdateLayout();
        }
        finally
        {
            _restoring = false;
            _carry = null;
        }
    }

    /// <summary>
    /// 残していた一覧の高さを手放す。手放すと自然な高さまで縮み、流れの位置がその外にあると押し戻されて跳ねる。
    /// 今の位置が自然な高さの中に収まるときだけ手放す
    /// </summary>
    private void ReleaseKeptLength()
    {
        if (ListHost.MinHeight <= 0)
        {
            return;
        }

        var natural = ListHost.Children.OfType<UIElement>().Select(child => child.DesiredSize.Height).DefaultIfEmpty(0).Max();
        var bodyNatural = ScrollBody.ActualHeight - ListHost.ActualHeight + natural;
        if (ShopScroll.VerticalOffset + ShopScroll.ViewportHeight <= bodyNatural + 0.5)
        {
            ListHost.ClearValue(MinHeightProperty);
        }
    }

    // ---- 戻ったときの一覧の位置（ユーザ判断 2026-09-28） ----
    // 同じ型の画面が続くと View は使い回されるので、Loaded ではなく DataContext の付け替えで結び直す

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ShopViewModel old)
        {
            old.ListReady -= OnListReady;
            old.ListAboutToChange -= OnListAboutToChange;
            old.PropertyChanged -= OnShopPropertyChanged;
            old.AnchorReader = null;
        }

        if (e.NewValue is ShopViewModel shop)
        {
            shop.AnchorReader = () => ListScrollAnchor.Capture(ShopScroll, VisibleList(shop), KeyOf);
            shop.ListReady += OnListReady;
            shop.ListAboutToChange += OnListAboutToChange;
            shop.PropertyChanged += OnShopPropertyChanged;

            // 前の店のリストの行を持ち越さない
            UpdateListSource();

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
            _restoring = true;
            try
            {
                ListScrollAnchor.Restore(ShopScroll, VisibleList(shop), anchor, KeysOf);
                ShopScroll.UpdateLayout();
            }
            finally
            {
                _restoring = false;
            }
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
