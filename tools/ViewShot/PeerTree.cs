using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;

namespace ViewShot;

/// <summary>
/// 読み上げ・自動操作の窓口（AutomationPeer）の木を、文字で書き出す。
///
/// 名前を付ける・押せる枠に替える・一覧の部品を替える、のような直しは見た目を変えないので、画像では確かめられない。
/// アプリを起動して UI Automation で読めば分かるが、画面を占有する。窓口は部品が自分で作る物なので、
/// 載せた部品から直にたどれば、相手（読み上げソフト）が受け取る名前・型・操作が分かる。
/// 直す前と後で書き出して、文字の差で比べる。
///
/// **相手の側から見た木そのものではない。**相手は「操作できる部品だけ」の見方で見るので、ここに出ていても相手には見えない物がある
/// （画面の外・隠している部品）。その印（画面の外）を行に出す。メニューや吹き出しのように別の窓に出る物は、ここには出ない
/// </summary>
internal static class PeerTree
{
    private static readonly (PatternInterface Pattern, string Label)[] Patterns =
    [
        (PatternInterface.Invoke, "押す"),
        (PatternInterface.Toggle, "切り替え"),
        (PatternInterface.ExpandCollapse, "開閉"),
        (PatternInterface.SelectionItem, "選ぶ"),
        (PatternInterface.Scroll, "流す"),
        (PatternInterface.Value, "値"),
    ];

    public static void Write(FrameworkElement root, TextWriter output)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(root);
        if (peer is null)
        {
            // 窓口を持たない入れ物（Border・Grid）。中の部品から始める
            foreach (var child in ChildrenOf(root))
            {
                Write(child, output, 0);
            }

            return;
        }

        Write(peer, output, 0);
    }

    private static IEnumerable<AutomationPeer> ChildrenOf(FrameworkElement element)
    {
        // FrameworkElementAutomationPeer を仮に被せると、その下の窓口を WPF と同じ決まりで集めてくれる
        return new FrameworkElementAutomationPeer(element).GetChildren() ?? [];
    }

    private static void Write(AutomationPeer peer, TextWriter output, int depth)
    {
        var patterns = Patterns.Where(entry => peer.GetPattern(entry.Pattern) is not null).Select(entry => entry.Label).ToList();
        var parts = new List<string> { peer.GetAutomationControlType().ToString() };
        if (peer.GetClassName() is { Length: > 0 } className)
        {
            parts.Add($"({className})");
        }

        parts.Add($"「{peer.GetName()}」");
        if (peer.GetAutomationId() is { Length: > 0 } id)
        {
            parts.Add($"#{id}");
        }

        if (patterns.Count > 0)
        {
            parts.Add("[" + string.Join("・", patterns) + "]");
        }

        if (!peer.IsEnabled())
        {
            parts.Add("押せない");
        }

        if (peer.IsOffscreen())
        {
            parts.Add("画面の外");
        }

        if (!peer.IsControlElement())
        {
            parts.Add("操作できる部品ではない");
        }

        output.WriteLine(new string(' ', depth * 2) + string.Join(" ", parts));
        foreach (var child in peer.GetChildren() ?? [])
        {
            Write(child, output, depth + 1);
        }
    }
}
