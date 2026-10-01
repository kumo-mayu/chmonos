// 素の ItemsControl の中身が UI Automation から見えなくなる条件を探す。
// 一度クライアントが木を読んだ後に項目を足す・差し替える、等しい値の項目、を試す
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Threading;

namespace PeerProbe;

public sealed class RowVm(string name)
{
    public string Name { get; } = name;
}

public sealed record RecordVm(string Name);

public static class Dynamic
{
    private const string Template =
        "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
        "<StackPanel Orientation='Horizontal'><TextBlock Text='{Binding Name}'/><Button Content='{Binding Name}'/><TextBox Text='x'/></StackPanel></DataTemplate>";

    public static void Run()
    {
        foreach (var useContent in new[] { false, true })
        {
            var label = useContent ? "ContentItemsControl" : "素の ItemsControl";
            var rows = new ObservableCollection<object> { new RowVm("初めの1"), new RowVm("初めの2") };
            ItemsControl list = useContent ? new Chmonos.App.Controls.ContentItemsControl() : new ItemsControl();
            list.ItemTemplate = (DataTemplate)XamlReader.Parse(Template);
            list.ItemsSource = rows;
            var host = new StackPanel();
            host.Children.Add(list);

            var parameters = new HwndSourceParameters("PeerProbe", 900, 700)
            {
                WindowStyle = unchecked((int)0x80000000),
                ExtendedWindowStyle = 0x08000080,
                PositionX = -30000,
                PositionY = -30000,
            };
            using var source = new HwndSource(parameters) { RootVisual = host };
            host.UpdateLayout();
            var handle = source.Handle;

            Console.WriteLine($"==== {label}");
            Console.WriteLine("  初め: " + Count(handle));

            rows.Add(new RowVm("足した3"));
            host.UpdateLayout();
            Console.WriteLine("  1件足した後: " + Count(handle));

            rows.Clear();
            rows.Add(new RowVm("入れ替え1"));
            rows.Add(new RowVm("入れ替え2"));
            rows.Add(new RowVm("入れ替え3"));
            host.UpdateLayout();
            Console.WriteLine("  空にして3件入れた後: " + Count(handle));

            list.ItemsSource = new ObservableCollection<object> { new RecordVm("等しい"), new RecordVm("等しい"), new RecordVm("別") };
            host.UpdateLayout();
            Console.WriteLine("  等しい record 2件＋別1件に差し替えた後: " + Count(handle));

            list.ItemsSource = new List<object> { new RowVm("同名"), new RowVm("同名") };
            host.UpdateLayout();
            Console.WriteLine("  名前が同じ別の物2件: " + Count(handle));

            list.Visibility = Visibility.Collapsed;
            host.UpdateLayout();
            Count(handle);
            list.ItemsSource = new List<object> { new RowVm("隠れている間に入れた1"), new RowVm("隠れている間に入れた2") };
            list.Visibility = Visibility.Visible;
            host.UpdateLayout();
            Console.WriteLine("  畳んでいる間に差し替えて出した後: " + Count(handle));
        }
    }

    /// <summary>別スレッドのクライアントから、ボタンと入力欄を数える（画面のスレッドはその間メッセージを回す）。</summary>
    private static string Count(IntPtr handle)
    {
        var frame = new DispatcherFrame();
        var result = "";
        var worker = new Thread(() =>
        {
            try
            {
                var root = AutomationElement.FromHandle(handle);
                var buttons = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                var edits = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                result = $"ボタン {buttons.Count} 件（{string.Join("・", buttons.Cast<AutomationElement>().Select(b => b.Current.Name))}）/ 入力欄 {edits.Count} 件";
            }
            catch (Exception ex)
            {
                result = "失敗: " + ex.Message;
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
        return result;
    }
}
