namespace Chmonos.Core.Models;

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

    /// <summary>
    /// ハッシュを取ったときに、その場所の文字に来ていたディスクの通し番号（2026-10-05・点検の16・ユーザ判断 16-A）。
    /// 別のディスクの上なら、場所・大きさ・更新日時が同じでも使い回さない。分からない場所（ネットワークの共有）は書かない。
    /// </summary>
    public string? Volume { get; init; }
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
    /// 同じ場所に前にあったファイル（別の中身）を持っていた商品のID（ユーザ判断 2026-09-30）。
    ///
    /// 手で商品に結んだファイルを同じ名前で上書きすると（壊れた zip を落とし直した・更新版を上書きした）、
    /// 古い中身の記録からその場所が外れ、新しい中身は手掛かりが無ければ未確定に出る。
    /// そのままだと、どの商品の物だったかが画面から分からなくなるので、候補として見せる。
    /// **自動では結ばない**（同じ名前の別の商品を置いただけかもしれない。推定した値は人が選んだときだけ入れる）。
    ///
    /// 取り込みが上書きを見つけたその回にしか分からない事実（次の回には、古い記録はもうその場所を指していない）なので記録に持ち、
    /// 次の取り込みで一覧を作り直すときも同じ中身の記録から引き継ぐ。
    /// </summary>
    public IReadOnlyList<string> SamePathItemIds { get; init; } = [];

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

    private BoothNotFoundNote? _notOnBooth;

    /// <summary>
    /// 取り込みがこのファイルの商品IDを BOOTH に聞き、「無い」（404）と答えられた記録（ユーザ判断 2026-10-06）。
    ///
    /// 前は答えを捨てて、商品IDを候補1つに載せて未確定へ送るだけだった。未確定の画面は選んだだけでは非公開だと分からず、
    /// 「情報を確認」でもう一度聞くまで、そのIDのまま登録する形を出せなかった。**もう聞いた答えを残すだけで、問い合わせは増やさない。**
    /// 一時的に届かなかった物は未確定へ送らない（「続きから」に残る）ので、ここに混ざらない。
    /// BOOTH に聞いていない行（除外を解除して戻した・商品から外して戻した・候補が複数で聞かなかった）には付けない。
    /// 人が「情報を確認」で聞き直すと、今も無ければ日時を新しくし、公開されていれば外す（<c>ItemService.PreviewWithReasonAsync</c>）。
    /// </summary>
    public BoothNotFoundNote? NotOnBooth { get => _notOnBooth; init => _notOnBooth = value; }

    /// <summary>
    /// 記録だけを差し替えた写し。ほかの欄は同じ物を指す（読み直した今の一覧に錠の中で当てるときに使う）。
    /// 欄を全部書き並べて作り直すと、後から欄を足したときに書き漏れて、その欄が消える
    /// </summary>
    public UnresolvedFile WithNotOnBooth(BoothNotFoundNote? note)
    {
        var copy = (UnresolvedFile)MemberwiseClone();
        copy._notOnBooth = note;
        return copy;
    }
}

/// <summary>BOOTH に商品IDを聞いて「無い」と答えられた記録（<c>unresolved.json</c> の <c>notOnBooth</c>）。</summary>
public sealed record BoothNotFoundNote
{
    /// <summary>聞いた商品ID。</summary>
    public required string ItemId { get; init; }

    /// <summary>最後に「無い」と答えられた日時。</summary>
    public required DateTimeOffset CheckedAt { get; init; }
}
