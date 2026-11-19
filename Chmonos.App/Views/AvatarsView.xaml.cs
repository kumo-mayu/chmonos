using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Chmonos.App.Controls;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

public partial class AvatarsView : UserControl
{
    public AvatarsView()
    {
        InitializeComponent();

        // 共通素体の欄は候補付きの欄（`SuggestBox`）に替えた（I6）。
        // 編集できる ComboBox の癖（開いている間の Enter を自分で処理済みにする・項目の押下が親まで上がる）を
        // ここで受け直していたが、その手当てごと要らなくなった

        // 素体の中の並べ替え（ユーザ指示 2026-11-04）。中身はタグ・属性の管理と共通（RowReorder）
        _memberReorder = new Controls.RowReorder(this, () => Model?.SelectedBaseMembers ?? Enumerable.Empty<ReorderableRow>());
        _memberReorder.Dropped += (moved, target, after) =>
        {
            if (Model is not null && moved is AvatarRowViewModel from && target is AvatarRowViewModel to)
            {
                Model.MoveBaseMemberAsync(from, to, after).Forget();
            }
        };
    }

    private readonly Controls.RowReorder _memberReorder;

    private AvatarsViewModel? Model => DataContext as AvatarsViewModel;

    private void OnBaseMembersPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => _memberReorder.OnPreviewMouseLeftButtonDown(sender, e);

    private void OnBaseMembersMouseMove(object sender, System.Windows.Input.MouseEventArgs e) => _memberReorder.OnMouseMove(sender, e);

    private void OnBaseMembersDragOver(object sender, DragEventArgs e) => _memberReorder.OnDragOver(sender, e);

    private void OnBaseMembersDragLeave(object sender, DragEventArgs e) => _memberReorder.OnDragLeave(sender, e);

    private void OnBaseMembersDrop(object sender, DragEventArgs e) => _memberReorder.OnDrop(sender, e);

    /// <summary>「名前を変更」か名前の候補を押した直後か。欄が出たときに入力を移すかをこれで決める。</summary>
    private bool _focusNameEditor;

    /// <summary>
    /// 名前を欄にするボタンを押した。欄が表示されたら、そこへ入力を移す。
    /// 打ちかけのあるアバターを一覧の矢印キーで選んだときにも欄は出るが、そのときは移さない（一覧の矢印キーが効かなくなる）
    /// </summary>
    private void StartRename_Click(object sender, RoutedEventArgs e) => _focusNameEditor = true;

    /// <summary>
    /// 素体の詳細の絵を左に置くと右に残る最低の幅。素体の商品の欄（230）・間（8）・「素体の商品を設定」（約120）が1行に収まる幅。
    /// これを切ると名前が「削除」の下に潜り、欄が切れた（窓の幅 900 で右の欄は約310しかない）
    /// </summary>
    private const double BaseDetailMinWidth = 360;

    /// <summary>
    /// 素体の詳細のカードが狭いときは、絵を左ではなく上に置く。
    /// 幅はカードの外（右の欄の幅）で決まり、中身の置き方では変わらないので、切り替えで行き来はしない
    /// </summary>
    private void BaseCardBody_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not DockPanel panel || panel.Children.Count == 0 || panel.Children[0] is not FrameworkElement image)
        {
            return;
        }

        var narrow = panel.ActualWidth < image.Width + 20 + BaseDetailMinWidth;
        DockPanel.SetDock(image, narrow ? Dock.Top : Dock.Left);
        image.HorizontalAlignment = HorizontalAlignment.Left;
        image.Margin = narrow ? new Thickness(0, 0, 0, 14) : new Thickness(0, 0, 20, 0);
    }

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
                    && VisualTreeFinder.FindDescendant<Expander>(container) is { IsExpanded: false } expander)
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

}
