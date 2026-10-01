namespace Chmonos.App.ViewModels;

/// <summary>
/// 商品のカード（<c>Views/ItemCardResources.xaml</c>）を並べる画面。カードの上の操作はここへ返す。
///
/// 右クリックのメニューは、同じ名前のコマンド（OpenBoothCommand・OpenShopCommand・CopyLinkCommand・EditItemCommand・
/// RevealCommand・HideItemCommand）をカードの Tag（＝この画面）から引く。検索画面とフォルダビューの右側で同じ操作にするため。
/// </summary>
public interface IItemCardHost
{
    /// <summary>カードを押した（選んでいないとき）。商品ページへ移る。</summary>
    void OpenItem(ItemCardViewModel card);

    /// <summary>中クリック。BOOTH の商品ページをブラウザで開く。</summary>
    void OpenBooth(ItemCardViewModel? card);

    /// <summary>お気に入りの星。</summary>
    Task ToggleFavoriteAsync(ItemCardViewModel card);
}
