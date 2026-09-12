using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

public partial class ShopView : UserControl
{
    public ShopView()
    {
        InitializeComponent();
    }

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ItemCardViewModel card }
            && DataContext is ShopViewModel shop)
        {
            shop.OpenItem(card);
        }
    }

    /// <summary>一覧の幅が変わったら列数を決め直す（行を仮想化の単位にしているため）。</summary>
    private void OnListSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is ShopViewModel shop)
        {
            shop.SetViewportWidth(e.NewSize.Width);
        }
    }
}
