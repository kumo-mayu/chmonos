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

    // 商品画像のURL。**商品IDが入っている。**
    // ブラウザで商品の絵をドラッグすると、リンクではなくこれが落ちてくる。
    // 例: https://booth.pximg.net/c/300x300_a2_g5/<shop-uuid>/i/4897493/<hash>_base_resized.jpg
    //     https://booth.pximg.net/<shop-uuid>/i/4897493/<hash>_base_resized.jpg
    //
    // ショップのアイコン（/users/<userId>/icon_image/…）には商品IDが無いので当たらない。
    private static readonly Regex ImageUrlRegex = new(
        @"^https?://booth\.pximg\.net/(?:c/[^/]+/)?[0-9a-f-]{36}/i/(?<id>\d+)/",
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
        if (cdnMatch.Success)
        {
            return cdnMatch.Groups["id"].Value;
        }

        var imageMatch = ImageUrlRegex.Match(trimmed);
        return imageMatch.Success ? imageMatch.Groups["id"].Value : null;
    }

    /// <summary>
    /// 文字列の**中から**最初のBOOTH商品IDを探す。
    ///
    /// 落ちてくるのはURL1本とは限らない。文章の選択をドラッグすればその文が落ちるし、
    /// ブラウザによってはHTMLの断片が来る。**URLだけを渡してもらう前提にしない。**
    /// </summary>
    public static string? FindItemIdIn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // 1本きりならそのまま（末尾の句読点なども落とせる）
        if (TryExtractItemId(text) is { } single)
        {
            return single;
        }

        foreach (Match match in UrlScanRegex.Matches(text))
        {
            if (TryExtractItemId(match.Value) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// 文字列の中から最初のショップのサブドメインを探す。商品が見つからなかったときの受け皿。
    /// ショップのトップだけでなく、配下のページ（/items/… 以外）も同じショップとして読む。
    /// </summary>
    public static string? FindShopSubdomainIn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (Match match in UrlScanRegex.Matches(text))
        {
            var shop = ShopSubdomainRegex.Match(TrimUrl(match.Value));
            if (shop.Success && !ReservedSubdomains.Contains(shop.Groups["sub"].Value))
            {
                return shop.Groups["sub"].Value;
            }
        }

        return null;
    }

    /// <summary>ショップのサブドメイン。トップでも配下のページでも拾う。</summary>
    private static readonly Regex ShopSubdomainRegex = new(
        @"^https?://(?<sub>[a-zA-Z0-9][a-zA-Z0-9-]*)\.booth\.pm(?:[/?#].*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>ショップではないサブドメイン。ここを開いても手持ちは出てこない。</summary>
    private static readonly HashSet<string> ReservedSubdomains =
        new(StringComparer.OrdinalIgnoreCase) { "accounts", "manage", "www", "asset", "s1", "s2", "s3", "s4", "s5", "s6" };

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
