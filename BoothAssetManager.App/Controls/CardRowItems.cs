using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace BoothAssetManager.App.Controls;

/// <summary>
/// カードを横に並べる「段」。窓の幅で決まる並べ方の都合の入れ物で、人にとって意味のある単位ではない
/// （幅を変えれば、同じカードが別の段に入る）。
///
/// 読み上げ・自動操作（UI Automation）には「操作できる部品」として出さない（ユーザ判断 2026-09-30）。
/// 出していた頃は、段が名前の無い入れ物として木に1段はさまり、読み上げは「入れ物、カード」と読んだ。
/// 操作できる部品でない物は、相手の既定の見方（操作できる部品だけ）では飛ばされ、中のカードが段の親の直下に出る。
///
/// 窓口そのものを無くすことはできない：ItemsControl を継いだ部品が窓口を返さないと、WPF は既定の窓口
/// （行を「データの項目」として型の名前で出す物）を代わりに作る（試験で確かめた）。自前の窓口を持たせて、印だけ外す。
/// カードの一覧（<see cref="CardRowsListBox"/>）は、この部品を丸ごと飛ばしてカードを集めるので、そちらでは木にも出ない。
///
/// 見た目と並べ方は ItemsControl のまま。Tab で止まらないのは <see cref="ContentItemsControl"/> と同じ理由
/// （並べるだけの入れ物に「何も起きない止まり」を作らない）。
/// 段でない繰り返しの一覧（札の並び・行の中のボタン）は <see cref="ContentItemsControl"/> を使う。
/// </summary>
public sealed class CardRowItems : ItemsControl
{
    static CardRowItems()
    {
        FocusableProperty.OverrideMetadata(typeof(CardRowItems), new FrameworkPropertyMetadata(false));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(CardRowItems owner) : FrameworkElementAutomationPeer(owner)
    {
        // 型の名前が空だと、確かめで木を全部書き出したときに何の入れ物か分からない
        protected override string GetClassNameCore() => nameof(CardRowItems);

        protected override bool IsControlElementCore() => false;

        protected override bool IsContentElementCore() => false;
    }
}
