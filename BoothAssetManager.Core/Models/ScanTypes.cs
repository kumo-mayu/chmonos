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

    /// <summary>
    /// zip の中のテキストに書かれていた商品IDの並び（ID を決める手掛かり）。**まだ中を読んでいなければ null。**
    ///
    /// 取り込み直すたびに、持っているファイルの zip を全部開き直していた。手掛かりは中身だけで決まるので、
    /// ハッシュと一緒に控えておけば開かずに済む（中身の一覧は商品の <c>contents</c> にある）。
    /// ハッシュを計算し直したら（大きさか日時が変わった）捨てて読み直す。このファイルは消してもよい（読み直すだけ）。
    /// </summary>
    public IReadOnlyList<string>? ClueItemIds { get; init; }
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

    /// <summary>
    /// 取り込みで zip として開けなかったか（途中で切れたダウンロード・中身がでたらめ・zip ではない物に .zip の名前）。
    ///
    /// 前は開けなかった zip を「中身の一覧が空の未確定」として置くだけで、普通の未確定と見分けられなかった
    /// （大容量の確かめ 2026-09-30 の問題4。一時展開して初めて「壊れている」と出た）。
    /// **開いてみないと分からない事実**なので記録に持つ（画面を開くたびに未確定の zip を全部開き直すと、数万件で固まる）。
    /// 立つのは形式が合わなかったときだけ——ほかのアプリが開いていた・権限が無い、は壊れているとは言えないので立てない。
    /// 開けた zip には書き出さない（全部の行に false が並ぶと読みにくい）。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool ArchiveBroken { get; init; }
}
