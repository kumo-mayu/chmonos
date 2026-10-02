using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>
/// 商品のカードの見た目と、カードの上の操作（検索画面から移した。中身は変えていない）。
/// 操作は、カードの Tag に入れた入れ物の画面（<see cref="IItemCardHost"/>）へ返す。
/// </summary>
public partial class ItemCardResources : ResourceDictionary
{
    public ItemCardResources()
    {
        InitializeComponent();
    }

    /// <summary>
    /// カードを並べている画面。カードの枠の Tag に入れてある（ContextMenu が視覚ツリーの外に出るので、元から Tag に置いていた）。
    /// カードの中の部品から押されたときは、枠まで遡って探す。
    /// </summary>
    private static IItemCardHost? HostOf(object sender)
    {
        for (var node = sender as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement { Tag: IItemCardHost host })
            {
                return host;
            }
        }

        return null;
    }

    /// <summary>サムネイル上の横位置に応じて、そのitemのギャラリー画像を切り替える。</summary>
    private void OnThumbnailMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ItemCardViewModel card)
        {
            return;
        }

        if (element.ActualWidth <= 0)
        {
            return;
        }

        card.ShowImageAt(e.GetPosition(element).X / element.ActualWidth, element.ActualWidth);
    }

    /// <summary>
    /// カードのクリック。
    ///
    /// 何も選んでいないときは商品ページへ移る（普段の主操作）。
    /// 1件でも選んでいるときは選択の切り替えにする。選んでいる最中に
    /// 少しずれただけで別画面へ飛ばされると、操作が途切れてしまうため。
    /// </summary>
    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        // マウスで開いたなら、戻ったときに止まり直す必要は無い（止まる印が急に出ると驚く）
        _returnTo = null;
        ActivateCard(sender);
    }

    /// <summary>
    /// 読み上げ・自動操作の「押す」（ItemCardBorder）。カードに止まって Enter を押したのと同じ
    /// （戻ってきたときに止まり直す所まで同じにする。読み上げで開いて戻ると、どのカードにいたかが分からなくなる）
    /// </summary>
    private void OnCardInvoked(object? sender, EventArgs e)
    {
        if (sender is not null)
        {
            RememberForReturn(sender);
            ActivateCard(sender);
        }
    }

    /// <summary>読み上げ・自動操作の「押す」（ItemListView の行）。行に止まって Enter を押したのと同じ</summary>
    private void OnListRowInvoked(object sender, RoutedEventArgs e)
    {
        RememberForReturn(sender);
        ActivateRow(sender);
        e.Handled = true;
    }

    private static void ActivateCard(object sender)
    {
        if (sender is not FrameworkElement { DataContext: ItemCardViewModel card })
        {
            return;
        }

        if (card.IsSelectionMode)
        {
            card.IsSelected = !card.IsSelected;
            return;
        }

        HostOf(sender)?.OpenItem(card);
    }

    /// <summary>
    /// リストの行のクリック（ユーザ指示 2026-09-14）。商品の行はカードと同じ（選んでいる最中は選択の切り替え、ほかは商品ページ）。
    /// フォルダの行（フォルダビュー）はそのフォルダへ移る
    /// </summary>
    private void OnListRowClick(object sender, MouseButtonEventArgs e)
    {
        _returnTo = null;
        ActivateRow(sender);
    }

    private static void ActivateRow(object sender)
    {
        if (sender is FrameworkElement { DataContext: FolderBrowserFolderCard folder })
        {
            if (HostOf(sender) is FolderViewDetail detail)
            {
                detail.OpenFolderCommand.Execute(folder);
            }

            return;
        }

        ActivateCard(sender);
    }

    /// <summary>
    /// キーボードで止まったカード・行の Enter・Space は、押したのと同じ（点検 2026-09-23）。
    /// 中の部品（ボタン）に止まっているときの Enter・Space はその部品の物なので、カード・行そのものに止まっているときだけ受ける
    /// </summary>
    private static bool IsActivateKey(object sender, KeyEventArgs e) =>
        ReferenceEquals(e.OriginalSource, sender)
        && e.Key is Key.Enter or Key.Space
        && Keyboard.Modifiers == ModifierKeys.None;

    private void OnCardKeyDown(object sender, KeyEventArgs e)
    {
        if (IsActivateKey(sender, e))
        {
            RememberForReturn(sender);
            ActivateCard(sender);
            e.Handled = true;
            return;
        }

        // カードは縦横に並ぶので、矢印で隣のカードへ移る。行ごとの一覧（ListBox）に任せると、
        // 行そのものは止まれない作りなので矢印が効かなかった
        // （WPF の方向の移動 MoveFocus は、カードを後から作る入れ物をまたげず動かなかったので、並びの位置から自分で探す）
        var direction = e.Key switch
        {
            Key.Left => (X: -1, Y: 0),
            Key.Right => (X: 1, Y: 0),
            Key.Up => (X: 0, Y: -1),
            Key.Down => (X: 0, Y: 1),
            _ => (X: 0, Y: 0),
        };
        if (direction != (0, 0) && ReferenceEquals(e.OriginalSource, sender) && Keyboard.Modifiers == ModifierKeys.None
            && sender is FrameworkElement element)
        {
            NeighborCard(element, direction.X, direction.Y)?.Focus();
            e.Handled = true;
        }
    }

    /// <summary>
    /// 矢印の向きにある隣のカード。横は同じ段の中でいちばん近いもの、縦は次の段のうち横の位置がいちばん近いもの。
    /// 探すのは同じ一覧（いちばん近い ListBox・無ければ ItemsControl）の中の、今作られているカードだけ
    /// </summary>
    private static FrameworkElement? NeighborCard(FrameworkElement from, int dx, int dy)
    {
        DependencyObject? root = null;
        for (var node = VisualTreeHelper.GetParent(from); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ListBox)
            {
                root = node;
                break;
            }

            root ??= node as ItemsControl;
        }

        if (root is not Visual scope)
        {
            return null;
        }

        var here = from.TransformToAncestor(scope).TransformBounds(new Rect(from.RenderSize));
        FrameworkElement? best = null;
        var bestScore = double.MaxValue;
        foreach (var card in Descendants(scope).OfType<Controls.ItemCardBorder>())
        {
            if (ReferenceEquals(card, from) || !card.IsVisible || !card.Focusable)
            {
                continue;
            }

            var there = card.TransformToAncestor(scope).TransformBounds(new Rect(card.RenderSize));
            var sameRow = Math.Abs(there.Top - here.Top) < here.Height / 2;
            double score;
            if (dx != 0)
            {
                var gap = (there.Left - here.Left) * dx;
                if (!sameRow || gap <= 0)
                {
                    continue;
                }

                score = gap;
            }
            else
            {
                var gap = (there.Top - here.Top) * dy;
                if (sameRow || gap <= 0)
                {
                    continue;
                }

                // 近い段を先に、同じ段の中では横の位置が近い方
                score = gap * 10000 + Math.Abs(there.Left - here.Left);
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = card;
            }
        }

        return best;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    private void OnListRowKeyDown(object sender, KeyEventArgs e)
    {
        if (IsActivateKey(sender, e))
        {
            RememberForReturn(sender);
            ActivateRow(sender);
            e.Handled = true;
        }
    }

    /// <summary>
    /// キーで開いたカード・行。戻ってきた画面は作り直されるので、止まっていた所が分からなくなり、
    /// Tab を頭から押し直すことになった。開いた物（画面の間で同じ物が残る）を覚えておき、作り直された部品がそれなら止まり直す。
    /// 画面が弱い参照で持つだけにし、覚えた物が一覧から消えても残さない
    /// </summary>
    private static WeakReference<object>? _returnTo;

    private static void RememberForReturn(object sender) =>
        _returnTo = sender is FrameworkElement { DataContext: { } data } ? new WeakReference<object>(data) : null;

    private void OnCardLoaded(object sender, RoutedEventArgs e)
    {
        if (_returnTo is null
            || !_returnTo.TryGetTarget(out var target)
            || sender is not FrameworkElement { DataContext: { } data } element
            || !ReferenceEquals(data, target))
        {
            return;
        }

        // 画面を差し替えた直後は、一覧そのものなどへ止まり先が動く。落ち着いてから止まり直す
        element.Dispatcher.BeginInvoke(
            () => RestoreFocus(element, target),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private static void RestoreFocus(FrameworkElement element, object target)
    {
        if (_returnTo is null || !_returnTo.TryGetTarget(out var still) || !ReferenceEquals(still, target) || !element.IsLoaded)
        {
            return;
        }

        // 戻る間に別の所へ止まっていれば（マウスで入力欄を押したなど）、そちらを奪わない。
        // 止まり先が無い・窓そのもの・この部品を含む一覧そのもの、のときだけ止まり直す
        var focused = Keyboard.FocusedElement as DependencyObject;
        var free = focused is null or Window
            || (focused is ItemsControl list && list.IsAncestorOf(element));
        if (!free)
        {
            _returnTo = null;
            return;
        }

        // 隠れている方（カードとリストの両方に同じ物がある）には止まれないので、覚えたまま見える方を待つ
        if (element.Focus())
        {
            _returnTo = null;
        }
    }

    /// <summary>お気に入りの星（#70）。カードのクリックへは流さない——流すと商品ページへ移ってしまう。</summary>
    private void OnFavoriteClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ItemCardViewModel card } && HostOf(sender) is { } host)
        {
            host.ToggleFavoriteAsync(card).Forget();
            e.Handled = true;
        }
    }

    /// <summary>読み上げ・自動操作から星を押した（PressableBorder）。マウスで押したのと同じ</summary>
    private void OnFavoriteInvoked(object? sender, EventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ItemCardViewModel card } && HostOf(sender) is { } host)
        {
            host.ToggleFavoriteAsync(card).Forget();
        }
    }

    /// <summary>右クリックのメニューの項目から、押されたカードと、カードを並べている画面を取り出す（メニューは画面の木の外に出る）。</summary>
    /// <remarks>
    /// 行（アバター・改変のリストなど <see cref="IHasItemCard"/>）から開いたメニューは、行の持つカードを宛先にする
    /// （メモ9-④ 2026-10-02：アバターのカードの右クリックにお気に入りが無く、星のある検索のカードとできることが分かれていた）
    /// </remarks>
    private static (ItemCardViewModel Card, IItemCardHost Host)? MenuTarget(object sender)
        => sender is MenuItem item
            && (item.DataContext as ItemCardViewModel ?? (item.DataContext as IHasItemCard)?.Card) is { } card
            && ItemsControl.ItemsControlFromItemContainer(item) is ContextMenu { PlacementTarget: { } target }
            && HostOf(target) is { } host
                ? (card, host)
                : null;

    /// <summary>右クリックのメニューの「お気に入りに入れる／外す」（キーボードから星へ届く道。Shift+F10）。</summary>
    private void OnMenuFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (MenuTarget(sender) is { } target)
        {
            target.Host.ToggleFavoriteAsync(target.Card).Forget();
        }
    }

    /// <summary>右クリックのメニューの「商品ページを開く」（選んでいる最中の「中を見る」と同じ）。</summary>
    private void OnMenuOpenItemClick(object sender, RoutedEventArgs e)
    {
        if (MenuTarget(sender) is { } target)
        {
            // キー（Shift+F10 のメニューを Enter）で開いたなら、戻ったらこのカードに止まり直す。マウスで押したならしない（カードのクリックと同じ）
            _returnTo = null;
            if (InputManager.Current.MostRecentInputDevice is KeyboardDevice)
            {
                RememberForReturn(sender);
            }

            target.Host.OpenItem(target.Card);
        }
    }

    /// <summary>選択中でも商品ページへ移れる出口。</summary>
    private void OnOpenItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ItemCardViewModel card } && HostOf(sender) is { } host)
        {
            host.OpenItem(card);
            e.Handled = true;
        }
    }

    /// <summary>読み上げ・自動操作から「中を見る」を押した（PressableBorder）。マウスで押したのと同じ</summary>
    private void OnOpenItemInvoked(object? sender, EventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ItemCardViewModel card } && HostOf(sender) is { } host)
        {
            host.OpenItem(card);
        }
    }

    /// <summary>
    /// 中クリックでBOOTHを開く近道。
    /// 知っている人だけが使うので、カードに出口を増やさずに済む（右クリックにも同じ項目がある）。
    /// </summary>
    private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle
            || sender is not FrameworkElement { DataContext: ItemCardViewModel card }
            || HostOf(sender) is not { } host)
        {
            return;
        }

        host.OpenBooth(card);
        e.Handled = true;
    }

    private void OnThumbnailMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ItemCardViewModel card })
        {
            card.ResetImage();
        }
    }
}
