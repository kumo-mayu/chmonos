using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Chmonos.App.Controls;

namespace ViewShot;

/// <summary>
/// 場面を Tab で一周して、止まった所を順に書き出す（<c>ViewShot tabs &lt;場面&gt;</c>）。<c>--keys</c> を渡すと、その順にキーを送って止まった所を書く。
///
/// 止まらない操作（商品ページの星・畳む三角）と、件数分止まる並びは、絵にも窓口の木にも出ない。
/// 見えない窓に載せ、Tab をキーの知らせとして入力の口から送って、一周するか上限まで進む（動かなければ WPF の「次へ進む」を直に呼ぶ）。
/// ほかのキーも入力の口から送る。実際のキーは押さない。
/// アプリの組み立てごと載る場面（商品ページ・要確認・改変の画面）でも動くので、作り物のデータを組む PeerProbe の focus では届かない画面を通しで見られる
/// </summary>
internal static class TabWalk
{
    private const int MaxStops = 400;

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);

    public static void Write(Stage stage, FrameworkElement root, string? from, IReadOnlyList<string> keys, TextWriter output)
    {
        // このスレッドの中でのフォーカスを見えない窓へ移す（ほかのアプリの前面の窓は変わらない）
        SetFocus(stage.Handle);
        Idle(root);
        if (from is not null)
        {
            if (FindById(root, from) is not { } start)
            {
                output.WriteLine($"ID {from} の部品が見つからない");
                return;
            }

            start.Focus();
        }
        else
        {
            root.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }

        Idle(root);
        if (Keyboard.FocusedElement is null)
        {
            output.WriteLine("フォーカスを受けた部品が無い（見えない窓がフォーカスを取れなかった）");
            return;
        }

        if (keys.Count > 0)
        {
            Script(stage, root, keys, output);
        }
        else
        {
            Walk(root, output);
        }
    }

    private static void Walk(FrameworkElement root, TextWriter output)
    {
        var first = Keyboard.FocusedElement;
        var count = 0;
        var outside = 0;
        for (var stop = 1; stop <= MaxStops; stop++)
        {
            var focused = Keyboard.FocusedElement;

            // 場面の外（主の窓のナビなど）は数えるが、見たい所の中だけ詳しく書く
            if (IsInside(root, focused))
            {
                count++;
                output.WriteLine($"{count,3}  {Describe(focused)}");
            }
            else
            {
                outside++;
            }

            PressTab((HwndSource)PresentationSource.FromVisual(root)!);
            if (ReferenceEquals(Keyboard.FocusedElement, focused))
            {
                break;
            }

            Idle(root);
            if (ReferenceEquals(Keyboard.FocusedElement, first))
            {
                break;
            }
        }

        output.WriteLine($"見たい所の中で止まった数: {count}（外で止まった数: {outside}）");
    }

    private static void Script(Stage stage, FrameworkElement root, IReadOnlyList<string> keys, TextWriter output)
    {
        var source = (HwndSource)PresentationSource.FromVisual(root)!;
        output.WriteLine($"最初     → {Describe(Keyboard.FocusedElement)}");
        foreach (var step in keys)
        {
            switch (step)
            {
                case "Tab":
                    PressTab(source);
                    break;
                case "ShiftTab":
                    Next(Keyboard.FocusedElement, FocusNavigationDirection.Previous);
                    break;
                default:
                    // PreviewKeyDown を送ると、受けられなかったときは入力の口が KeyDown に進めて送る（実際のキーと同じ）
                    var key = Enum.Parse<Key>(step);
                    InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyUpEvent });
                    break;
            }

            Idle(root);
            var where = IsInside(root, Keyboard.FocusedElement) ? string.Empty : "  （見たい所の外）";
            output.WriteLine($"{step,-8} → {Describe(Keyboard.FocusedElement)}{where}");
        }
    }

    /// <summary>
    /// Tab をキーの知らせとして送る（並びの ArrowGroup は、見える分だけ作る一覧の中で Tab を受けて行き先を作らせる。
    /// 「次へ進む」を直に呼ぶと、その受け口を通らない）。WPF の Tab の移動もキーの知らせで動く。
    /// フォーカスが動かなかったときだけ「次へ進む」を直に呼ぶ（一周の終わりなど）。Shift は実際のキーの状態を読むので作れず、ShiftTab は直に呼ぶ
    /// </summary>
    private static void PressTab(HwndSource source)
    {
        var before = Keyboard.FocusedElement;
        InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Tab) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Tab) { RoutedEvent = Keyboard.PreviewKeyUpEvent });
        if (ReferenceEquals(Keyboard.FocusedElement, before))
        {
            Next(before, FocusNavigationDirection.Next);
        }
    }

    private static bool Next(IInputElement? focused, FocusNavigationDirection direction) => focused switch
    {
        UIElement element => element.MoveFocus(new TraversalRequest(direction)),
        ContentElement element => element.MoveFocus(new TraversalRequest(direction)),
        _ => false,
    };

    private static bool IsInside(FrameworkElement root, IInputElement? focused)
        => focused is Visual visual && root.IsAncestorOf(visual) || focused is ContentElement;

    private static FrameworkElement? FindById(DependencyObject root, string id)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element && AutomationProperties.GetAutomationId(element) == id && element.IsVisible)
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

    private static string Describe(IInputElement? element)
    {
        if (element is not DependencyObject node)
        {
            return "（無し）";
        }

        var name = AutomationProperties.GetName(node);
        if (string.IsNullOrEmpty(name))
        {
            name = node switch
            {
                ContentControl { Content: string text } => text,
                Hyperlink link => new TextRange(link.ContentStart, link.ContentEnd).Text,
                TextBox => "（入力欄）",
                _ => string.Empty,
            };
        }

        var id = AutomationProperties.GetAutomationId(node);
        var frame = node is FrameworkElement { FocusVisualStyle: null } and not TextBox ? "  枠=無し" : string.Empty;
        var group = node is UIElement member && ArrowGroup.OwnsArrows(member) ? "  [並び：矢印で中を移る]" : string.Empty;
        return $"{node.GetType().Name}「{name}」{(id.Length > 0 ? "#" + id : string.Empty)}{frame}{group}";
    }

    private static void Idle(FrameworkElement root) => root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
