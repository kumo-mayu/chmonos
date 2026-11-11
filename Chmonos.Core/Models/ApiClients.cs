namespace Chmonos.Core.Models;

/// <summary>
/// 外から使う API に、人が許したソフトの一覧（保存先の <c>api-clients.json</c>。docs/spec/api-clients.md・plan 31）。
/// トークンそのものは持たず、ハッシュだけを控える（ファイルを読まれてもトークンにはならない）。
/// 新しいファイルなので保存先の形式の版は上げない（古い版は知らないファイルを読まず、消しもしない。data-format.md）
/// </summary>
public sealed record ApiClientList
{
    /// <summary>このファイルの形の版（保存先の形式の版とは別。中身を変えるときに上げる）。</summary>
    public int Version { get; init; } = 1;

    public IReadOnlyList<ApiClientRecord> Clients { get; init; } = [];
}

/// <summary>許したソフト1つ。</summary>
public sealed record ApiClientRecord
{
    /// <summary>ソフトの ID（Chmonos が作る。名乗った名前は重なり得るので、取り消しや変更はこれで指す）。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>ソフトが名乗った名前（本人の確かめではない。窓にもそう書く）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>許可を求める接続を開いた exe（確かめられなかったら null）。</summary>
    public string? ExePath { get; init; }

    /// <summary>ブラウザのページから求めたときのオリジン（それ以外は null）。</summary>
    public string? Origin { get; init; }

    /// <summary>
    /// 許した書き込みの種類（<see cref="ApiScopes"/> の名前）。読むのはいつも許す。
    /// 列挙ではなく文字列で持つ：種類を足すたびに保存先の形式の版を上げずに済むように（知らない名前は読むときに捨てる）
    /// </summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>トークンの SHA-256（16進の小文字）。トークンは256ビットの乱数なので、塩や遅いハッシュは要らない。</summary>
    public string TokenHash { get; init; } = string.Empty;

    public DateTimeOffset GrantedAt { get; init; }

    /// <summary>最後に使った日時（まだなら null）。書くのは間引く（<c>ApiClientBook</c>）ので、分単位の目安。</summary>
    public DateTimeOffset? LastUsedAt { get; init; }
}

/// <summary>
/// 書き込みの種類（ソフトごとに人が選んで許す）。今ある書き込みの道（docs/spec/local-api.md の 3a・3b・3c・段4）を、使う人に分かる粒で分けた。
/// 名前は API と保存のファイルで変えない英字。画面の名前は <see cref="Label"/>。
/// </summary>
public static class ApiScopes
{
    /// <summary>お気に入り・非表示の印。</summary>
    public const string Marks = "marks";

    /// <summary>「使った」の足跡。</summary>
    public const string Recent = "recent";

    /// <summary>通知の既読・解消。</summary>
    public const string Notifications = "notifications";

    /// <summary>タグ・属性・メモ（商品に付ける・一覧に足す）。</summary>
    public const string Tags = "tags";

    /// <summary>対応アバター・共通素体の紐付け。</summary>
    public const string Avatars = "avatars";

    /// <summary>改変（作る・メモ・blueprint ID・Unity プロジェクト・使った物）。</summary>
    public const string Modifications = "modifications";

    /// <summary>Unity へ送る（止めるも）。</summary>
    public const string Unity = "unity";

    /// <summary>Chmonos の画面・エクスプローラで開く（データは変えないが、使う人の画面を動かす）。</summary>
    public const string Open = "open";

    /// <summary>画面に並べる順（軽い物から）。</summary>
    public static IReadOnlyList<string> All { get; } = [Marks, Recent, Notifications, Tags, Avatars, Modifications, Unity, Open];

    public static bool IsKnown(string scope) => All.Contains(scope, StringComparer.Ordinal);

    /// <summary>画面に出す名前（窓と設定の一覧）。</summary>
    public static string Label(string scope) => scope switch
    {
        Marks => "お気に入り・非表示",
        Recent => "「使った」の記録",
        Notifications => "通知の既読・解消",
        Tags => "タグ・属性・メモ",
        Avatars => "対応アバター・共通素体",
        Modifications => "改変",
        Unity => "Unityへ送る",
        Open => "画面・エクスプローラで開く",
        _ => scope,
    };
}
