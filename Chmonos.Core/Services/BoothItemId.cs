using BoothZipInspector;

namespace Chmonos.Core.Services;

/// <summary>
/// 商品IDの入力を1つの規則で読む。
///
/// 商品IDを打つ場面では、ブラウザから来るのはURLの方が普通なので、
/// どの入力欄でもURLをそのまま貼れるようにする。
/// ID欄ごとに書き方が違うと、ここでは貼れる／ここでは貼れない、を覚える羽目になる。
/// </summary>
public static class BoothItemId
{
    /// <summary>
    /// 数字だけならそのまま、BOOTHのURLならそこから商品IDを取り出す。
    /// どちらでもなければ null。
    ///
    /// URLは1本きりでなくてよい。**文章の中に混じっていても拾う。**
    /// 実際に落ちてくるのは、リンクだけとは限らない
    /// （文章の選択をドラッグすればその文が来るし、HTMLの断片が来ることもある）。
    ///
    /// 拾える形：
    /// <list type="bullet">
    /// <item>商品ページ <c>booth.pm/ja/items/123</c> / <c>shop.booth.pm/items/123</c></item>
    /// <item>商品画像 <c>booth.pximg.net/c/.../i/123/....jpg</c>（絵をドラッグしたとき）</item>
    /// <item>配布ファイル <c>s6.booth.pm/&lt;uuid&gt;/f/123/456/name.zip</c></item>
    /// </list>
    /// </summary>
    public static string? Parse(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.All(char.IsAsciiDigit))
        {
            return trimmed;
        }

        return BoothUrlExtractor.FindItemIdIn(trimmed);
    }

    /// <summary>
    /// ショップのサブドメインを取り出す。商品が見つからなかったときの受け皿。
    /// ショップのURLを落とされたら、そのショップの画面へ送れる。
    /// </summary>
    public static string? ParseShop(string? text) => BoothUrlExtractor.FindShopSubdomainIn(text);
}
