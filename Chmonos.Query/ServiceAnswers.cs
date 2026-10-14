namespace Chmonos.Query;

/// <summary>商品をまとめて引く（<c>POST /v1/items/batch</c>）。一覧から詳細へ、1件ずつ聞かずに済むように。</summary>
/// <param name="Include">商品1件と同じ include（<c>avatarLinks,allFiles</c> など）。</param>
public sealed record ItemsBatchRequest(IReadOnlyList<string>? Ids, string? Include = null);

/// <param name="Missing">見つからなかった ID（送った順）。</param>
public sealed record ItemsBatchAnswer(IReadOnlyList<ItemDetail> Items, IReadOnlyList<string> Missing);

/// <summary>動いているか（<c>GET /v1/health</c>。トークンなし）。</summary>
/// <param name="Status"><c>ok</c>（答えられる）・<c>tooNew</c>（保存の形がこの API より新しい）。</param>
/// <param name="FailedItemCount">
/// 読めなかった商品（手で壊した JSON など）の数。黙って減らさず知らせる。ID はトークンの要る <c>/v1/failed-items</c> で返す
/// （health はトークンなしで答えるので、保存先の中身を入れない。点検30）。
/// </param>
public sealed record HealthAnswer(string Status, DateTimeOffset LoadedAt, int ItemCount, int FailedItemCount);

/// <summary>バージョン（<c>GET /v1/version</c>。トークンなし）。</summary>
/// <param name="Api">URL のバージョン（<c>v1</c>）。</param>
/// <param name="App">アプリと同じ番号（同じ zip で配る）。</param>
/// <param name="StoreFormat">保存先の形の版。</param>
/// <param name="StoreFormatSupported">この API が読める版の上限。</param>
/// <param name="Notice">BOOTH の情報の扱いについての注意（docs/spec/local-api.md「注意」）。</param>
/// <param name="Epoch">この API の起動ごとに変わる名前。通番は起動ごとに数え直すので、外の道具は名前が変わったら全部を取り直す。</param>
/// <param name="Sequence">今の通番（変化を1つ記録するたびに進む）。</param>
/// <param name="Busy">アプリがまとめて書いている途中なら、その種類（<c>import</c> など）。その間は古い答えを返し続ける。</param>
public sealed record VersionAnswer(string Api, string App, int StoreFormat, int StoreFormatSupported, string Notice, string Epoch, long Sequence, string? Busy);

/// <summary>変化1つ（<c>/v1/changes</c> と SSE の出来事）。</summary>
/// <param name="Kind"><c>changed</c>（写しを読み直した）・<c>reloaded</c>（全部を読み直した。外の道具も全部を取り直す）・<c>busy</c>（まとめて書く操作の始まりと終わり）・<c>formatTooNew</c>・<c>stopping</c>。</param>
/// <param name="Parts">読み直した部分（<c>items</c>・<c>images</c>・<c>registry</c>・<c>userTags</c> など）。</param>
/// <param name="ItemIds">変わった商品（増えた・直した・消えた）。</param>
/// <param name="Busy"><c>busy</c> のとき、始まりならその種類、終わりなら null。</param>
public sealed record ChangeEventRef(long Seq, string Kind, IReadOnlyList<string> Parts, IReadOnlyList<string> ItemIds, string? Busy, DateTimeOffset At);

/// <param name="Reload">頼んだ通番からの変化を全部は持っていない（古すぎる・API が起動し直した）。外の道具は全部を取り直す。</param>
public sealed record ChangesAnswer(string Epoch, long Sequence, bool Reload, IReadOnlyList<ChangeEventRef> Changes);

/// <summary>書き込みの要求：オン・オフ（お気に入り・非表示など）。</summary>
public sealed record FlagRequest(bool Value);

/// <summary>開いている Unity のエディタ（送り先を選ぶため）。<see cref="ProjectPath"/> は Hub・VCC の一覧で言い当てられたときだけ入る。</summary>
public sealed record UnityEditorRef(int ProcessId, string? ProjectName, string? ProjectPath);

/// <summary>
/// Unity へ送る要求。送る物（<see cref="Entry"/>＝zip の中のパス、<see cref="ZipHash"/>＝どの zip か）は、商品の unitypackage が1つなら省ける。
/// 送り先（<see cref="Project"/>＝プロジェクトのフォルダ）は、開いているエディタが1つなら省ける
/// </summary>
public sealed record UnitySendRequest(string? Entry, string? ZipHash, string? Project);

/// <summary>
/// Unity へ送った結果。<see cref="State"/>：sending（送っている）・imported（取り込んだ）・alreadyPresent（もう入っていた）・
/// cancelled（Unity の取り込み画面で取り消した）・stopped（止めた）・failed（送れなかった。<see cref="Problem"/> に理由）
/// </summary>
public sealed record UnitySendStatus(
    string ItemId, string Package, string Project, string State, string? Problem, DateTimeOffset StartedAt, DateTimeOffset? EndedAt);

/// <summary>書き込みの要求：ユーザータグ1つ（大分類と小分類）。</summary>
public sealed record UserTagInput(string Top, IReadOnlyList<string>? Subs);

/// <summary>書き込みの要求：商品のユーザータグを丸ごと置き換える。</summary>
public sealed record UserTagsRequest(IReadOnlyList<UserTagInput> Tags);

/// <summary>書き込みの要求：ユーザータグを1つ足す・外す（小分類を省くと大分類だけ）。一覧に足すときも使う。</summary>
public sealed record UserTagChangeRequest(string Top, string? Sub);

/// <summary>書き込みの要求：属性の値（0〜100。null は外して未評価に戻す）。名指しした属性だけ書く。</summary>
public sealed record AttributesRequest(IReadOnlyDictionary<string, int?> Values);

/// <summary>書き込みの要求：メモ（null か空で消す）。</summary>
public sealed record MemoRequest(string? Memo);

/// <summary>書き込みの要求：名前（属性を一覧に足す）。</summary>
public sealed record NameRequest(string Name);

/// <summary>書き込みの要求：足跡の種類（今は <c>used</c> だけ）。</summary>
public sealed record RecentRequest(string Kind);

/// <summary>書き込みの要求：ID の一覧（通知の既読・解消）。</summary>
public sealed record IdsRequest(IReadOnlyList<string> Ids);

/// <summary>書き込みの要求：対応アバター1体（商品 ID）。</summary>
public sealed record AvatarLinkRequest(string AvatarId);

/// <summary>書き込みの要求：確認待ちの対応アバターのうち、確認済みにする物（名指し。まとめての確定は出さない）。</summary>
public sealed record AvatarConfirmRequest(IReadOnlyList<string> AvatarIds);

/// <summary>書き込みの要求：共通素体1つ（名前）。</summary>
public sealed record BaseLinkRequest(string BaseName);

/// <summary>書き込みの要求：改変を作る（アバター1体＋名前）。</summary>
public sealed record ModificationCreateRequest(string AvatarId, string Name);

/// <summary>書き込みの要求：改変の blueprint ID（<c>avtr_</c>＋GUID。null か空で外す）。</summary>
public sealed record BlueprintIdRequest(string? BlueprintId);

/// <summary>エクスプローラで開く場所（省くと、場所が1つだけの商品ならそこ）。</summary>
public sealed record RevealRequest(string? Path);

/// <summary>書き込みの要求：改変の Unity プロジェクトのフォルダ（null で外す）。</summary>
public sealed record UnityProjectRequest(string? Path);

/// <summary>
/// 書き込みの要求：改変の使った物1行。足すときは商品と、分かればファイル（zip のハッシュ・中の unitypackage）。
/// 外す・戻すときは行を名指しする（同じ商品が2行あるときは fileHash・package・addedAt で絞る。答えの members と同じ値）
/// </summary>
public sealed record ModificationMemberRequest(string ItemId, long? VariationId, string? FileHash, string? Package, DateTimeOffset? AddedAt);

/// <summary>
/// 書き込みを受け付けたが、アプリがまとめて書いている途中（取り込み）なので、答えの写しはまだ差し替えていない（202）。
/// 終わったら変化の知らせ（SSE・changes）で伝える。<see cref="Busy"/> は何をしている途中か
/// </summary>
public sealed record AcceptedAnswer(string Busy);

/// <summary>
/// 誤りの答え（RFC 9457 <c>application/problem+json</c>）。<see cref="Code"/> は機械が読む名前で変えない。<see cref="Detail"/> は人が読む説明。
/// </summary>
public sealed record ProblemAnswer(string Type, string Title, int Status, string Detail, string Code);

public static class ServiceText
{
    /// <summary>注意の文面（ユーザ判断 2026-10-12「注意は入れましょう」。根拠は docs/research/booth-terms.md §5）。</summary>
    public const string Notice =
        "この API が返す BOOTH の商品の情報（商品名・説明文・画像など）の権利は、それぞれの作者にあります。"
        + "自分で使うための物なので、他の人に渡したり、公開したりしないでください。"
        + "購入したファイルの扱いは、各ショップの利用規約に従ってください。";
}
