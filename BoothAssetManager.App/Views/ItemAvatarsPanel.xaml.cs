using System.Windows.Controls;

namespace BoothAssetManager.App.Views;

/// <summary>
/// 対応アバターの欄。商品ページと編集画面で同じ部品を使う（ユーザ判断：JSONに関わる編集は両方で同等にする）。
/// </summary>
public partial class ItemAvatarsPanel : UserControl
{
    public ItemAvatarsPanel()
    {
        InitializeComponent();
    }
}
