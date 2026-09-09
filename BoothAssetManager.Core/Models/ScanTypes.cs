namespace BoothAssetManager.Core.Models;

/// <summary>
/// スキャン高速化のためのキャッシュ1件（<c>scan-cache.json</c>）。
/// パス・サイズ・更新日時が一致すればハッシュ計算を省略する。
/// 丸ごと捨てても再計算に時間がかかるだけで、データは壊れない。
/// </summary>
public sealed class ScanCacheEntry
{
    public required string Path { get; init; }

    public required long SizeBytes { get; init; }

    public required DateTimeOffset ModifiedAtUtc { get; init; }

    public required string Hash { get; init; }
}

/// <summary>
/// 管理対象から外したファイル（<c>excluded.json</c>）。
/// パスは計算ゼロで弾くためのショートカット、ハッシュは移動されても効かせるための最終判定。
/// </summary>
public sealed class ExcludedEntry
{
    public required string Hash { get; init; }

    public IReadOnlyList<string> Paths { get; init; } = [];

    public required DateTimeOffset ExcludedAt { get; init; }

    public string? Reason { get; init; }
}

/// <summary>
/// 「このファイルはこの商品のものではない」という記録（<c>detached.json</c>）。
///
/// 商品ページでファイルを外したときに書く。**外しただけでは元に戻ってしまう**ため。
/// 手掛かり（ファイル名・zipの中身・Zone.Identifier）から商品IDが1つに決まるファイルは、
/// 取り込みのたびに同じ商品へ自動で紐付く。間違った紐付けはまさにその手掛かりが
/// 間違っている場合なので、外した記録を残さないと次の取り込みで戻る。
///
/// 消えるのは、ユーザが同じ商品へ改めて紐付け直したとき（<c>AssignItemId</c>）。
/// 「やっぱりこれで合っていた」と言われたものを、こちらが覚えていて弾き続ける方がおかしい。
/// </summary>
public sealed class DetachedFile
{
    public required string Hash { get; init; }

    /// <summary>紐付けてはいけない商品ID。ファイルそのものを除外するわけではない。</summary>
    public required string ItemId { get; init; }

    /// <summary>外したときのパス。人がJSONを読んだときに何のファイルか分かるように残す。</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];

    public required DateTimeOffset DetachedAt { get; init; }
}

/// <summary>
/// BoothIDを確定できなかったファイル（<c>unresolved.json</c>）。
/// itemのスキーマに「IDが無い状態」を持ち込まないため、確定するまでこちらに置く。
/// </summary>
public sealed class UnresolvedFile
{
    public required string Hash { get; init; }

    public required IReadOnlyList<string> Paths { get; init; }

    public required long SizeBytes { get; init; }

    public required DateTimeOffset ModifiedAtUtc { get; init; }

    public required DateTimeOffset FirstSeenAt { get; init; }

    /// <summary>アーカイブ内のファイル名一覧（手掛かりの提示に使う）。</summary>
    public IReadOnlyList<string> Contents { get; init; } = [];

    /// <summary>Zone.Identifier に残っていたダウンロード元。</summary>
    public string? ZoneHostUrl { get; init; }

    public string? ZoneReferrerUrl { get; init; }

    /// <summary>解決できなかった候補ID。0件（手掛かりなし）と複数件（曖昧）の両方があり得る。</summary>
    public IReadOnlyList<string> CandidateItemIds { get; init; } = [];
}
