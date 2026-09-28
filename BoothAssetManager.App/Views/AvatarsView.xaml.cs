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

        // 共通素体の欄は候補付きの欄（`SuggestBox`）に替えた（I6）。
        // 編集できる ComboBox の癖（開いている間の Enter を自分で処理済みにする・項目の押下が親まで上がる）を
        // ここで受け直していたが、その手当てごと要らなくなった
    }

    /// <summary>「名前を変更」か名前の候補を押した直後か。欄が出たときに入力を移すかをこれで決める。</summary>
    private bool _focusNameEditor;

    /// <summary>
    /// 名前を欄にするボタンを押した。欄が表示されたら、そこへ入力を移す。
    /// 打ちかけのあるアバターを一覧の矢印キーで選んだときにも欄は出るが、そのときは移さない（一覧の矢印キーが効かなくなる）
    /// </summary>
    private void StartRename_Click(object sender, RoutedEventArgs e) => _focusNameEditor = true;

    private void NameEditor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!_focusNameEditor || e.NewValue is not true || sender is not TextBox box)
        {
            return;
        }

        _focusNameEditor = false;
        // 表示に変わった瞬間はまだ並べ終えておらず、入力を受けられない。並べ終えてから移す
        box.Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            box.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
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
