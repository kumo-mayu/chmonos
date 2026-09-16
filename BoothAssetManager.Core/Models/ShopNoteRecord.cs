using System.Text.Json.Serialization;

namespace BoothAssetManager.Core.Models;

/// <summary>
/// ショップに人が付けた星とメモ（<c>shops.json</c>・ユーザ判断 2026-09-16）。
///
/// **商品の <c>booth.shop</c>・<c>local.shop</c> は動かさない。**あちらは「その商品を取得したときの BOOTH の姿」と
/// 「人が入れたその商品のショップ」で、商品の記録の一部。ショップの記録を1か所にまとめて商品から鍵で指す形にすると、
/// 商品の JSON を開いてもどのショップか読めなくなる。
///
/// 星とメモは商品に持たせない——同じショップの全商品に写すことになり、直すたびに食い違う。
/// <c>shop-banners.json</c> とも分ける。あちらは裏の取得が書く機械の記録で、こちらは人の入力。
/// 混ぜると、古い写しで人の入力を上書きする事故を招く。
///
/// 商品が無くなったショップの記録も消さない（人が書いたもの）。ショップ一覧に出なくなるだけ。
/// </summary>
public sealed record ShopNoteRecord
{
    /// <summary>ショップの鍵（ショップ一覧が束ねる鍵と同じ：サブドメイン、または <c>local-</c> の鍵）。大文字小文字は区別しない。</summary>
    public required string Subdomain { get; init; }

    /// <summary>
    /// 書いた時点のショップ名。**見分け用で、表示には使わない**（表示は今どおり商品から出す）。
    /// <c>local-</c> の鍵だけでは、ファイルを開いても何のショップか読めないため（ユーザ判断 2026-09-16）。
    /// </summary>
    public string? NameHint { get; init; }

    /// <summary>BOOTH のショップの変わらない ID。サブドメインが変わったときに、あとでつなぎ直す手がかり。</summary>
    public string? Uuid { get; init; }

    /// <summary>お気に入りのショップか。</summary>
    public bool IsFavorite { get; init; }

    /// <summary>メモ（利用規約・問い合わせ先・作者の別名義など、ショップ単位でしか持てない知識）。</summary>
    public string? Memo { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>星もメモも無い。残す意味が無いので、書くときに落とす。</summary>
    [JsonIgnore]
    public bool IsEmpty => !IsFavorite && string.IsNullOrWhiteSpace(Memo);
}
