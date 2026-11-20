using System.Windows;
using System.Windows.Controls;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>
/// カードの升の見た目を選ぶ：商品はカード、並べ替えの区切りの札（<see cref="SortDivider"/>）は札。
/// 型ごとの暗黙の見た目（DataType）にしないのは、カードの見た目が名前付きの資源（<c>ItemCardTemplate</c>）で、
/// 段（<c>CardRowItems</c>）の ItemTemplate に名指しで当てているため。名指しのままにすると札にもカードの見た目が当たる
/// </summary>
public sealed class CardSlotTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Card { get; set; }

    public DataTemplate? Divider { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        => item is SortDivider ? Divider : Card;
}

/// <summary>
/// リストの行の見た目を選ぶ：商品は列に並ぶ行、札は列をまたぐ1本の行。
/// 札の行は押す・右クリック・キーボードの止まりを持たない（<c>ItemCardResources.xaml</c> の <c>ItemListDividerRowStyle</c>）
/// </summary>
public sealed class ItemListRowStyleSelector : StyleSelector
{
    public Style? Row { get; set; }

    public Style? Divider { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container)
        => item is SortDivider ? Divider : Row;
}

/// <summary>
/// アバターの管理の「変換」の左の流れ：先頭の追加した組の節（<see cref="ConversionPairsSection"/>）と、プロファイルの商品の行・群で見た目を分ける。
/// 節は選ばない入れ物で出し、節の群には見出しを付けない（探す欄より下を1つの流れにするため、節を一覧の1行として持つ。ユーザ判断 2026-11-20・①）。
/// 行（ListBoxItem の入れ物）と群（GroupItem の入れ物）の両方に使う
/// </summary>
public sealed class ConversionListStyleSelector : StyleSelector
{
    public Style? Section { get; set; }

    public Style? Other { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container)
        => item is ConversionPairsSection or System.Windows.Data.CollectionViewGroup { Name: ConversionPairsSection } ? Section : Other;
}
