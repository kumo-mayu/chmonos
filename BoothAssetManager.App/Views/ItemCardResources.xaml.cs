using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

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

    /// <summary>お気に入りの星（#70）。カードのクリックへは流さない——流すと商品ページへ移ってしまう。</summary>
    private void OnFavoriteClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ItemCardViewModel card } && HostOf(sender) is { } host)
        {
            host.ToggleFavoriteAsync(card).Forget();
            e.Handled = true;
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
