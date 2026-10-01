using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Chmonos.App.Controls;

/// <summary>
/// 商品のリスト（1行ずつの表示）。見た目と動きは ListView のままで、行に読み上げ・自動操作の「押す」を足す。
///
/// 素の ListView の行は「選ぶ」しか持たない。このリストの行は選ぶ物ではなく、押すと商品ページへ移る（フォルダの行はそのフォルダへ）。
/// 読み上げ・自動操作で行を「選ぶ」と何も起きず、行に止めて Enter を送るしか移る手が無かった（2026-09-30）。
/// 「押す」が来たら行に <see cref="RowInvokedEvent"/> を上げ、置き場所（行のスタイル）がキーボードの Enter と同じ処理につなぐ。
/// カードの側は <see cref="ItemCardBorder"/> が同じことをしている
/// </summary>
public sealed class ItemListView : ListView
{
    /// <summary>行に「押す」が来た。行（ListViewItem）の上で上がる。</summary>
    public static readonly RoutedEvent RowInvokedEvent = EventManager.RegisterRoutedEvent(
        "RowInvoked", RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(ItemListView));

    public event RoutedEventHandler RowInvoked
    {
        add => AddHandler(RowInvokedEvent, value);
        remove => RemoveHandler(RowInvokedEvent, value);
    }

    public ItemListView()
    {
        // 暗黙の見た目（Themes/Controls.xaml の ListView）は、型がぴったり同じ部品にしか当たらない。
        // 継いだ型のままだと文字の色の指定が外れ、暗い表で行の文字が既定の黒になる。ListView の見た目を名指しで引く
        SetResourceReference(StyleProperty, typeof(ListView));
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        var peer = new Peer(this);

        // 素の ListView は、列の見出しと行の窓口を View（GridView）に作らせている。同じ物を渡す
        if (View is GridView grid)
        {
            peer.UseView(new GridViewAutomationPeer(grid, this));
        }

        return peer;
    }

    private sealed class Peer(ItemListView owner) : ListViewAutomationPeer(owner)
    {
        internal void UseView(IViewAutomationPeer view) => ViewAutomationPeer = view;

        protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => new RowPeer(item, this, owner);
    }

    private sealed class RowPeer(object item, ListViewAutomationPeer list, ItemListView owner)
        : GridViewItemAutomationPeer(item, list), IInvokeProvider
    {
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

        void IInvokeProvider.Invoke()
        {
            if (!IsEnabled())
            {
                throw new ElementNotEnabledException();
            }

            // 仮想化で画面の外へ出た行は、部品がもう無い
            if (owner.ItemContainerGenerator.ContainerFromItem(Item) is not ListViewItem row)
            {
                throw new ElementNotAvailableException();
            }

            // 押した先が窓を出しても呼んだ側を待たせないよう、呼び出しを返してから動かす（既定のボタンと同じ）
            owner.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => row.RaiseEvent(new RoutedEventArgs(RowInvokedEvent, row)));
        }
    }
}
