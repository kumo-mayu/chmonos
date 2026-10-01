using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Chmonos.App.Controls;

/// <summary>
/// 束（見出しでまとめた行）のある一覧。見た目と動きは ListBox のままで、開いている束の見出しの中のボタンを読み上げ・自動操作に出す。
///
/// WPF は、束（GroupItem）が開いている間、束の子として行だけを渡す。見出しの中のボタン（未確定の「まとめて扱う」「元zipとして扱う」
/// 「このフォルダを選択」）は、名前も ID も付いているのに、束を畳んでいる間しか木に出なかった（2026-09-30。見えない窓に載せて木を書き出して確かめた）。
/// 束の窓口は WPF が作る物で差し替えられないので、一覧の側で、開いている束のすぐ後ろに見出しの中の部品を並べて渡す。
/// 畳んでいる束は WPF が見出しごと渡すので、足さない（2回出さない）。
/// </summary>
public sealed class GroupedListBox : ListBox
{
    public GroupedListBox()
    {
        // 暗黙の見た目（Themes/Controls.xaml の ListBox）は、型がぴったり同じ部品にしか当たらない。ListBox の見た目を名指しで引く
        SetResourceReference(StyleProperty, typeof(ListBox));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(GroupedListBox owner) : ListBoxAutomationPeer(owner)
    {
        protected override List<AutomationPeer>? GetChildrenCore()
        {
            var children = base.GetChildrenCore();
            if (children is null)
            {
                return null;
            }

            var result = new List<AutomationPeer>(children.Count);
            foreach (var child in children)
            {
                result.Add(child);
                if (child is GroupItemAutomationPeer { Owner: GroupItem group })
                {
                    result.AddRange(HeaderParts(group));
                }
            }

            return result;
        }

        /// <summary>開いている束の、見出しの中の部品（文字は除く）。</summary>
        private static IEnumerable<AutomationPeer> HeaderParts(GroupItem group)
        {
            if (Find<Expander>(group) is not { IsExpanded: true } expander || Find<ToggleButton>(expander) is not { } header)
            {
                yield break;
            }

            // 見出しの押す所そのものは渡さない。WPF が束の窓口に結び付けていて（開閉の知らせを束から出すため）、渡すと束がもう1つ出る
            if (UIElementAutomationPeer.CreatePeerForElement(header)?.GetChildren() is not { } parts)
            {
                yield break;
            }

            foreach (var part in parts)
            {
                if (part.GetAutomationControlType() != AutomationControlType.Text)
                {
                    yield return part;
                }
            }
        }

        private static T? Find<T>(DependencyObject root)
            where T : DependencyObject
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is T found)
                {
                    return found;
                }

                // 行の一覧（ItemsPresenter）の中は探さない。束の中の行の部品を見出しと取り違えない
                if (child is not ItemsPresenter && Find<T>(child) is { } nested)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
