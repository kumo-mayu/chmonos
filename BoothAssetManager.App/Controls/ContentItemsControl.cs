using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace BoothAssetManager.App.Controls;

/// <summary>
/// 中の部品が UI Automation（読み上げ・自動操作）から見える ItemsControl。
///
/// 素の ItemsControl は項目を「データの項目」として出し、その中身は項目の入れ物（ContentPresenter）を通して探す。
/// 入れ物は自分の窓口を持たないので、**項目の中の入力欄・チェック・ボタンが1つも見えなくなる**
/// （検索の絞り込みの条件で、入力欄が1つも見つからなかった）。
/// 自分をふつうの部品として出せば、中身は画面の木をたどって見つかる。選ぶ一覧（ListBox）には使わない。
/// </summary>
public sealed class ContentItemsControl : ItemsControl
{
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
}
