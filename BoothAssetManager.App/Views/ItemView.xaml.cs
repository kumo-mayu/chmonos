using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

public partial class ItemView : UserControl
{
    public ItemView()
    {
        InitializeComponent();
    }

    /// <summary>メイン画像上の横位置に応じてギャラリーを切り替える。設定でオフにできる。</summary>
    private void OnGalleryMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element || DataContext is not ItemViewModel item)
        {
            return;
        }

        if (element.ActualWidth <= 0)
        {
            return;
        }

        item.ShowImageAt(e.GetPosition(element).X / element.ActualWidth, element.ActualWidth);
    }
}
