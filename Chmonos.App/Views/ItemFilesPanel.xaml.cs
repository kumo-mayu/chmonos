using System.Windows;
using System.Windows.Controls;

namespace Chmonos.App.Views;

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

    /// <summary>
    /// ファイルの行のメニュー（行の右クリックと「開く ▾」は同じメニュー）に、プラグインの <c>file</c> の項目を足す。
    /// 押せるかは開いた行と、その時のソフトの応答で決まるので、開くたびに作り直す
    /// </summary>
    private void OnFileMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu { PlacementTarget: FrameworkElement { DataContext: ViewModels.LocalFileRow row, Tag: ViewModels.ItemViewModel item } target } menu)
        {
            PluginMenuItems.AttachTo(menu, ItemCardResources.PluginMenusOf(target), [Chmonos.Api.PluginMenuSpot.File(item.Item.Id, row.Hash)]);
        }
    }

    /// <summary>unitypackage の行の「Unity ▾」に、プラグインの <c>unityPackage</c> の項目を足す（zip の中・単体の物。フォルダの中の物は zip のハッシュが無い）。</summary>
    private void OnPackageMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu { PlacementTarget: FrameworkElement { DataContext: ViewModels.UnityPackageRow row, Tag: ViewModels.ItemViewModel item } target } menu)
        {
            var fileHash = (row.FileRow as ViewModels.LocalFileRow)?.Hash;
            PluginMenuItems.AttachTo(menu, ItemCardResources.PluginMenusOf(target), [Chmonos.Api.PluginMenuSpot.UnityPackage(item.Item.Id, fileHash, row.Entry.EntryPath)]);
        }
    }
}
