using System.Windows.Controls;

namespace Chmonos.App.Views;

/// <summary>
/// 説明文に載っている YouTube の動画の欄。商品ページと編集画面で同じ部品を使う
/// （ユーザ判断 2026-09-29：編集画面には動画を出す所が無かった）。
/// </summary>
public partial class ItemVideosPanel : UserControl
{
    public ItemVideosPanel()
    {
        InitializeComponent();
    }
}
