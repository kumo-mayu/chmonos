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

    /// <summary>
    /// アバター詳細の頭で、絵の右に残る幅の下限。いちばん長いボタン「プロファイルを検索で開く」（約165）と間（8）が1つ入る幅。
    /// 窓の幅 900 では右の列が約150で、ボタンが右で切れた（2026-11-20 の調べ）。絵（84）・間（16）を引いた残りで決める
    /// </summary>
    private const double AvatarHeadTextMinWidth = 180;

    private void AvatarHeadGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = AvatarHeadGrid.ActualWidth < 84 + 16 + AvatarHeadTextMinWidth;
        Grid.SetRow(AvatarHeadActions, narrow ? 2 : 1);
        Grid.SetColumn(AvatarHeadActions, narrow ? 0 : 1);
        Grid.SetColumnSpan(AvatarHeadActions, narrow ? 2 : 1);
        AvatarHeadActions.Margin = narrow ? new Thickness(0, 10, 0, 0) : new Thickness(6, 10, 0, 0);
    }

    /// <summary>
    /// 変換の商品の頭で、絵（200）の右に残る幅の下限。「商品ページを開く」（約110）と「BOOTHで開く」が並んで入る幅。
    /// これを切ると名前の列が約100で、ボタンが右で切れた。狭い時は絵を上に置き、名前とボタンを下に全幅で出す
    /// </summary>
    private const double ConversionHeadTextMinWidth = 190;

    private void ConversionHeadGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = ConversionHeadGrid.ActualWidth < 200 + 18 + ConversionHeadTextMinWidth;
        Grid.SetColumn(ConversionHeadText, narrow ? 0 : 1);
        Grid.SetColumnSpan(ConversionHeadText, narrow ? 2 : 1);
        Grid.SetRow(ConversionHeadText, narrow ? 1 : 0);
        Grid.SetColumnSpan(ConversionHeadImage, narrow ? 2 : 1);
        ConversionHeadImage.HorizontalAlignment = HorizontalAlignment.Left;
        ConversionHeadImage.Margin = narrow ? new Thickness(0, 0, 0, 12) : new Thickness(0, 0, 18, 0);
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

    /// <summary>
    /// 「変換」の左の流れの先頭の追加した組の節（ユーザ判断 2026-11-20・①）は選ぶ行ではない。節の中の空きを押しても、入れ物が行として選ばれないよう止める
    /// （中のボタン・欄・組の行は、ここへ来る前に自分で受けている）
    /// </summary>
    private void ConversionPairsSection_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => e.Handled = true;

    /// <summary>
    /// 節の中の欄（組の欄・並び）で、欄が使わなかった ↑↓・PageUp・PageDown・Home・End を、外の一覧へ渡さない。
    /// 節は外の一覧の1行なので、渡すと外の一覧が商品の行へ止まりを移し、打っていた欄から離れた
    /// </summary>
    private void ConversionPairsSection_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is System.Windows.Input.Key.Up or System.Windows.Input.Key.Down or System.Windows.Input.Key.PageUp
            or System.Windows.Input.Key.PageDown or System.Windows.Input.Key.Home or System.Windows.Input.Key.End)
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// 節・組のまとまりの見出し・商品が無い時の1行は止まらない入れ物なので ↑↓ では選ばれないが、選ばれた時（読み上げ・自動操作から）は今の選び
    /// （組の行か商品の行）に戻す
    /// </summary>
    private void ConversionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && list.SelectedItem is ConversionPairsSection or ConversionPairGroup or ConversionListEnd
            && DataContext is AvatarsViewModel vm)
        {
            list.SelectedItem = vm.SelectedConversionEntry;
        }
    }

    /// <summary>
    /// 「変換」の左の一覧のキー（ユーザ判断 2026-11-20・④）。組のまとまりの見出しは選ぶ行ではないので、ListBox の既定の ↑↓（止まれない入れ物を飛ばす）
    /// では見出しへ届かず、キーボードから畳めなかった。組の見出し・組の行に止まっている間は、↑↓・Home・End・PageUp・PageDown で
    /// 組の見出し・組の行・商品の行を1つの並びとして渡り（行なら選ぶ）、見出しの上では ← で畳み → で開く（Enter・Space の開閉は三角が自分で受ける）。
    /// 商品の行の間は ListBox の既定に任せ（群の畳みも既定が飛ばす）、商品の行から組の物へ移る時だけ受ける。
    /// 節の中の欄（組を追加する欄・並び）に止まっている時は受けない（欄の ↑↓ は候補・選び）。修飾キー付きは画面の物（Alt+← の戻る）なので受けない
    /// </summary>
    private void ConversionList_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not ListBox list || DataContext is not AvatarsViewModel vm
            || System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.None)
        {
            return;
        }

        var focused = System.Windows.Input.Keyboard.FocusedElement as FrameworkElement;
        var current = focused?.DataContext;
        if (current is not (ConversionPairGroup or ConversionPairRow or ConversionItemRowViewModel)
            || (current is ConversionItemRowViewModel && focused is not ListBoxItem))
        {
            return;
        }

        if (current is ConversionPairGroup group && e.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right)
        {
            group.IsExpanded = e.Key == System.Windows.Input.Key.Right;
            e.Handled = true;
            return;
        }

        // 一画面ぶんの目安（6行）。組の一覧に高さの上限があった時の 260 を、行のおおよその高さで割った数のまま
        const int page = 6;
        var (step, toEdge) = e.Key switch
        {
            System.Windows.Input.Key.Up => (-1, false),
            System.Windows.Input.Key.Down => (1, false),
            System.Windows.Input.Key.PageUp => (-page, false),
            System.Windows.Input.Key.PageDown => (page, false),
            System.Windows.Input.Key.Home => (-1, true),
            System.Windows.Input.Key.End => (1, true),
            _ => (0, false),
        };
        if (step == 0)
        {
            return;
        }

        var target = vm.ConversionEntryTarget(current, step, toEdge);
        if (current is ConversionItemRowViewModel && target is not (ConversionPairGroup or ConversionPairRow))
        {
            return;
        }

        // 端で止まった時も受けておく（ListBox の既定に渡すと、見出しを飛ばして選びが動く）
        e.Handled = true;
        if (target is null)
        {
            return;
        }

        vm.SelectConversionEntry(target);

        // 一覧は見える行しか作らないので、先に見える所まで流して作らせてから止める
        list.ScrollIntoView(target);
        list.UpdateLayout();
        if (list.ItemContainerGenerator.ContainerFromItem(target) is not ListBoxItem container)
        {
            return;
        }

        if (target is ConversionPairGroup)
        {
            VisualTreeFinder.FindDescendant<ExpandToggle>(container)?.Focus();
        }
        else
        {
            container.Focus();
        }
    }

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

    /// <summary>
    /// 共通素体を探して足す欄で、打ってすぐ Enter を押した。結び付けの待ち（Delay=200）で画面の側の語がまだ前の物のままなので、先に流し込む
    /// （流し込まないと、決めた後に遅れて届いた語が「既にあります」を消す。SearchAddBox と同じ）
    /// </summary>
    private void BaseSearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            BaseSearchBox.GetBindingExpression(SuggestBox.TextProperty)?.UpdateSource();
        }
    }

    /// <summary>「追加」は打った語を足す。打ってすぐ押した時の待ちを、コマンドが動く前（Click は Command より先）に流し込む。</summary>
    private void BaseAdd_Click(object sender, RoutedEventArgs e)
        => BaseSearchBox.GetBindingExpression(SuggestBox.TextProperty)?.UpdateSource();

}
