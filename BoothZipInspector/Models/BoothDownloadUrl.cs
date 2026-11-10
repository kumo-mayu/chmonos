namespace BoothZipInspector.Models;

/// <summary>BOOTH のダウンロードの URL から読んだ値（<see cref="BoothUrlExtractor.TryParseDownloadUrl"/>）。</summary>
/// <param name="DownloadableId">ダウンロードの ID（配布ファイルごとの番号）。</param>
/// <param name="ItemId">商品 ID。配布 CDN の URL にだけ入っている。</param>
public sealed record BoothDownloadUrl(long DownloadableId, string? ItemId);
