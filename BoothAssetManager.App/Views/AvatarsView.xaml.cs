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

        // 一覧の項目の押下は、開いた一覧（別の窓）から ComboBox まで上がってくる。ComboBox 自身が処理済みにするので、処理済みでも受ける
        BaseCombo.AddHandler(PreviewMouseLeftButtonUpEvent, new System.Windows.Input.MouseButtonEventHandler(BaseCombo_ItemClicked), true);

        // 編集できる ComboBox は、一覧を開いているときの Enter を自分の PreviewKeyDown（クラスの処理）で処理済みにして閉じる。処理済みでも受ける
        BaseCombo.AddHandler(PreviewKeyDownEvent, new System.Windows.Input.KeyEventHandler(BaseCombo_PreviewKeyDown), true);
    }

    /// <summary>
    /// 共通素体の一覧をマウスで押したときだけ、そのまま入れる。
    ///
    /// 選んだ項目（SelectedItem）で入れていたときは、矢印キーで候補を見ているだけでも次々に入れ替わった
    /// （ユーザ指示 2026-09-17：勝手に入らないように）。キーで選んだときは Enter で入れる
    /// </summary>
    private void BaseCombo_ItemClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var item = FindAncestor<ComboBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.Content is not string name || DataContext is not AvatarsViewModel vm)
        {
            return;
        }

        // 押した項目が欄に入るのは押下の処理の後なので、名前を直接渡してから入れる
        vm.BaseInput = name;
        vm.SetBaseCommand.Execute(null);
    }

    /// <summary>
    /// Enter で入れる。一覧を開いているときの Enter は ComboBox が閉じるのに使ってしまい、KeyBinding まで届かなかった
    /// </summary>
    private void BaseCombo_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter || DataContext is not AvatarsViewModel vm)
        {
            return;
        }

        vm.BaseInput = BaseCombo.Text;
        vm.SetBaseCommand.Execute(null);
        BaseCombo.IsDropDownOpen = false;
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T)
        {
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }

        return node as T;
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
