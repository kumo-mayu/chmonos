using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;

namespace BoothAssetManager.App.Controls;

/// <summary>
/// 欄や行を開閉する三角。見た目と動きは ToggleButton のままで、読み上げ・自動操作に「開いている／畳んでいる」を渡す。
///
/// 素の ToggleButton は「オン／オフ」しか言わない。名前を「ローカルファイルを開く」と付けていたので、開いていても同じ名前で読まれ、
/// 確かめの道具は開いているのかを知らずに押して、開いている欄を畳んでいた（2026-09-30）。
/// 畳める欄の部品（Expander・<c>TriangleExpander</c>）は、名前に欄の名前を持ち、状態を UI Automation の開閉（ExpandCollapse）で渡している。
/// 同じ形に揃える：名前は欄・行の名前（「ローカルファイル」「（小分類名）の商品」）を付け、「開く」「畳む」は状態が言う。
/// 「切り替える」（Toggle）も今までどおり持つ。
/// </summary>
public sealed class ExpandToggle : ToggleButton
{
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        RaiseStateChanged(wasExpanded: false);
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        RaiseStateChanged(wasExpanded: true);
    }

    private void RaiseStateChanged(bool wasExpanded)
    {
        // 相手（読み上げ）がいるときだけ窓口がある。いなければ何もしない
        if (UIElementAutomationPeer.FromElement(this) is { } peer)
        {
            peer.RaisePropertyChangedEvent(
                ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                wasExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
                wasExpanded ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded);
        }
    }

    private sealed class Peer(ExpandToggle owner) : ToggleButtonAutomationPeer(owner), IExpandCollapseProvider
    {
        protected override string GetClassNameCore() => nameof(ExpandToggle);

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPattern(patternInterface);

        public ExpandCollapseState ExpandCollapseState =>
            owner.IsChecked == true ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

        public void Expand() => Set(true);

        public void Collapse() => Set(false);

        private void Set(bool expanded)
        {
            if (!IsEnabled())
            {
                throw new ElementNotEnabledException();
            }

            // 値を入れる。結び付けた先（ViewModel の開閉）へは、押したときと同じ道で届く
            owner.SetCurrentValue(IsCheckedProperty, expanded);
        }
    }
}
