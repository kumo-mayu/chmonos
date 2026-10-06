namespace Chmonos.App.ViewModels;

/// <summary>
/// 右に商品ページを組み込んで出す画面（フォルダビュー・改変の画面）。主画面が、裏で起きたこと
/// （対応アバターの判定が終わった、など）を、組み込んだ商品ページへも届けるための口。
/// </summary>
internal interface IEmbeddedItemPageHost
{
    /// <summary>今、右に出している商品ページ。出していなければ null。</summary>
    ItemViewModel? EmbeddedItemPage { get; }
}
