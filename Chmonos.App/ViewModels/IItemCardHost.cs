namespace Chmonos.App.ViewModels;

/// <summary>
/// 商品のカード（<c>Views/ItemCardResources.xaml</c>）を並べる画面。カードの上の操作はここへ返す。
///
/// 右クリックのメニューは、カードの Tag（＝この画面）の <see cref="MenuCommands"/> から命令を引く。
/// 検索画面・フォルダビューの右側・ショップ・改変・アバター・タグと属性の管理で同じ操作にするため。
/// </summary>
public interface IItemCardHost
{
    /// <summary>カードを押した（選んでいないとき）。商品ページへ移る。</summary>
    void OpenItem(ItemCardViewModel card);

    /// <summary>中クリック。BOOTH の商品ページをブラウザで開く。</summary>
    void OpenBooth(ItemCardViewModel? card);

    /// <summary>お気に入りの星。</summary>
    Task ToggleFavoriteAsync(ItemCardViewModel card);

    /// <summary>
    /// 右クリックのメニューが押す命令の一式。画面ごとに名前で11本を写していたのをやめ、この1つに結ぶ（R1）。
    /// 実装を書き忘れると組み立てが落ちる
    /// </summary>
    CardMenuCommands MenuCommands { get; }
}
