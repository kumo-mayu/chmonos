using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

public partial class ShopsView : UserControl
{
    public ShopsView()
    {
        InitializeComponent();
    }

    /// <summary>カードのどこを押しても開く。中に押せるものが無いので、全体を的にする。</summary>
    private void OnShopClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ShopCardViewModel card })
        {
            card.OpenCommand?.Execute(null);
        }
    }
}
