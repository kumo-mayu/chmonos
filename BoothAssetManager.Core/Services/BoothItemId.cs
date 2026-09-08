using BoothZipInspector;

namespace BoothAssetManager.Core.Services;

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

        return BoothUrlExtractor.TryExtractItemId(trimmed);
    }
}
