using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
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
        if (e.OriginalSource is ScrollViewer && DataContext is SearchViewModel search)
        {
            search.IsScrolledDown = e.VerticalOffset > 0;
        }
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

        if (DataContext is SearchViewModel search)
        {
            search.OpenItem(card);
        }
    }

    /// <summary>お気に入りの星（#70）。カードのクリックへは流さない——流すと商品ページへ移ってしまう。</summary>
    private void OnFavoriteClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ItemCardViewModel card }
            && DataContext is SearchViewModel search)
        {
            _ = search.ToggleFavoriteAsync(card);
            e.Handled = true;
        }
    }

    /// <summary>選択中でも商品ページへ移れる出口。</summary>
    private void OnOpenItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ItemCardViewModel card }
            && DataContext is SearchViewModel search)
        {
            search.OpenItem(card);
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
            || DataContext is not SearchViewModel search)
        {
            return;
        }

        search.OpenBooth(card);
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
