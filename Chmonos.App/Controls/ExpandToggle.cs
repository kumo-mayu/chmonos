using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;

namespace Chmonos.App.Controls;

/// <summary>
/// 欄や行を開閉する三角。見た目と動きは ToggleButton のままで、読み上げ・自動操作に「開いている／畳んでいる」を渡す。
///
/// 素の ToggleButton は「オン／オフ」しか言わない。名前を「ローカルファイルを開く」と付けていたので、開いていても同じ名前で読まれ、
/// 確かめの道具は開いているのかを知らずに押して、開いている欄を畳んでいた（2026-09-30）。
///
/// 置き場所は、何の開閉かだけを <see cref="Subject"/> に渡す（「ローカルファイル」「（小分類名）の商品」）。
/// - 名前は、押すと起きることを状態から作る：「（欄）を開く」⇄「（欄）を折りたたむ」（ナビ・検索の絞り込み・商品ページのバリエーションと同じ言い方）
/// - 開いているかは、UI Automation の開閉（ExpandCollapse）でも渡す。「開く」「畳む」を名指しで呼べるので、開いている物を押して畳む事故が起きない
/// - 「切り替える」（Toggle）も今までどおり持つ
/// </summary>
public sealed class ExpandToggle : ToggleButton
{
    public static readonly DependencyProperty SubjectProperty = DependencyProperty.Register(
        nameof(Subject), typeof(string), typeof(ExpandToggle),
        new FrameworkPropertyMetadata(string.Empty, (element, _) => ((ExpandToggle)element).UpdateName()));

    /// <summary>何を開閉するか（欄・行の名前）。読み上げの名前は、これと今の状態から作る。</summary>
    public string Subject
    {
        get => (string)GetValue(SubjectProperty);
        set => SetValue(SubjectProperty, value);
    }

    /// <summary>開閉の部品の名前。開閉をボタンで作った所（フォルダの木の印）も同じ言い方にするので、ここで1つにする。</summary>
    public static string NameFor(string subject, bool isExpanded) => isExpanded ? $"{subject}を折りたたむ" : $"{subject}を開く";

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        UpdateName();
        RaiseStateChanged(wasExpanded: false);
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        UpdateName();
        RaiseStateChanged(wasExpanded: true);
    }

    private void UpdateName()
    {
        // 置き場所が名前を直に付けている（Subject を渡していない）ときは触らない
        if (Subject.Length > 0)
        {
            SetCurrentValue(AutomationProperties.NameProperty, NameFor(Subject, IsChecked == true));
        }
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
