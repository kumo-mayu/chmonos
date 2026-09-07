using System.Text.RegularExpressions;
using BoothZipInspector.Models;

namespace BoothZipInspector;

/// <summary>
/// BOOTHの商品URL・ショップURルをテキストから抽出する純粋ロジック。
/// ファイルI/Oには依存しないため単体テストしやすい。
/// </summary>
public static class BoothUrlExtractor
{
    private static readonly Regex UrlScanRegex = new(
        @"https?://[^\s""'<>\]\)]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ItemUrlRegex = new(
        @"^https?://(?:(?<sub>[a-zA-Z0-9][a-zA-Z0-9-]*)\.)?booth\.pm(?:/(?<lang>[a-z]{2}(?:-[a-z]{2})?))?/items/(?<id>\d+)(?:[/?#].*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ShopUrlRegex = new(
        @"^https?://(?<sub>[a-zA-Z0-9][a-zA-Z0-9-]*)\.booth\.pm/?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 配布CDNのURL。Chrome/Edge が書く Zone.Identifier の HostUrl やブラウザ履歴に現れる。
    // 例: https://s6.booth.pm/c80ffe79-.../f/5813187/7905648/Kipfel_1.2.0.zip?...
    //     <shop-uuid>/f/<商品ID>/<配布ファイルID>/<ファイル名>
    private static readonly Regex CdnUrlRegex = new(
        @"^https?://s\d+\.booth\.pm/[0-9a-f-]{36}/f/(?<id>\d+)/(?<dl>\d+)/",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly char[] TrailingPunctuation = { '.', ',', ';', ':', '!', '?', ')', ']', '}', '"', '\'' };

    /// <summary>
    /// URL文字列がBOOTH商品URLであれば商品IDを返す。そうでなければnull。
    /// </summary>
    public static string? TryExtractItemId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var trimmed = TrimUrl(url);
        var match = ItemUrlRegex.Match(trimmed);
        if (match.Success)
        {
            return match.Groups["id"].Value;
        }

        var cdnMatch = CdnUrlRegex.Match(trimmed);
        return cdnMatch.Success ? cdnMatch.Groups["id"].Value : null;
    }

    /// <summary>
    /// 1つのURL文字列を分類してBoothClueを返す。BOOTH関連でなければnull。
    /// </summary>
    public static BoothClue? Classify(string url, string sourcePath)
    {
        var trimmed = TrimUrl(url);

        var itemMatch = ItemUrlRegex.Match(trimmed);
        if (itemMatch.Success)
        {
            return new BoothClue
            {
                Kind = BoothClueKind.ItemUrl,
                Url = trimmed,
                ItemId = itemMatch.Groups["id"].Value,
                SourcePath = sourcePath,
            };
        }

        var cdnMatch = CdnUrlRegex.Match(trimmed);
        if (cdnMatch.Success)
        {
            return new BoothClue
            {
                Kind = BoothClueKind.ItemUrl,
                Url = trimmed,
                ItemId = cdnMatch.Groups["id"].Value,
                SourcePath = sourcePath,
            };
        }

        var shopMatch = ShopUrlRegex.Match(trimmed);
        if (shopMatch.Success)
        {
            return new BoothClue
            {
                Kind = BoothClueKind.ShopUrl,
                Url = trimmed,
                ItemId = null,
                SourcePath = sourcePath,
            };
        }

        if (trimmed.Contains("booth.pm", StringComparison.OrdinalIgnoreCase))
        {
            return new BoothClue
            {
                Kind = BoothClueKind.OtherBoothUrl,
                Url = trimmed,
                ItemId = null,
                SourcePath = sourcePath,
            };
        }

        return null;
    }

    /// <summary>
    /// テキスト全体からBOOTH関連の手掛かりをすべて抽出する。
    /// </summary>
    public static IReadOnlyList<BoothClue> ExtractFromText(string text, string sourcePath)
    {
        var results = new List<BoothClue>();
        if (string.IsNullOrEmpty(text))
        {
            return results;
        }

        foreach (Match match in UrlScanRegex.Matches(text))
        {
            var clue = Classify(match.Value, sourcePath);
            if (clue is not null)
            {
                results.Add(clue);
            }
        }

        return results;
    }

    private static string TrimUrl(string url)
    {
        var trimmed = url.Trim();
        return trimmed.TrimEnd(TrailingPunctuation);
    }
}
