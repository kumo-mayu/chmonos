namespace Chmonos.Query;

/// <summary>商品をまとめて引く（<c>POST /v1/items/batch</c>）。一覧から詳細へ、1件ずつ聞かずに済むように。</summary>
/// <param name="Include">商品1件と同じ include（<c>avatarLinks,allFiles</c> など）。</param>
public sealed record ItemsBatchRequest(IReadOnlyList<string>? Ids, string? Include = null);

/// <param name="Missing">見つからなかった ID（送った順）。</param>
public sealed record ItemsBatchAnswer(IReadOnlyList<ItemDetail> Items, IReadOnlyList<string> Missing);

/// <summary>動いているか（<c>GET /v1/health</c>。合言葉なし）。</summary>
/// <param name="Status"><c>ok</c>（答えられる）・<c>tooNew</c>（保存の形がこの API より新しい）。</param>
/// <param name="FailedItemIds">読めなかった商品（手で壊した JSON など）。黙って減らさず知らせる。</param>
public sealed record HealthAnswer(string Status, DateTimeOffset LoadedAt, int ItemCount, IReadOnlyList<string> FailedItemIds);

/// <summary>バージョン（<c>GET /v1/version</c>。合言葉なし）。</summary>
/// <param name="Api">URL のバージョン（<c>v1</c>）。</param>
/// <param name="App">アプリと同じ番号（同じ zip で配る）。</param>
/// <param name="StoreFormat">保存先の形の版。</param>
/// <param name="StoreFormatSupported">この API が読める版の上限。</param>
/// <param name="Notice">BOOTH の情報の扱いについての注意（docs/spec/local-api.md「注意」）。</param>
public sealed record VersionAnswer(string Api, string App, int StoreFormat, int StoreFormatSupported, string Notice);

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
