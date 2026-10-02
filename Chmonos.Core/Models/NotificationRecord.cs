namespace Chmonos.Core.Models;

/// <summary>「今すぐ困らないが知っておきたい」ことの種別。UIでは種別ごとにグループ表示する。</summary>
public enum NotificationKind
{
    /// <summary>商品ページの内容が変わった。</summary>
    ItemUpdated,

    /// <summary>マスタに存在しないuserTag/属性を参照しているitemがある。</summary>
    OrphanTag,

    /// <summary>手元のファイル・購入の記録が指す種類がBOOTH側から消えた。</summary>
    OrphanVariationLink,

    /// <summary>消えていた種類がBOOTHに戻ってきた（<see cref="OrphanVariationLink"/> の裏返し）。</summary>
    VariationBackOnBooth,

    /// <summary>
    /// 説明文のセクションが取れない商品が急増した（BOOTH側の構造変化の疑い）。
    ///
    /// **要確認の束には出さない**（ユーザ判断 2026-09-18：商品1件ごとの話と同列に並べると埋もれる）。
    /// アプリ全体の話なので、ナビの「設定」の上の帯で知らせる。
    /// </summary>
    PageStructureChanged,

    /// <summary>フォルダとして登録した商品のzipが手元に入った。登録を解除できる。</summary>
    ArchiveFoundForFolder,

    /// <summary>非公開と見なしていた商品がBOOTHに戻ってきた。</summary>
    ItemBackOnBooth,

    /// <summary>
    /// 自動で始めた取り込みで、zipを展開したフォルダの中のファイルを指していた
    /// （ユーザ判断 2026-09-21・G2）。押してもいないのに窓で尋ねるのはやめ、そのまま取り込んで後から直せるようにする。
    /// </summary>
    UnpackedFilesImported,

    /// <summary>
    /// 手で直した JSON に食い違いがある（ユーザ判断 2026-09-21・J2/L6）。
    /// 同じ名前が2つある・商品IDとファイル名が違う。**直し方はこちらで決めず、人に伝える。**
    /// </summary>
    HandEditMismatch,
}

/// <summary>
/// 要確認1件（<c>notifications.json</c>）。確認しても即削除せず既読にし、
/// 上限を超えたら古い既読から捨てる。
/// </summary>
public sealed record NotificationRecord
{
    public required string Id { get; init; }

    public required NotificationKind Kind { get; init; }

    /// <summary>関連するBOOTH商品ID。商品に紐付かない通知（構造変化など）では null。</summary>
    public string? ItemId { get; init; }

    public required string Title { get; init; }

    public required string Detail { get; init; }

    /// <summary>
    /// 更新時の差分要約。旧データのスナップショットは保存しないので、
    /// 取得した瞬間に比較してここへ書き込む。
    /// </summary>
    public IReadOnlyList<NotificationDiff> Diffs { get; init; } = [];

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// 未読のうちに次の変化を重ねた日時（<see cref="Services.ChangeStack"/>。ユーザ判断 2026-10-02）。重ねていなければ null。
    /// <see cref="CreatedAt"/> は最初の変化の日時のまま残す。何日にわたって変わったかが読めるように
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>最後に変わった日時。要確認の並びと「何分前」はこれで見る（重ねる前は差し替えで作った日時が新しくなっていたので、並びはそれに合わせる）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset LastChangedAt => UpdatedAt ?? CreatedAt;

    public bool IsRead { get; init; }

    /// <summary>
    /// 知らせた状況がもう無くなっているか（ユーザ判断 2026-09-18）。
    ///
    /// タグを作り直した・種類を結び直した・フォルダ登録を解除した、のように用が済んでも
    /// 通知は残る作りだったので、**消さずに「解消済み」と印を付ける**。何が起きていたかは
    /// 後から辿れるようにし、上限を超えたときは既読と同じく古い方から捨てる
    /// </summary>
    public bool IsResolved { get; init; }

    /// <summary>更新履歴セクションの変化など、注目度の高い通知か。</summary>
    public bool IsStrong { get; init; }
}

/// <summary>変わったセクション1つぶんの差分要約。</summary>
public sealed class NotificationDiff
{
    /// <summary>変わった箇所の名前（セクション見出し、または name / description）。</summary>
    public required string Field { get; init; }

    public string? Before { get; init; }

    public string? After { get; init; }

    /// <summary>
    /// 説明文の見出しの、変わった行だけ（行単位の差。メモ13-②）。<see cref="Before"/>・<see cref="After"/> は頭の抜き出しなので、
    /// 見出しの後ろの方が変わると前後が同じに見えていた。前の本文は保存しないので、知らせを作る瞬間にしか作れない。
    /// 短い値の欄（商品名・価格・数）と、この形より前に作った知らせでは null（空と同じ扱い）
    /// </summary>
    public IReadOnlyList<NotificationLine>? Lines { get; init; }

    /// <summary>
    /// 足した行のうち、上限（<see cref="Services.LineDiff.MaxLines"/>）を超えて残さなかった数。残した行からは数え直せないので書く。超えていなければ null
    /// </summary>
    public int? MoreAdded { get; init; }

    /// <summary>消した行のうち、上限を超えて残さなかった数（商品ページは消えた行だけを数えて出すので、足した行と分けて持つ）。</summary>
    public int? MoreRemoved { get; init; }
}

/// <summary>変わった行が、後の本文に足された物か、前の本文から消えた物か。</summary>
public enum NotificationLineKind
{
    Added,
    Removed,
}

/// <summary>変わった行1つ（<c>{"kind": "added", "text": "…"}</c>）。</summary>
public sealed class NotificationLine
{
    public required NotificationLineKind Kind { get; init; }

    public required string Text { get; init; }
}
