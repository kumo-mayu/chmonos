using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Chmonos.App.Controls;

/// <summary>
/// 並べ替えの区切りの札（カードの升に入る方）の枠。見た目は Border のまま、読み上げ・自動操作には「文字」1つとして出す。
///
/// 素の Border は窓口を持たないので、カードの一覧の窓口（<see cref="CardRowsListBox"/>）が中を下りて、
/// 札の中の文字（項目の名前・親・名前・件数）を4つの別々の文字として並べてしまう。名前（「カテゴリ：3Dモデル / 衣装、12 件」）を持つ1つにまとめ、中は出さない。
/// 押せる物ではない（札は商品ではなく、押しても何も起きない）ので「押す」は持たせない。キーボードでも止まらない
/// </summary>
public sealed class SortDividerBorder : Border
{
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(SortDividerBorder owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(SortDividerBorder);

        protected override List<AutomationPeer>? GetChildrenCore() => null;
    }
}
