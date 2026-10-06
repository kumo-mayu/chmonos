using System.Text.RegularExpressions;
using Chmonos.Core.Models;

namespace Chmonos.Core.Booth;

/// <summary>
/// 「BOOTHで開く」で外のアプリ（ブラウザ）へ渡すリンクを決める（2026-10-06 外部の点検・L106）。
///
/// 開く操作は OS の関連付けに渡すので、渡した文字列が実行ファイルの場所や <c>file:</c>・独自の形の URL なら、
/// それがそのまま起動される。前は商品の記録の <c>booth.url</c>・<c>booth.shop.url</c>（手で直せる JSON）を
/// そのまま渡していたので、そこに書かれた物を何でも開けた。
///
/// - **商品ページは記録の URL を使わず、商品ID から作る。**ID は BOOTH の番号（数字だけ）のときだけ。
///   BOOTH の商品ページは <c>booth.pm/ja/items/{番号}</c> でどのショップの物にも届くので、記録の URL を使う理由が無い
/// - **ショップのページは https で、BOOTH のホスト（<c>booth.pm</c>・<c>*.booth.pm</c>）の物だけ**
/// - どれでもない URL（説明文の中のリンク・動画）は、開く所（App の <c>Shell.OpenUrl</c>）が http/https かを見る
/// </summary>
public static class BoothLinks
{
    private static readonly Regex BoothNumber = new(@"\A[0-9]{1,18}\z", RegexOptions.CultureInvariant);

    // ホスト名の1区切りの決まり（英数字とハイフン、端はハイフンでない）。BOOTH のサブドメインもこの形
    private static readonly Regex HostLabel = new(@"\A[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\z", RegexOptions.CultureInvariant);

    /// <summary>この商品の BOOTH のページ。BOOTH に無い商品（仮ID・BOOTHに無いとして登録した物）と、ID が番号でない物は null。</summary>
    public static string? ItemPage(ItemRecord item)
        => item.IsLocalOnly ? null : ItemPage(item.Id);

    /// <summary>商品ID（BOOTH の番号）から商品ページを作る。番号でなければ null。</summary>
    public static string? ItemPage(string? itemId)
        => itemId is not null && BoothNumber.IsMatch(itemId) ? BoothClient.ItemPageUrl(itemId) : null;

    /// <summary>
    /// 記録にあるショップの URL が開いてよい物なら、正規化した URL。https・BOOTH のホスト・利用者名やポートの無い物だけ。
    /// </summary>
    public static string? ShopPage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0
            || !IsBoothHost(uri.Host))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    /// <summary>
    /// ショップのページ。記録の URL が開いてよい物ならそれを、無ければサブドメインから作る（<c>https://{サブドメイン}.booth.pm/</c>）。
    /// 記録の URL が開いてよくない物なら、サブドメインから作る方へ落とす（手で直した URL で、店のページへ行けなくはしない）。
    /// サブドメインもホスト名の形でなければ null。
    /// </summary>
    public static string? ShopPage(string? subdomain, string? recordedUrl)
    {
        if (ShopPage(recordedUrl) is { } recorded)
        {
            return recorded;
        }

        var label = subdomain?.Trim().ToLowerInvariant();
        return label is not null && HostLabel.IsMatch(label) && !LocalShopKey.IsLocal(label)
            ? $"https://{label}.booth.pm/"
            : null;
    }

    private static bool IsBoothHost(string host)
        => host.Equals("booth.pm", StringComparison.OrdinalIgnoreCase)
            || (host.EndsWith(".booth.pm", StringComparison.OrdinalIgnoreCase)
                && HostLabel.IsMatch(host[..^".booth.pm".Length].ToLowerInvariant()));
}
