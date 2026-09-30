// 使い方：
//   dotnet run --project experiments/PeerProbe                 全部の場面の木を書き出す
//   dotnet run --project experiments/PeerProbe -- 20-item      名前にこの文字を含む場面だけ
//   dotnet run --project experiments/PeerProbe -- dynamic      素の ItemsControl の行が見えなくなる条件を試す
//   （--dir <フォルダ> で、別の場所の *.probe.txt を読む。PROBE_TEXT=1 で文字の部品も全部書く）
// 読み方：行は「型 [クラス] 名前 id 操作」。頭の「×control」は、既定の見方（操作できる部品）に出ない物。
// 場面のデータで __press に挙げた ID（と ID が Probe で始まる物）は、持っている操作で実際に押し、画面の側に届いたかを書く。
// 場面のデータは作り物だけを書く（友人のデータの名前・ID を書かない）。メニューや窓を開く部品は __press に挙げない（見えない窓からでも画面に出る）
//
// 部品の UI Automation の木を、画面に何も出さずに書き出す。
// 画面は別の担当が使っているので、アプリは起動しない。見えない窓（WS_VISIBLE なし・画面の外）に部品を載せ、
// 確かめの道具と同じ UI Automation のクライアントの側から木をたどる（別スレッドから。同じスレッドからは自分の窓を読めない）。
// アプリの見た目（App.xaml の資源）は読み込むが、起動の処理（OnStartup）は走らせない。通信も保存先も触らない
using System.Collections.ObjectModel;
using System.Dynamic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Threading;

namespace PeerProbe;

public static class Program
{
    private const string Ns =
        "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
        "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
        "xmlns:s='clr-namespace:System;assembly=System.Runtime' " +
        "xmlns:controls='clr-namespace:BoothAssetManager.App.Controls;assembly=BoothAssetManager.App' " +
        "xmlns:views='clr-namespace:BoothAssetManager.App.Views;assembly=BoothAssetManager.App' " +
        "xmlns:vm='clr-namespace:BoothAssetManager.App.ViewModels;assembly=BoothAssetManager.App' " +
        "xmlns:probe='clr-namespace:PeerProbe;assembly=PeerProbe'";

    [STAThread]
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        // 場面は probes\*.probe.txt（XAML と、---data--- の後に結ぶ値の JSON）。ビルドで実行ファイルの横へ写す
        var dirAt = Array.IndexOf(args, "--dir");
        var dir = dirAt >= 0 && dirAt + 1 < args.Length ? args[dirAt + 1] : Path.Combine(AppContext.BaseDirectory, "probes");
        var only = args.Length > 0 && dirAt != 0 ? args[0] : "";
        if (only == "dynamic")
        {
            Dynamic.Run();
            return;
        }

        // アプリの資源（色・ボタンの見た目）だけを読み込む。Run しないので OnStartup は走らない
        var app = new BoothAssetManager.App.App();
        app.InitializeComponent(); Console.WriteLine("  [色の表] 読み込み直後: " + app.Resources.MergedDictionaries[0].Source + " Text=" + app.TryFindResource("Text"));
        EventManager.RegisterClassHandler(typeof(ListViewItem), BoothAssetManager.App.Controls.ItemListView.RowInvokedEvent,
            new RoutedEventHandler((sender, _) => ProbeLog.Lines.Add("行に「押す」が届いた: " + System.Windows.Automation.AutomationProperties.GetName((DependencyObject)sender))));

        foreach (var file in Directory.GetFiles(dir, "*.probe.txt").OrderBy(f => f))
        {
            if (!Path.GetFileName(file).Contains(only))
            {
                continue;
            }

            Console.WriteLine($"==== {Path.GetFileName(file)}");
            try
            {
                var parts = File.ReadAllText(file).Split("---data---");
                var xaml = parts[0].Replace("__NS__", Ns);
                var root = (FrameworkElement)XamlReader.Parse(xaml); Console.WriteLine("  [色の表] 組んだ後: " + app.Resources.MergedDictionaries[0].Source + " Text=" + app.TryFindResource("Text"));
                if (parts.Length > 1)
                {
                    root.DataContext = Fake.From(JsonDocument.Parse(parts[1]).RootElement);
                }

                if (file.Contains("50-realcards"))
                {
                    root.DataContext = new ProbeHost();
                    Fake.Press.Clear();
                    foreach (var id in new[] { "ItemCard", "ItemCardFavorite", "ItemListFavorite", "ListViewItem" })
                    {
                        Fake.Press.Add(id);
                    }
                }

                if (parts.Length > 1)
                {
                    if (file.Contains("41-search"))
                    {
                        RealModules.AddTo(root.DataContext);
                    }
                }

                // WS_POPUP だけ（WS_VISIBLE を付けない）。画面の外に置き、前面にも出さない
                var parameters = new HwndSourceParameters("PeerProbe", 1200, 900)
                {
                    WindowStyle = unchecked((int)0x80000000),
                    ExtendedWindowStyle = 0x08000080, // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                    PositionX = -30000,
                    PositionY = -30000,
                };
                using var source = new HwndSource(parameters) { RootVisual = root };
                root.UpdateLayout();

                // 後から作る部品（カードの中身）が出揃うまで回す
                root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                root.UpdateLayout();
                var handle = source.Handle;

                // 継いだ型（ItemListView）に、ListView の暗黙の見た目が当たっているか
                foreach (var list in LogicalTreeHelper.GetChildren(root).OfType<ListView>())
                {
                    var origin = DependencyPropertyHelper.GetValueSource(list, Control.ForegroundProperty);
                    Console.WriteLine($"  [見た目] {list.GetType().Name}: 文字の色={list.Foreground}（{origin.BaseValueSource}） 地={list.Background} 枠={list.BorderBrush} Style={(list.Style is null ? "無し" : "有り")} 資源Text={list.TryFindResource("Text")} Surface={list.TryFindResource("Surface")} 同じStyle={ReferenceEquals(list.Style, list.TryFindResource(typeof(ListView)))}");
                }

                var frame = new DispatcherFrame();
                var lines = new List<string>();
                var worker = new Thread(() =>
                {
                    try
                    {
                        var element = AutomationElement.FromHandle(handle);
                        Dump(element, 0, lines);
                        // 道具と同じ探し方（FindAll・型で絞る）で数える
                        foreach (var type in new[] { ControlType.Button, ControlType.Edit, ControlType.CheckBox, ControlType.ComboBox, ControlType.ListItem, ControlType.DataItem, ControlType.MenuItem, ControlType.Slider, ControlType.RadioButton })
                        {
                            var found = element.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, type));
                            if (found.Count > 0)
                            {
                                lines.Add($"  FindAll {type.ProgrammaticName.Replace("ControlType.", "")}: {found.Count} 件 → " + string.Join(" / ", found.Cast<AutomationElement>().Select(e => $"{e.Current.Name}#{e.Current.AutomationId}")));
                            }
                        }

                        lines.Add(Press(element));
                    }
                    catch (Exception ex)
                    {
                        lines.Add("失敗: " + ex);
                    }
                    finally
                    {
                        frame.Dispatcher.BeginInvoke(() => frame.Continue = false);
                    }
                });
                worker.SetApartmentState(ApartmentState.MTA);
                worker.Start();
                Dispatcher.PushFrame(frame);
                worker.Join();
                root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                foreach (var line in lines)
                {
                    Console.WriteLine(line);
                }

                foreach (var line in ProbeLog.Lines)
                {
                    Console.WriteLine("  [画面の側] " + line);
                }

                ProbeLog.Lines.Clear();
            }
            catch (Exception ex)
            {
                Console.WriteLine("失敗: " + ex);
            }
        }
    }

    private static void Dump(AutomationElement element, int depth, List<string> lines)
    {
        var current = element.Current;
        var patterns = element.GetSupportedPatterns().Select(p => p.ProgrammaticName.Replace("PatternIdentifiers.Pattern", ""))
            .Where(p => p is not ("SynchronizedInput" or "ScrollItem" or "VirtualizedItem"));
        var type = current.ControlType.ProgrammaticName.Replace("ControlType.", "");
        // 道具の Get-ChmonosElements は既定の見方（ControlView）で探す。そこに出ない物は × を付ける
        var mark = current.IsControlElement ? "" : "×control ";
        if (type is "ScrollBar" or "Thumb" || current.AutomationId is "PART_ContentHost")
        {
            return;
        }

        // 文字だけの部品は、ID の付いた物だけ書く（量を減らす）
        if (type != "Text" || current.AutomationId.Length > 0 || depth < 3 || Environment.GetEnvironmentVariable("PROBE_TEXT") == "1")
        {
            lines.Add($"{new string(' ', depth * 2)}{mark}{type} [{current.ClassName}] 名前=「{current.Name}」 id=「{current.AutomationId}」 {string.Join(",", patterns)}");
        }

        var walker = TreeWalker.RawViewWalker;
        for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
        {
            Dump(child, depth + 1, lines);
        }
    }

    /// <summary>ID が Probe で始まる物と、データで press に挙げた ID の物を、持っている操作で押してみる。</summary>
    private static string Press(AutomationElement root)
    {
        var results = new List<string>();
        var all = root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
        foreach (AutomationElement element in all)
        {
            var id = element.Current.AutomationId;
            if (!id.StartsWith("Probe", StringComparison.Ordinal) && !Fake.Press.Contains(id) && !Fake.Press.Contains(element.Current.ClassName))
            {
                continue;
            }

            var label = $"{id}「{element.Current.Name}」";
            try
            {
                if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
                {
                    ((InvokePattern)invoke).Invoke();
                    results.Add($"  押した（Invoke）: {label}");
                }
                else if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expand))
                {
                    ((ExpandCollapsePattern)expand).Expand();
                    results.Add($"  開いた（Expand）: {label}");
                }
                else
                {
                    results.Add($"  押す操作を持たない: {label}");
                }
            }
            catch (Exception ex)
            {
                results.Add($"  押せなかった: {label} {ex.GetType().Name} {ex.Message}");
            }
        }

        Thread.Sleep(300);
        return string.Join(Environment.NewLine, results);
    }
}

/// <summary>画面の側で起きたことの控え（押したら何が呼ばれたか）。</summary>
public static class ProbeLog
{
    public static List<string> Lines { get; } = [];
}

/// <summary>押したら控えに書くだけのコマンド。</summary>
public sealed class LogCommand : ICommand
{
    public string Label { get; set; } = "";

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => ProbeLog.Lines.Add($"コマンド: {Label}" + (parameter is null ? "" : $"（{Describe(parameter)}）"));

    private static string Describe(object value) =>
        value is IDictionary<string, object?> bag && bag.TryGetValue("Name", out var name) ? $"{name}" : $"{value}";
}

/// <summary>作り物の画面の値。JSON を、結べる形（ExpandoObject と一覧）にする。"cmd:名前" はコマンドになる。</summary>
public static class Fake
{
    public static HashSet<string> Press { get; } = [];

    public static object? From(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                IDictionary<string, object?> bag = new ExpandoObject();
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "__press")
                    {
                        Press.Clear();
                        foreach (var id in property.Value.EnumerateArray())
                        {
                            Press.Add(id.GetString()!);
                        }

                        continue;
                    }

                    bag[property.Name] = From(property.Value);
                }

                return bag;
            case JsonValueKind.Array:
                return new ObservableCollection<object?>(element.EnumerateArray().Select(From));
            case JsonValueKind.String:
                var text = element.GetString()!;
                return text.StartsWith("cmd:", StringComparison.Ordinal) ? new LogCommand { Label = text[4..] } : text;
            case JsonValueKind.Number:
                return element.TryGetInt32(out var whole) ? whole : element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }
}
