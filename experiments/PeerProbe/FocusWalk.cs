// キーボードのフォーカスを Tab と同じ順に進めて、止まった所と、そのとき出ている「乗せたときだけ出すボタン」を書き出す。
// フォーカスの枠と Tab の動きは、窓を出さずに描く台では確かめられず、アプリを起動すると画面を占有する。
// 見えない窓に載せ、WPF の「次へ進む」（Tab キーと同じ決まり）を呼んで順に止める。Enter は入力の口から送る（実際のキーは押さない）
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;

namespace PeerProbe;

public static class FocusWalk
{
    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);

    public static void Run()
    {
        var app = new Chmonos.App.App();
        app.InitializeComponent();

        Walk("商品ページの札（対応アバター・共通素体・説明文の候補）", AvatarsPanel(), stops: 12, enterAt: ["ItemAvatarChip", "ItemAvatarReject", "ItemBaseMentionDismiss"]);

        MeasureRefresh(avatars: 244);

        // 並びは Tab で1回だけ入り、中は矢印で移る（ユーザ判断 2026-10-01）。札が2段に折り返す幅で、矢印・Home・End・出入りを順に送る
        Script("対応アバターの札（6体・2段）を矢印で移り、Tab で出入りする", AvatarsPanel(avatars: 6, width: 330),
            ["Tab", "Tab", "Right", "Right", "Right", "Down", "Left", "Up", "End", "Home", "Right", "Tab", "Tab", "ShiftTab", "ShiftTab", "ShiftTab", "Tab", "Right", "Enter"]);
        Script("設定の除外したファイル（40行）を矢印で移り、解除した後に止まる所", SettingsExcluded(rows: 40, removeOnRestore: true),
            ["Tab", "Down", "Down", "End", "Up", "Enter", "Enter", "Home", "Up", "Tab", "ShiftTab"],
            start: root => FindById(root, "Settings.ExcludedExpander") is { } expander ? FindChild<Chmonos.App.Controls.ExpandToggle>(expander) : null,
            note: OuterScroll);
        Walk("要確認の行", InboxRows(), stops: 11, enterAt: ["InboxRow", "InboxRowRead", "InboxRowAction"]);

        // カードの一覧は、読み上げ・自動操作に段を出さない部品へ替えた（2026-09-30）。キーボードの動きが前の作りと同じことを、並べて見る
        Walk("カードの一覧（前の作り：ListBox と ContentItemsControl）", CardList("ListBox", "controls:ContentItemsControl"), stops: 5, enterAt: ["ItemCard"], arrows: true);
        Walk("カードの一覧（今の作り：CardRowsListBox と CardRowItems）", CardList("controls:CardRowsListBox", "controls:CardRowItems"), stops: 5, enterAt: ["ItemCard"], arrows: true);

        // 設定の除外したファイルは、見える分だけ行を作る一覧にした（2026-10-01）。作られていない行へも Tab で進めるか（流れて次の行ができるか）。
        // 画面の上の欄から数えると長いので、欄の三角から始める
        Walk("設定の除外したファイル（見える分だけ作る一覧・40行）", SettingsExcluded(rows: 40), stops: 44, enterAt: ["Settings.ExcludedRestore"],
            start: root => FindById(root, "Settings.ExcludedExpander") is { } expander ? FindChild<Chmonos.App.Controls.ExpandToggle>(expander) : null,
            note: OuterScroll);
    }

    /// <summary>
    /// 札の多い並びで、Tab で止まる物を数え直す1回の重さ（札にフォーカスが来るたび・行が作られるたびに走る）。
    /// 対応アバターは244体の商品がある。全部を出した形（「残りを表示」の後）で測る
    /// </summary>
    private static void MeasureRefresh(int avatars)
    {
        var root = AvatarsPanel(avatars, width: 700);
        using var source = Host(root);
        var list = Groups(root).First();
        var times = new List<double>();
        for (var i = 0; i < 20; i++)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Chmonos.App.Controls.ArrowGroup.RefreshNow(list);
            times.Add(clock.Elapsed.TotalMilliseconds);
        }

        times.Sort();
        Console.WriteLine($"==== 札{avatars}枚の並びで、Tab で止まる物を数え直す1回：中央 {times[times.Count / 2]:0.00} ms・最大 {times[^1]:0.00} ms（20回）");
    }

    /// <summary>
    /// 画面全体を流す枠（いちばん外の ScrollViewer）の位置と、止まった部品が見えている枠のどこにあるか。
    /// 一覧の外へ Tab で出たときに、止まった部品を見せる以上に流れていないかを見る
    /// </summary>
    private static string OuterScroll(FrameworkElement root, FrameworkElement focused)
    {
        if (FindChild<ScrollViewer>(root) is not { } outer)
        {
            return "";
        }

        // 消えた行の部品・使い回しを待つ部品にフォーカスが残っていると、外の枠の中に位置が無い
        if (!focused.IsDescendantOf(outer) || !focused.IsVisible)
        {
            return $"外の位置={outer.VerticalOffset:0.0}/{outer.ScrollableHeight:0.0} 部品は画面に無い（消えた行か、隠れた部品）";
        }

        var top = focused.TransformToAncestor(outer).Transform(new Point(0, 0)).Y;
        return $"外の位置={outer.VerticalOffset:0.0}/{outer.ScrollableHeight:0.0} 部品の上端={top:0.0}（見える高さ {outer.ViewportHeight:0}）";
    }

    private static FrameworkElement SettingsExcluded(int rows, bool removeOnRestore = false)
    {
        IDictionary<string, object?> data = new ExpandoObject();
        data["HasExcluded"] = true;
        data["IsExcludedExpanded"] = true;
        data["ExcludedText"] = $"{rows} 件";
        data["HiddenText"] = "0 件";
        var list = new ObservableCollection<object>();
        foreach (var index in Enumerable.Range(0, rows))
        {
            var row = (IDictionary<string, object?>)Row($"作り物_{index:00}", ("RestoreCommand", $"作り物_{index:00}の除外を解除"));
            row["Label"] = $@"D:\作り物\file_{index:00}.png";
            row["SubText"] = string.Empty;
            if (removeOnRestore)
            {
                // アプリと同じく、解除したら記録を読み直して、その行だけを一覧から抜く（CollectionSync.Apply は RemoveAt で抜く）
                var captured = (object)row;
                row["RestoreCommand"] = new RelayCommand(() =>
                {
                    ProbeLog.Lines.Add($"コマンド: {row["Label"]}の除外を解除（行を抜く）");
                    list.Remove(captured);
                });
            }

            list.Add(row);
        }

        data["Excluded"] = list;
        return new SettingsView { DataContext = data, Width = 900, Height = 700 };
    }

    private static FrameworkElement? FindById(DependencyObject root, string id)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element && AutomationProperties.GetAutomationId(element) == id)
            {
                return element;
            }

            if (FindById(child, id) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindChild<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>検索・ショップ・フォルダの右と同じ形のカードの一覧（2段・2枚ずつ）。一覧と段の部品の型だけを替えて組む。</summary>
    private static FrameworkElement CardList(string list, string row)
    {
        var xaml = $$"""
            <UserControl {{Program.Ns}}>
                <UserControl.Resources>
                    <ResourceDictionary>
                        <ResourceDictionary.MergedDictionaries>
                            <ResourceDictionary Source="pack://application:,,,/Chmonos;component/Views/ItemCardResources.xaml" />
                        </ResourceDictionary.MergedDictionaries>
                    </ResourceDictionary>
                </UserControl.Resources>
                <{{list}} ItemsSource="{Binding Rows}" BorderThickness="0" Background="Transparent" KeyboardNavigation.TabNavigation="Continue"
                          HorizontalContentAlignment="Stretch" ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                          VirtualizingPanel.IsVirtualizing="True" VirtualizingPanel.VirtualizationMode="Recycling" VirtualizingPanel.ScrollUnit="Pixel">
                    <{{list}}.ItemContainerStyle>
                        <Style TargetType="ListBoxItem">
                            <Setter Property="Padding" Value="0" />
                            <Setter Property="Margin" Value="0" />
                            <Setter Property="Focusable" Value="False" />
                            <Setter Property="Template">
                                <Setter.Value>
                                    <ControlTemplate TargetType="ListBoxItem">
                                        <ContentPresenter />
                                    </ControlTemplate>
                                </Setter.Value>
                            </Setter>
                        </Style>
                    </{{list}}.ItemContainerStyle>
                    <{{list}}.ItemTemplate>
                        <DataTemplate>
                            <{{row}} ItemsSource="{Binding Cards}" ItemTemplate="{StaticResource ItemCardTemplate}" Focusable="False">
                                <ItemsControl.ItemsPanel>
                                    <ItemsPanelTemplate>
                                        <StackPanel Orientation="Horizontal" />
                                    </ItemsPanelTemplate>
                                </ItemsControl.ItemsPanel>
                            </{{row}}>
                        </DataTemplate>
                    </{{list}}.ItemTemplate>
                </{{list}}>
            </UserControl>
            """;
        var root = (FrameworkElement)System.Windows.Markup.XamlReader.Parse(xaml);
        root.DataContext = new ProbeHost(rows: 2);
        return root;
    }

    private static FrameworkElement AvatarsPanel(int avatars = 2, double width = 700)
    {
        IDictionary<string, object?> data = new ExpandoObject();
        data["IsAvatarsExpanded"] = true;
        data["AvatarsCountText"] = $"（{avatars}）";
        var tiles = new ObservableCollection<object>();
        foreach (var index in Enumerable.Range(0, avatars))
        {
            var letter = (char)('A' + index);
            tiles.Add(new AvatarRow
            {
                ItemId = $"90000{index + 1:00}", Name = $"作り物のアバター{letter}", SourceText = "説明文",
                OpenCommand = new RelayCommand(() => ProbeLog.Lines.Add($"コマンド: アバター{letter}を開く")),
                RejectCommand = new RelayCommand(() => ProbeLog.Lines.Add($"コマンド: アバター{letter}を外す")),
            });
        }

        // 末尾の「＋ 追加」。並びとは別に Tab で止まる
        tiles.Add(AvatarAddTile.Instance);
        data["AvatarTiles"] = tiles;
        data["IsAddingAvatar"] = false;
        data["HasAvatarBases"] = true;
        data["AvatarBases"] = new ObservableCollection<object> { Row("作り物の素体", ("RejectCommand", "素体を外す")) };
        data["HasBaseMentions"] = true;
        data["BaseMentions"] = new ObservableCollection<object>
        {
            Row("作り物の候補", ("AddCommand", "候補を追加"), ("DismissCommand", "候補を消す")),
        };
        data["HasRejectedAvatars"] = false;
        data["StartAddAvatarCommand"] = new LogCommand { Label = "対応アバターの追加を始める" };
        return new ItemAvatarsPanel { DataContext = data, Width = width };
    }

    private static FrameworkElement InboxRows()
    {
        // 行の見た目は要確認の画面が持っている。画面は組まず、見た目だけを借りる
        var template = (DataTemplate)new InboxView().Resources["InboxRowTemplate"];
        var rows = new ObservableCollection<object>();
        foreach (var name in new[] { "作り物の知らせ1", "作り物の知らせ2" })
        {
            var row = (IDictionary<string, object?>)Row(name, ("OpenItemCommand", name + "の商品を開く"), ("ActionCommand", name + "を直しに行く"));
            row["Title"] = name;
            row["HasItem"] = true;
            row["HasAction"] = true;
            row["ActionText"] = "商品情報を取り直す";
            row["CreatedText"] = "3時間前";
            row["ReadButtonText"] = "既読にする";
            row["ReadButtonName"] = name + "を既読にする";
            row["IsRead"] = false;
            row["Cards"] = new ObservableCollection<object>();
            rows.Add(row);
        }

        return new Chmonos.App.Controls.ContentItemsControl { ItemsSource = rows, ItemTemplate = template, Width = 700, Focusable = false };
    }

    private static object Row(string name, params (string Key, string Label)[] commands)
    {
        IDictionary<string, object?> row = new ExpandoObject();
        row["Name"] = name;
        foreach (var (key, label) in commands)
        {
            row[key] = new LogCommand { Label = label };
        }

        return row;
    }

    /// <summary>
    /// 決めた順にキーを送り、送るたびに止まった所を書く。Tab・ShiftTab は WPF の「次へ・前へ進む」（Tab キーと同じ決まり）、
    /// ほかは入力の口からキーを送る（PreviewKeyDown と PreviewKeyUp。実際のキーは押さない）
    /// </summary>
    private static void Script(
        string title, FrameworkElement root, string[] steps,
        Func<FrameworkElement, FrameworkElement?>? start = null,
        Func<FrameworkElement, FrameworkElement, string>? note = null)
    {
        Console.WriteLine($"==== {title}");
        using var source = Host(root);
        if (start?.Invoke(root) is { } startAt)
        {
            startAt.Focus();
        }
        else
        {
            root.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }

        Idle(root);
        foreach (var group in Groups(root))
        {
            var members = Chmonos.App.Controls.ArrowGroup.MembersOf(group);
            var stops = members.Where(member => KeyboardNavigation.GetIsTabStop(member)).ToList();
            Console.WriteLine($"  並び {AutomationProperties.GetAutomationId(group)}{AutomationProperties.GetName(group)}：止まり先 {members.Count}・Tab で止まる物 {stops.Count}（{string.Join(" / ", stops.OfType<FrameworkElement>().Select(Describe))}）");
        }

        Console.WriteLine($"  最初: {Now(root, note)}");
        foreach (var step in steps)
        {
            var focused = Keyboard.FocusedElement as UIElement;
            switch (step)
            {
                case "Tab":
                    focused?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    break;
                case "ShiftTab":
                    focused?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
                    break;
                default:
                    Send(source, Enum.Parse<Key>(step));
                    break;
            }

            Idle(root);
            var log = ProbeLog.Lines.Count > 0 ? "  ／ " + string.Join(" / ", ProbeLog.Lines) : "";
            ProbeLog.Lines.Clear();
            Console.WriteLine($"  {step,-8} → {Now(root, note)}{log}");
        }
    }

    private static string Now(FrameworkElement root, Func<FrameworkElement, FrameworkElement, string>? note)
        => Keyboard.FocusedElement is FrameworkElement now
            ? $"{Describe(now)}{(note is null || !now.IsDescendantOf(root) ? "" : "  " + note(root, now))}"
            : $"止まり先が無い（{Keyboard.FocusedElement?.GetType().Name ?? "null"}）";

    private static void Send(HwndSource source, Key key)
    {
        var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        // PreviewKeyDown を送ると、受けられなかったときは入力の口が KeyDown に進めて送る（実際のキーと同じ）
        InputManager.Current.ProcessInput(preview);
        InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyUpEvent });
    }

    private static IEnumerable<ItemsControl> Groups(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ItemsControl list && Chmonos.App.Controls.ArrowGroup.GetIsEnabled(list))
            {
                yield return list;
            }

            foreach (var found in Groups(child))
            {
                yield return found;
            }
        }
    }

    private static void Idle(FrameworkElement root) => root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static HwndSource Host(FrameworkElement root)
    {
        var parameters = new HwndSourceParameters("PeerProbe", 900, 700)
        {
            WindowStyle = unchecked((int)0x80000000),
            ExtendedWindowStyle = 0x00000080, // WS_EX_TOOLWINDOW。フォーカスを受けるので NOACTIVATE は付けない（見えない窓なので前面には出ない）
            PositionX = -30000,
            PositionY = -30000,
        };
        var source = new HwndSource(parameters) { RootVisual = root };
        root.UpdateLayout();
        Idle(root);

        // このスレッドの中でのフォーカスを見えない窓へ移す（ほかのアプリの前面の窓は変わらない）
        SetFocus(source.Handle);
        Idle(root);
        return source;
    }

    /// <summary>今止まっている所で矢印を送り、止まり先がどこへ移ったかを書く。</summary>
    private static void Arrow(HwndSource source, FrameworkElement root, Key key)
    {
        InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent });
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Console.WriteLine($"      {key} → {(Keyboard.FocusedElement is FrameworkElement now ? Describe(now) : "止まり先が無い")}");
    }

    private static void Walk(
        string title, FrameworkElement root, int stops, string[] enterAt, bool arrows = false,
        Func<FrameworkElement, FrameworkElement?>? start = null,
        Func<FrameworkElement, FrameworkElement, string>? note = null)
    {
        Console.WriteLine($"==== {title}");
        var parameters = new HwndSourceParameters("PeerProbe", 900, 700)
        {
            WindowStyle = unchecked((int)0x80000000),
            ExtendedWindowStyle = 0x00000080, // WS_EX_TOOLWINDOW。フォーカスを受けるので NOACTIVATE は付けない（見えない窓なので前面には出ない）
            PositionX = -30000,
            PositionY = -30000,
        };
        using var source = new HwndSource(parameters) { RootVisual = root };
        root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        // このスレッドの中でのフォーカスを見えない窓へ移す（ほかのアプリの前面の窓は変わらない）
        SetFocus(source.Handle);
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        if (start?.Invoke(root) is { } startAt)
        {
            startAt.Focus();
        }
        else
        {
            root.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }

        // カードの矢印：右・下・左・上と送って、一回りして最初のカードへ戻るか
        if (arrows)
        {
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Console.WriteLine($"  最初の止まり: {(Keyboard.FocusedElement is FrameworkElement first ? Describe(first) : "無し")}");
            foreach (var key in new[] { Key.Right, Key.Down, Key.Left, Key.Up })
            {
                Arrow(source, root, key);
            }
        }

        var pressed = new HashSet<string>();
        for (var stop = 0; stop < stops; stop++)
        {
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            if (Keyboard.FocusedElement is not FrameworkElement focused)
            {
                Console.WriteLine("  フォーカスを受けた部品が無い（見えない窓がフォーカスを取れなかった）");
                return;
            }

            var id = AutomationProperties.GetAutomationId(focused);
            Console.WriteLine($"  止まった: {Describe(focused)}  枠={(focused.FocusVisualStyle is null ? "無し" : "有り")}{(note is null ? "" : "  " + note(root, focused))}");
            Console.WriteLine($"      出ている: {string.Join(" / ", Revealed(root))}");

            // 決めた部品の最初の1つで Enter を送り、押したのと同じことが起きるかを見る
            if (enterAt.Contains(id) && pressed.Add(id))
            {
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent };
                InputManager.Current.ProcessInput(key);
                InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyUpEvent });
                root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                // 切り替えの部品（要確認の既読の丸）は、コマンドではなく値が替わる。行の側（商品を開く）へ漏れていないことも、同じ行で分かる
                var toggled = focused is System.Windows.Controls.Primitives.ToggleButton toggle ? $"切り替わった（入={toggle.IsChecked}）" : null;
                Console.WriteLine($"      Enter → {string.Join(" / ", new[] { toggled }.Concat(ProbeLog.Lines).Where(line => line is not null).DefaultIfEmpty("何も起きない"))}");
                ProbeLog.Lines.Clear();

                if (focused is System.Windows.Controls.Primitives.ToggleButton space)
                {
                    InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
                    InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
                    root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Console.WriteLine($"      Space → 切り替わった（入={space.IsChecked}）{(ProbeLog.Lines.Count > 0 ? " / " + string.Join(" / ", ProbeLog.Lines) : "")}");
                    ProbeLog.Lines.Clear();
                }
            }

            if (!focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)))
            {
                Console.WriteLine("  （次が無い）");
                break;
            }
        }
    }

    /// <summary>乗せたとき・フォーカスがあるときだけ出す部品のうち、今出ている物（透明でなく、見えている物）。</summary>
    private static IEnumerable<string> Revealed(DependencyObject root)
    {
        var found = new List<string>();
        Visit(root, found, 1.0);
        return found.Count > 0 ? found : ["（無し）"];

        static void Visit(DependencyObject node, List<string> found, double opacity)
        {
            if (node is UIElement element)
            {
                if (element.Visibility != Visibility.Visible)
                {
                    return;
                }

                opacity *= element.Opacity;
            }

            if (node is FrameworkElement frame
                && AutomationProperties.GetAutomationId(frame) is "ItemAvatarReject" or "ItemAvatarBaseReject" or "ItemBaseMentionDismiss" or "InboxRowOpenItem" or "InboxRowAction"
                && opacity > 0)
            {
                found.Add($"{AutomationProperties.GetName(frame)}#{AutomationProperties.GetAutomationId(frame)}");
            }

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            {
                Visit(VisualTreeHelper.GetChild(node, index), found, opacity);
            }
        }
    }

    private static string Describe(FrameworkElement element)
        => $"{element.GetType().Name}「{AutomationProperties.GetName(element)}」#{AutomationProperties.GetAutomationId(element)}";
}
