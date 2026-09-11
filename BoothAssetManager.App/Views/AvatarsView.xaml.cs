using System.Windows.Controls;

namespace BoothAssetManager.App.Views;

public partial class AvatarsView : UserControl
{
    public AvatarsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 選んだアバターを一覧の見える所まで流す。
    ///
    /// 共通素体の中のアバターや商品ページの札から来たとき、選択は変わっても一覧は動かないので、
    /// 400体の中のどれが選ばれているのかが見えなかった（U16）。
    /// 見た目だけの話なので、ViewModel には持たせない
    /// </summary>
    private void AvatarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && list.SelectedItem is { } selected)
        {
            list.ScrollIntoView(selected);
        }
    }
}
