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

    /// <summary>カードをクリックしたら商品ページへ移る。検索画面の状態はそのまま残る。</summary>
    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ItemCardViewModel card })
        {
            return;
        }

        if (DataContext is SearchViewModel search)
        {
            search.OpenItem(card);
        }
    }

    private void OnThumbnailMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ItemCardViewModel card })
        {
            card.ResetImage();
        }
    }
}
