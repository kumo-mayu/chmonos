using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace BoothAssetManager.App.Controls;

/// <summary>
/// 選ぶと Command を呼ぶ繋がったボタンの1区画（カード／リスト・アバターとして扱うか）。
///
/// これらは「点いているか」を画面の値から読むだけにして（OneWay）、変えるのは押したときの Command に任せている
/// （同じ GroupName の別の画面の区画が外れたときに、その画面の値まで書き換えないため）。
/// ところが素の RadioButton は、UI Automation（読み上げ・自動操作）で「選ぶ」と**点くだけで Command を呼ばない**。
/// 点いた見た目なのに一覧はそのまま、という食い違いになった（点検 2026-09-23：改変の「使ったもの」のカード／リスト）。
/// 選ぶ操作をクリックと同じ道に通し、どの手段で選んでも同じことが起きるようにする。
/// </summary>
public sealed class CommandRadioButton : RadioButton
{
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    internal void ClickFromAutomation() => OnClick();

    private sealed class Peer(CommandRadioButton owner) : RadioButtonAutomationPeer(owner), ISelectionItemProvider
    {
        void ISelectionItemProvider.Select()
        {
            if (!IsEnabled())
            {
                throw new ElementNotEnabledException();
            }

            owner.ClickFromAutomation();
        }

        // 1つの区画は「選ぶ」しか持たない。足す・外すは素の RadioButton と同じく受けない
        void ISelectionItemProvider.AddToSelection() => ((ISelectionItemProvider)this).Select();

        void ISelectionItemProvider.RemoveFromSelection() => throw new InvalidOperationException();

        bool ISelectionItemProvider.IsSelected => owner.IsChecked == true;

        IRawElementProviderSimple? ISelectionItemProvider.SelectionContainer => null;
    }
}
