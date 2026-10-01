using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chmonos.App.Controls;

/// <summary>
/// 中の部品が UI Automation（読み上げ・自動操作）から見える ItemsControl。
///
/// 素の ItemsControl は項目を「データの項目」として出し、その中身は項目の入れ物（ContentPresenter）を通して探す。
/// 入れ物は自分の窓口を持たないので、**項目の中の入力欄・チェック・ボタンが1つも見えなくなる**
/// （検索の絞り込みの条件で、入力欄が1つも見つからなかった）。
/// 自分をふつうの部品として出せば、中身は画面の木をたどって見つかる。選ぶ一覧（ListBox）には使わない。
///
/// 見えない窓に載せて UI Automation の側から測ると（2026-09-30）、素の ItemsControl にはほかに2つ困ることがあった：
/// 行が「データの項目」として出て、その名前が行の ToString()（行の型の名前）になる——読み上げに内部の名前が読まれる。
/// 値の等しい行（同じ文字列・等しい record）は1つにまとめられ、2つめ以降の中のボタンが見えない
/// （「同じ」3行で、見えたボタンは1つ）。繰り返しの一覧は、素の ItemsControl ではなくこれを使う（画面の決め事 ui-input.md）。
///
/// テンプレートに ScrollViewer を持たせた（仮想化した）ときは、流す操作もこの窓口から渡す。
/// テンプレートの中の ScrollViewer は自分を部品として名乗らないので、渡さないと一覧を流す手段が
/// UI Automation から消える（管理の画面の一覧を仮想化したとき、確かめの道具が一覧を送れなくなった。2026-09-24）。
/// ListBox が自分の窓口から流す操作を渡しているのと同じ形。
/// </summary>
public sealed class ContentItemsControl : ItemsControl
{
    static ContentItemsControl()
    {
        // 並べるだけの入れ物なので、Tab で止まらない。既定のままだと、一覧ごとに「何も起きない止まり」が1つ入り、
        // キーボードのフォーカスがどこにも見えなくなる（札の並び・行の中の札の一覧で、Tab を1回余分に押すことになっていた。2026-09-30）。
        // 中の部品（ボタン・入力欄・止まれる札）には、今までどおり順に止まる
        FocusableProperty.OverrideMetadata(typeof(ContentItemsControl), new FrameworkPropertyMetadata(false));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ContentItemsControlAutomationPeer(this);

    /// <summary>テンプレートの中の ScrollViewer（項目の中の物は数えない）。</summary>
    internal ScrollViewer? ScrollHost => FindScrollViewer(this);

    internal static ScrollViewer? FindScrollViewer(DependencyObject parent)
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
        // 型の名前が空だと、確かめで木を書き出したときに何の入れ物か分からない
        protected override string GetClassNameCore() => nameof(ContentItemsControl);

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
