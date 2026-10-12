namespace Chmonos.Query;

/// <summary>商品をまとめて引く（<c>POST /v1/items/batch</c>）。一覧から詳細へ、1件ずつ聞かずに済むように。</summary>
/// <param name="Include">商品1件と同じ include（<c>avatarLinks,allFiles</c> など）。</param>
public sealed record ItemsBatchRequest(IReadOnlyList<string>? Ids, string? Include = null);

/// <param name="Missing">見つからなかった ID（送った順）。</param>
public sealed record ItemsBatchAnswer(IReadOnlyList<ItemDetail> Items, IReadOnlyList<string> Missing);

/// <summary>動いているか（<c>GET /v1/health</c>。合言葉なし）。</summary>
/// <param name="Status"><c>ok</c>（答えられる）・<c>tooNew</c>（保存の形がこの API より新しい）。</param>
/// <param name="FailedItemCount">
/// 読めなかった商品（手で壊した JSON など）の数。黙って減らさず知らせる。ID は合言葉の要る <c>/v1/failed-items</c> で返す
/// （health は合言葉なしで答えるので、保存先の中身を入れない。点検30）。
/// </param>
public sealed record HealthAnswer(string Status, DateTimeOffset LoadedAt, int ItemCount, int FailedItemCount);

/// <summary>バージョン（<c>GET /v1/version</c>。合言葉なし）。</summary>
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

/// <summary>アプリからの知らせ（<c>POST /internal/busy</c>）：まとめて書く操作の始まりと終わり。ファイルを見張っても分からないのはここだけ。</summary>
/// <param name="State"><c>start</c> か <c>end</c>。</param>
/// <param name="Kind">操作の種類（<c>import</c> など）。</param>
public sealed record BusyRequest(string State, string? Kind);

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
