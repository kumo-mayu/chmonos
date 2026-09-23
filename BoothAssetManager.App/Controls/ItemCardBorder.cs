using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace BoothAssetManager.App.Controls;

/// <summary>
/// 商品のカードの枠。見た目は Border のままで、読み上げ・自動操作から「押せる1件」として見えるようにする。
///
/// 素の Border は UI Automation に出ないので、キーボードで止まっても何に止まったのか読み上げられず、
/// 自動操作からもカードを掴めなかった（点検 2026-09-23）。「押す」は画面の側（<see cref="Invoked"/>）が
/// マウスで押したときと同じ処理にする
/// </summary>
public sealed class ItemCardBorder : Border
{
    /// <summary>読み上げ・自動操作から「押す」が来た。</summary>
    public event EventHandler? Invoked;

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(ItemCardBorder owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

        protected override string GetClassNameCore() => nameof(ItemCardBorder);

        protected override bool IsControlElementCore() => true;

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

        void IInvokeProvider.Invoke()
        {
            if (!IsEnabled())
            {
                throw new ElementNotEnabledException();
            }

            owner.Invoked?.Invoke(owner, EventArgs.Empty);
        }
    }
}
