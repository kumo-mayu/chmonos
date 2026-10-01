using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Chmonos.App.Controls;

/// <summary>
/// 行を押すと決まる一覧（候補付きの入力欄の候補）。見た目と動きは ListBox のままで、行に読み上げ・自動操作の「押す」を足す。
///
/// 素の ListBox の行は「選ぶ」しか持たない。候補の行は、クリックか Enter で決まる物なので、
/// 読み上げ・自動操作で行を「選ぶ」と色が付くだけで何も決まらなかった（2026-09-30。確かめの道具は欄に打って Enter を送るしか無かった）。
/// 「押す」が来たら <see cref="RowInvoked"/> を上げ、置き場所がクリックと同じ処理につなぐ。商品のリストの <see cref="ItemListView"/> と同じ考え
/// </summary>
public sealed class InvokableListBox : ListBox
{
    /// <summary>行に「押す」が来た。引数はその行の項目。</summary>
    public event Action<object>? RowInvoked;

    public InvokableListBox()
    {
        // 暗黙の見た目（Themes/Controls.xaml の ListBox）は、型がぴったり同じ部品にしか当たらない。ListBox の見た目を名指しで引く
        SetResourceReference(StyleProperty, typeof(ListBox));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(InvokableListBox owner) : ListBoxAutomationPeer(owner)
    {
        protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => new RowPeer(item, this, owner);
    }

    private sealed class RowPeer(object item, SelectorAutomationPeer list, InvokableListBox owner)
        : ListBoxItemAutomationPeer(item, list), IInvokeProvider
    {
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

        void IInvokeProvider.Invoke()
        {
            if (!IsEnabled())
            {
                throw new ElementNotEnabledException();
            }

            // 押した先が窓を出しても呼んだ側を待たせないよう、呼び出しを返してから動かす（既定のボタンと同じ）
            var row = Item;
            owner.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => owner.RowInvoked?.Invoke(row));
        }
    }
}
