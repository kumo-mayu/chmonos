using System.Windows;
using System.Windows.Controls;

namespace BoothAssetManager.App.Views;

/// <summary>
/// 手元のファイルの欄。商品ページと編集画面で同じ部品を使う（ユーザ判断：JSONに関わる編集は両方で同等にする）。
/// 編集画面では「使う」操作（Unityへ送る・展開して開く）を出さない（<see cref="ViewModels.ItemViewModel.ShowsUseActions"/>）。
/// </summary>
public partial class ItemFilesPanel : UserControl
{
    public ItemFilesPanel()
    {
        InitializeComponent();
    }

    /// <summary>「開く ▾」「Unityへ送る ▾」（ユーザ判断 2026-09-19：右端に並んだボタンをまとめ、名前のすぐ横に置く）。</summary>
    private void OnMenuButtonClick(object sender, RoutedEventArgs e) => Controls.MenuButton.OpenBelow(sender, e);
}
