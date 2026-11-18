namespace Chmonos.App.ViewModels;

/// <summary>
/// ギャラリーの部品（<see cref="Views.ItemGalleryPanel"/>）を置く画面。商品（商品ページ・編集画面）と改変で同じ部品を使う
/// （ユーザ指示 2026-09-13：改変の写真も「ほかの箇所のギャラリーUIと同じに」）。
/// 部品の裏側が画面に頼むのは、サムネイルに乗ったときの切り替えだけ。ほかは同じ名前の値とコマンドに結び付く
/// </summary>
public interface IGalleryHost
{
    /// <summary>サムネイル一覧にマウスを乗せるだけで切り替えるか。設定で変えられる。</summary>
    bool SwitchOnHover { get; }

    /// <summary>乗ってから切り替わるまでの滞留時間（ミリ秒）。</summary>
    int HoverDelayMs { get; }

    void HoverImage(GalleryImage image);

    /// <summary>
    /// 小さな絵の並びを畳んでいるか。畳んだかは次に起動した時まで覚える（ユーザ判断 2026-11-19・open.md 61）。
    /// 商品ページ・編集・改変で1つ（画面ごとに分けない。数十枚の商品で畳みたい理由は、どの画面でも同じ）。
    /// ui-state.json の collapsedPanes に <see cref="GalleryFoldKey.Tiles"/> で載る（左の一覧の畳みと同じ欄）
    /// </summary>
    bool GalleryTilesFolded { get; set; }
}

public static class GalleryFoldKey
{
    public const string Tiles = "gallery.tiles";
}
