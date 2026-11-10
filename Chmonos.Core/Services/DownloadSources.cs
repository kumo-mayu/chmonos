using BoothZipInspector;
using BoothZipInspector.Models;
using Chmonos.Core.Booth;
using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// ダウンロード元の記録（Zone.Identifier の HostUrl・ReferrerUrl）から、ファイルの行に残す <see cref="DownloadSource"/> を作る（ユーザ判断 2026-11-10）。
/// 取り込み・未確定から選ぶ・手で結ぶ・起動時の見回りが、どれもここを通る。
/// </summary>
/// <remarks>
/// - 残すのは BOOTH のダウンロードの ID と、組み直した商品ページの URL だけ。署名付きの CDN の URL・BOOTH 以外のホストは残さない
///   （URL は正規表現の組から組み直し、元の文字列を写さない）。
/// - Chrome・Edge は HostUrl に配布 CDN の URL を書き、ReferrerUrl は <c>accounts.booth.pm</c> の頭だけ（docs/research/id-resolution.md）。
///   商品ページの URL が記録に無いことがほとんどなので、CDN の URL に入っている商品 ID から <see cref="BoothClient.ItemPageUrl"/> の形で組む。
///   読んだ値の書き直しで、推した値ではない。
/// - Brave（HostUrl はページの頭だけ）・エクスプローラーで解いた中身（ReferrerUrl は元の zip の場所）からは何も残らない。
/// </remarks>
public static class DownloadSources
{
    /// <summary>読んだ記録から作る。何も分からなければ中身の空の物（読んだ印）。</summary>
    public static DownloadSource From(string? hostUrl, string? referrerUrl)
    {
        var download = BoothUrlExtractor.TryParseDownloadUrl(hostUrl) ?? BoothUrlExtractor.TryParseDownloadUrl(referrerUrl);

        // 商品ページを開いて落とした物は ReferrerUrl が商品ページのことがある。書いてあればそれを、無ければ CDN の商品 ID から組む
        var itemUrl = BoothUrlExtractor.TryNormalizeItemPageUrl(referrerUrl)
            ?? BoothUrlExtractor.TryNormalizeItemPageUrl(hostUrl)
            ?? (download?.ItemId is { } itemId ? BoothClient.ItemPageUrl(itemId) : null);

        return new DownloadSource { DownloadableId = download?.DownloadableId, ItemUrl = itemUrl };
    }

    /// <summary>読んだ Zone.Identifier から作る（印が無ければ中身の空の物）。</summary>
    public static DownloadSource From(ZoneIdentifierInfo zone)
        => zone.Found ? From(zone.HostUrl, zone.ReferrerUrl) : new DownloadSource();

    /// <summary>
    /// 今ある行と見つけた行を合わせる（<c>LocalFileMerger</c>）。今ある行が何か知っていればそれを保ち、空か未読なら見つけた方。
    /// 同じハッシュ（同じ中身）の記録なので、見つけた方の値は推した値ではなく、その中身について読んだ事実。
    /// </summary>
    public static DownloadSource? Combine(DownloadSource? current, DownloadSource? discovered)
        => current is { IsEmpty: false } ? current : discovered ?? current;
}
