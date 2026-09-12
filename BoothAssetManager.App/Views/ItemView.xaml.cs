using System.Windows.Controls;

namespace BoothAssetManager.App.Views;

/// <summary>
/// 商品ページ。画像・対応アバター・手元のファイルの欄は、編集画面と共有する部品
/// （<see cref="ItemGalleryPanel"/>・<see cref="ItemAvatarsPanel"/>・<see cref="ItemFilesPanel"/>）。
/// </summary>
public partial class ItemView : UserControl
{
    public ItemView()
    {
        InitializeComponent();
    }
}
