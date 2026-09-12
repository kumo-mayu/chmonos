using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using BoothAssetManager.App.ViewModels;

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
    /// **畳んだ見出しの中を選んだなら開く。**畳んだままだと選んだ物が見えない。
    /// 見た目だけの話なので、ViewModel には持たせない
    /// </summary>
    private void AvatarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list || list.SelectedItem is not { } selected)
        {
            return;
        }

        if (CollectionViewSource.GetDefaultView(list.ItemsSource)?.Groups is { } groups)
        {
            foreach (var group in groups.OfType<CollectionViewGroup>())
            {
                if (group.Items.Contains(selected)
                    && list.ItemContainerGenerator.ContainerFromItem(group) is GroupItem container
                    && FindChild<Expander>(container) is { IsExpanded: false } expander)
                {
                    expander.IsExpanded = true;
                }
            }
        }

        list.ScrollIntoView(selected);
    }

    /// <summary>見出しが作られたら、覚えている畳み方に合わせる（行を使い回すので、作られるたびに合わせ直す）。</summary>
    private void GroupExpander_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander { DataContext: CollectionViewGroup { Name: string name } } expander)
        {
            expander.IsExpanded = !AvatarsViewModel.IsGroupCollapsed(name);
        }
    }

    private void GroupExpander_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is Expander { DataContext: CollectionViewGroup { Name: string name } } expander)
        {
            AvatarsViewModel.SetGroupCollapsed(name, !expander.IsExpanded);
        }
    }

    private static T? FindChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found)
            {
                return found;
            }

            if (FindChild<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }
}
