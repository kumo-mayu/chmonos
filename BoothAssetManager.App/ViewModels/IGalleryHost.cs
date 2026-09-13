namespace BoothAssetManager.App.ViewModels;

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
}
