using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Chmonos.Core.Models;

/// <summary>
/// ユーザが入れたショップ。BOOTHから取れない商品のために持つ。
///
/// **商品が非公開でも、ショップは見られる場合がある。**
/// URLを貼れば本物のサブドメインが取れ、既にあるショップに正しく束ねられる。
/// </summary>
public sealed record LocalShop
{
    public required string Name { get; init; }

    /// <summary>
    /// ショップを束ねる鍵。URLを貼ったなら本物のサブドメイン、貼っていなければ
    /// <c>local-</c> を付けた鍵（<see cref="LocalShopKey"/>）。
    /// </summary>
    public required string Subdomain { get; init; }

    /// <summary>貼られたショップのURL。手で名前だけ入れたときは null。</summary>
    public string? Url { get; init; }

    /// <summary>本物のショップに結び付いているか（アイコンやバナーが使える）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsOnBooth => !LocalShopKey.IsLocal(Subdomain);
}

/// <summary>
/// ショップの鍵を決める。
///
/// サブドメインは**ショップを束ねる鍵であり、アイコンとバナーのファイル名でもある**
/// （<c>Paths.FindShopIcon(subdomain)</c>）。だから手で作った鍵が本物と衝突すると、
/// **本物のアイコンとバナーが表示されて区別が付かなくなる。**
///
/// **ローマ字化は採らない。**「ほとぎ屋」→ <c>hotogiya</c> は実在するし、
/// 漢字の読みは一意に決まらない（<c>KanjiReadings</c> は「撫で音」に15通りを作る。
/// わざと多めに作る設計なので、鍵を1つに決める用途には使えない）。
///
/// 名前から決めるので、**同じ名前を2回入れれば同じショップに束ねられる。**
/// </summary>
public static class LocalShopKey
{
    public const string Prefix = "local-";

    private const int HashLength = 8;

    /// <summary>ショップ名から鍵を作る。前後の空白と大文字小文字は無視する。</summary>
    public static string For(string shopName)
    {
        var normalized = (shopName ?? string.Empty).Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));

        return Prefix + Convert.ToHexStringLower(hash)[..HashLength];
    }

    public static bool IsLocal(string? subdomain)
        => subdomain is not null && subdomain.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// 貼られた文字列からショップのサブドメインを読む。
    /// <c>https://hotogiya.booth.pm/</c> も <c>hotogiya.booth.pm</c> も読む。
    /// ショップのURLに見えなければ null——**推測で鍵を作らない。**
    /// </summary>
    public static string? SubdomainFromUrl(string? text)
    {
        var match = ShopUrlRegex.Match((text ?? string.Empty).Trim());
        if (!match.Success)
        {
            return null;
        }

        var subdomain = match.Groups[1].Value.ToLowerInvariant();

        // booth.pm 自身のページ（https://booth.pm/ja/items/...）はショップではない
        return subdomain is "www" or "accounts" ? null : subdomain;
    }

    private static readonly Regex ShopUrlRegex = new(
        @"^(?:https?://)?([a-z0-9][a-z0-9-]*)\.booth\.pm(?:/.*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
}
