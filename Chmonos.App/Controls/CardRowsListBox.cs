using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Chmonos.App.Controls;

/// <summary>
/// カードを段（<see cref="CardRowItems"/>）に切って縦に並べる一覧（検索・ショップ・ショップ一覧・フォルダの右）。
/// 見た目・キーボードの動き・仮想化は ListBox のままで、読み上げ・自動操作（UI Automation）にはカードを一覧の直下に出す。
///
/// WPF には仮想化する WrapPanel が無いので、幅から列数を決めてカードを段に切り、段を ListBox の行にしている。
/// ListBox の窓口は行を「一覧の項目」として出し、名前は行の ToString()——段の型の名前が読まれていた
/// （「…ViewModels.CardRow、入れ物、（商品名）」。2026-09-30 に、見えない窓に載せて木を書き出して確かめた）。
/// 段は選べず、止まれず、幅を変えれば中身が入れ替わる。人にも道具にも意味が無いので、木に出さない（ユーザ判断 2026-09-30）。
///
/// 行の窓口（ListBoxItem）は ListBox の窓口が作る物で、部品の側からは差し替えられない。一覧の窓口ごと替え、
/// 今作られている段の中を画面の木で下りて、窓口を持つ部品（カード）を集める。
/// 「選ぶ」の操作は渡さない（選ぶ物が無い）。流す操作は、ListBox と同じに中の ScrollViewer の物を渡す。
/// </summary>
public sealed class CardRowsListBox : ListBox
{
    private Panel? _itemsHost;

    public CardRowsListBox()
    {
        // 暗黙の見た目（Themes/Controls.xaml の ListBox）は、型がぴったり同じ部品にしか当たらない。ListBox の見た目を名指しで引く
        SetResourceReference(StyleProperty, typeof(ListBox));

        // カードの一覧は Tab で1回だけ入り、中は矢印でカードからカードへ移る（ユーザ判断 2026-10-01）。
        // 前は1枚ずつ Tab で止まり、検索の結果を抜けて下の「絞り込みを折りたたむ」へ行くのに、見えているカードの数だけ押した。
        // ListBox の既定（Once）は中を丸ごと1つにする WPF の仕組みで、並び（ArrowGroup）の「止まる物を1つにする」とは別。
        // 並びに任せるので Continue にする（Once のままだと、並びが選んだ止まり先ではなく、一覧が覚えた物へ入る）
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Continue);
        ArrowGroup.SetIsEnabled(this, true);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    /// <summary>段を並べている板。型が替わると作り直されるので、今もこの一覧の物かを見てから使う。</summary>
    private Panel? ItemsHost
    {
        get
        {
            if (_itemsHost is not { IsItemsHost: true } || !ReferenceEquals(GetItemsOwner(_itemsHost), this))
            {
                _itemsHost = FindItemsHost(this);
            }

            return _itemsHost;
        }
    }

    private Panel? FindItemsHost(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);

            // 段の中の板（カードを横に並べる板）も IsItemsHost なので、持ち主で見分ける
            if (child is Panel { IsItemsHost: true } panel && ReferenceEquals(GetItemsOwner(panel), this))
            {
                return panel;
            }

            if (FindItemsHost(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private sealed class Peer(CardRowsListBox owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

        // 型の名前が空だと、確かめで木を書き出したときに何の一覧か分からない
        protected override string GetClassNameCore() => nameof(CardRowsListBox);

        public override object? GetPattern(PatternInterface patternInterface)
        {
            if (patternInterface == PatternInterface.Scroll
                && ContentItemsControl.FindScrollViewer(owner) is { } scroll
                && UIElementAutomationPeer.CreatePeerForElement(scroll) is { } peer)
            {
                peer.EventsSource = this;
                return peer.GetPattern(patternInterface);
            }

            return base.GetPattern(patternInterface);
        }

        protected override List<AutomationPeer>? GetChildrenCore()
        {
            if (owner.ItemsHost is not { } host)
            {
                return null;
            }

            // 使い回す仮想化（Recycling）では、板の子の並びは画面の順と限らず、使い終わって次を待っている行も混ざる。
            // 今の行の番号を持つ物だけを、番号の順に並べる（待っている行は番号が -1）
            var rows = new List<(int Index, DependencyObject Row)>(host.Children.Count);
            foreach (UIElement row in host.Children)
            {
                var index = owner.ItemContainerGenerator.IndexFromContainer(row);
                if (index >= 0)
                {
                    rows.Add((index, row));
                }
            }

            rows.Sort((a, b) => a.Index.CompareTo(b.Index));

            var children = new List<AutomationPeer>();
            foreach (var (_, row) in rows)
            {
                Collect(row, children);
            }

            return children.Count > 0 ? children : null;
        }

        /// <summary>
        /// 画面の木を下り、窓口を持つ部品に当たったら足して、そこから先は下りない（その先は部品の窓口が自分で集める）。
        /// WPF の既定の集め方と同じ。行（ListBoxItem）そのものには窓口を聞かないので、行は木に出ない。
        /// 段（<see cref="CardRowItems"/>）も窓口を聞かずに素通りする
        /// </summary>
        private static void Collect(DependencyObject parent, List<AutomationPeer> into)
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is UIElement element and not CardRowItems && UIElementAutomationPeer.CreatePeerForElement(element) is { } peer)
                {
                    into.Add(peer);
                }
                else
                {
                    Collect(child, into);
                }
            }
        }
    }
}
