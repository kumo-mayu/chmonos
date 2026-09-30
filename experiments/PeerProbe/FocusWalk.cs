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
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.App.Views;

namespace PeerProbe;

public static class FocusWalk
{
    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);

    public static void Run()
    {
        var app = new BoothAssetManager.App.App();
        app.InitializeComponent();

        Walk("商品ページの札（対応アバター・共通素体・説明文の候補）", AvatarsPanel(), stops: 12, enterAt: ["ItemAvatarChip", "ItemAvatarReject", "ItemBaseMentionDismiss"]);
        Walk("要確認の行", InboxRows(), stops: 9, enterAt: ["InboxRow", "InboxRowAction"]);
    }

    private static FrameworkElement AvatarsPanel()
    {
        IDictionary<string, object?> data = new ExpandoObject();
        data["IsAvatarsExpanded"] = true;
        data["AvatarsCountText"] = "（2）";
        data["AvatarTiles"] = new ObservableCollection<object>
        {
            new AvatarRow
            {
                ItemId = "9000001", Name = "作り物のアバターA", SourceText = "説明文",
                OpenCommand = new RelayCommand(() => ProbeLog.Lines.Add("コマンド: アバターAを開く")),
                RejectCommand = new RelayCommand(() => ProbeLog.Lines.Add("コマンド: アバターAを外す")),
            },
            new AvatarRow
            {
                ItemId = "9000002", Name = "作り物のアバターB", SourceText = "説明文",
                OpenCommand = new RelayCommand(() => ProbeLog.Lines.Add("コマンド: アバターBを開く")),
                RejectCommand = new RelayCommand(() => ProbeLog.Lines.Add("コマンド: アバターBを外す")),
            },
        };
        data["HasAvatarBases"] = true;
        data["AvatarBases"] = new ObservableCollection<object> { Row("作り物の素体", ("RejectCommand", "素体を外す")) };
        data["HasBaseMentions"] = true;
        data["BaseMentions"] = new ObservableCollection<object>
        {
            Row("作り物の候補", ("AddCommand", "候補を追加"), ("DismissCommand", "候補を消す")),
        };
        data["HasRejectedAvatars"] = false;
        return new ItemAvatarsPanel { DataContext = data, Width = 700 };
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
            row["Cards"] = new ObservableCollection<object>();
            rows.Add(row);
        }

        return new BoothAssetManager.App.Controls.ContentItemsControl { ItemsSource = rows, ItemTemplate = template, Width = 700, Focusable = false };
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

    private static void Walk(string title, FrameworkElement root, int stops, string[] enterAt)
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
        root.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));

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
            Console.WriteLine($"  止まった: {Describe(focused)}  枠={(focused.FocusVisualStyle is null ? "無し" : "有り")}");
            Console.WriteLine($"      出ている: {string.Join(" / ", Revealed(root))}");

            // 決めた部品の最初の1つで Enter を送り、押したのと同じことが起きるかを見る
            if (enterAt.Contains(id) && pressed.Add(id))
            {
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent };
                InputManager.Current.ProcessInput(key);
                InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyUpEvent });
                root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Console.WriteLine($"      Enter → {(ProbeLog.Lines.Count > 0 ? string.Join(" / ", ProbeLog.Lines) : "何も起きない")}");
                ProbeLog.Lines.Clear();
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
