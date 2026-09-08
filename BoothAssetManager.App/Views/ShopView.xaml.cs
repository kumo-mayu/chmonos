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
}
