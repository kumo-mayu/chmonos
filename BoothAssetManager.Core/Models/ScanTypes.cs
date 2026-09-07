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
