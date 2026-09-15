using System.Windows;
using System.Windows.Controls;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

/// <summary>ショップ1件の画面。カードとリストの上の操作は <see cref="ItemCardResources"/> が持つ（検索画面と同じ）。</summary>
public partial class ShopView : UserControl
{
    public ShopView()
    {
        InitializeComponent();
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
