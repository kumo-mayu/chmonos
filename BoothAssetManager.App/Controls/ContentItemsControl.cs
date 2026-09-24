using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace BoothAssetManager.App.Controls;

/// <summary>
/// 中の部品が UI Automation（読み上げ・自動操作）から見える ItemsControl。
///
/// 素の ItemsControl は項目を「データの項目」として出し、その中身は項目の入れ物（ContentPresenter）を通して探す。
/// 入れ物は自分の窓口を持たないので、**項目の中の入力欄・チェック・ボタンが1つも見えなくなる**
/// （検索の絞り込みの条件で、入力欄が1つも見つからなかった）。
/// 自分をふつうの部品として出せば、中身は画面の木をたどって見つかる。選ぶ一覧（ListBox）には使わない。
///
/// テンプレートに ScrollViewer を持たせた（仮想化した）ときは、流す操作もこの窓口から渡す。
/// テンプレートの中の ScrollViewer は自分を部品として名乗らないので、渡さないと一覧を流す手段が
/// UI Automation から消える（管理の画面の一覧を仮想化したとき、確かめの道具が一覧を送れなくなった。2026-09-24）。
/// ListBox が自分の窓口から流す操作を渡しているのと同じ形。
/// </summary>
public sealed class ContentItemsControl : ItemsControl
{
    protected override AutomationPeer OnCreateAutomationPeer() => new ContentItemsControlAutomationPeer(this);

    /// <summary>テンプレートの中の ScrollViewer（項目の中の物は数えない）。</summary>
    internal ScrollViewer? ScrollHost => FindScrollViewer(this);

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer scroll)
            {
                return scroll;
            }

            // 項目の中まで下りない（項目の中の流せる部品を一覧のものと取り違える）
            if (child is ItemsPresenter)
            {
                continue;
            }

            if (FindScrollViewer(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private sealed class ContentItemsControlAutomationPeer(ContentItemsControl owner) : FrameworkElementAutomationPeer(owner)
    {
        public override object? GetPattern(PatternInterface patternInterface)
        {
            if (patternInterface == PatternInterface.Scroll
                && owner.ScrollHost is { } scroll
                && UIElementAutomationPeer.CreatePeerForElement(scroll) is { } peer)
            {
                peer.EventsSource = this;
                return peer.GetPattern(patternInterface);
            }

            return base.GetPattern(patternInterface);
        }
    }
}
