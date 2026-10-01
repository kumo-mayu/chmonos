namespace Chmonos.App.ViewModels;

/// <summary>
/// 商品のカードを持っている行（改変の「使ったもの」の行など）。
///
/// **カードの右クリック（`ItemCardResources` の `CardMenu`）を、カードそのものでない行からも使うため**
/// （ユーザ指示 2026-09-20・M1：改変はカード表示だと右クリックが出て、リスト表示だと出なかった）。
/// メニューは押した行を `CommandParameter` で渡すので、受け取る側はこの入れ物からカードを取り出す
/// （<see cref="SearchViewModel.AsCard"/>）。
/// </summary>
internal interface IHasItemCard
{
    /// <summary>その行の商品のカード。手元に無い商品（記録だけ残る行）は null。</summary>
    ItemCardViewModel? Card { get; }
}
