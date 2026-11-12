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

    /// <summary>既定の、止めるまでの拒否の回数（ユーザの言葉 2026-11-11「デフォルトで3回拒否したら止める形が良いかも」）。</summary>
    public const int DefaultRefusalLimit = 3;

    /// <summary>
    /// 続けて拒否したら止める回数（設定の「API」で変える。0 なら拒否を数えず、止めもしない）。
    /// 許しの控えと同じファイルに置く：止めの記録と同じ錠で読み書きでき、引越し・書き出しの扱いも同じになる（docs/spec/api-clients.md）
    /// </summary>
    public int RefusalLimit { get; init; } = DefaultRefusalLimit;

    /// <summary>人が［拒否］を押したソフトの記録（2026-11-11）。許可したら、そのソフトの行は消す。今ある控えに無い欄なので、読むときは null を空として受ける。</summary>
    public IReadOnlyList<ApiClientRefusal> Refusals { get; init; } = [];
}

/// <summary>
/// 続けて拒否したソフト1つ（同じソフトの決め方は許しと同じ：名前・exe・オリジン）。トークンは無いので、名前・場所・回数・日時だけを持つ。
/// </summary>
public sealed record ApiClientRefusal
{
    public string Name { get; init; } = string.Empty;

    public string? ExePath { get; init; }

    public string? Origin { get; init; }

    /// <summary>続けて拒否した回数（許可したら行ごと消すので、0 には戻さない）。</summary>
    public int Count { get; init; }

    public DateTimeOffset LastRefusedAt { get; init; }

    /// <summary>
    /// 止めた日時（回数が上限に届いた時。止めていなければ null）。回数と上限から計算せずに持つ：
    /// 後で上限を上げても、人が見ていないうちに止めが外れて窓が出始めないように。一覧に日時も出す
    /// </summary>
    public DateTimeOffset? BlockedAt { get; init; }

    /// <summary>このソフトか（名前は大文字と小文字も分け、exe とオリジンは分けない。許しの控えと同じ）。</summary>
    public bool IsSame(string name, string? exePath, string? origin)
        => string.Equals(Name, name, StringComparison.Ordinal)
            && string.Equals(ExePath, exePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Origin, origin, StringComparison.OrdinalIgnoreCase);
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
    /// 許した書き込みと見張りの種類（<see cref="ApiScopes"/> の名前）。読むのはいつも許す。
    /// 列挙ではなく文字列で持つ：種類を足すたびに保存先の形式の版を上げずに済むように（知らない名前は読むときに捨てる）
    /// </summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>トークンの SHA-256（16進の小文字）。トークンは256ビットの乱数なので、塩や遅いハッシュは要らない。</summary>
    public string TokenHash { get; init; } = string.Empty;

    public DateTimeOffset GrantedAt { get; init; }

    /// <summary>最後に使った日時（まだなら null）。書くのは間引く（<c>ApiClientBook</c>）ので、分単位の目安。</summary>
    public DateTimeOffset? LastUsedAt { get; init; }

    /// <summary>
    /// 最後に出来事を取りに来た日時（<c>GET /v1/plugin/events</c>。plan 44。まだなら null）。書くのは使った日時と同じく10分ごとに間引く。
    /// 「応答していない」を決めるのは API がメモリに持つ正確な時刻で、これは次に起動するまでの目安
    /// </summary>
    public DateTimeOffset? LastPolledAt { get; init; }

    /// <summary>このソフトか（名前・exe・オリジンが同じ）。</summary>
    public bool IsSame(string name, string? exePath, string? origin)
        => string.Equals(Name, name, StringComparison.Ordinal)
            && string.Equals(ExePath, exePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Origin, origin, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// 見張り：商品ページを開いた、をソフトへ知らせる（plan 44・docs/spec/plugins.md）。見張りは書き込みではないが、使う人の操作を外へ出すので人が選んで許す
    /// （ユーザ判断 2026-11-12「選ばせましょう」）。名前は <c>watch.</c> と出来事の種類（<c>/v1/plugin/subscriptions</c> の <c>kinds</c>）
    /// </summary>
    public const string WatchItemOpened = WatchPrefix + "itemOpened";

    /// <summary>見張り：検索・フォルダの右側・ショップの複数選択が変わった。</summary>
    public const string WatchSelectionChanged = WatchPrefix + "selectionChanged";

    /// <summary>見張り：取り込みが終わった。</summary>
    public const string WatchImportFinished = WatchPrefix + "importFinished";

    public const string WatchPrefix = "watch.";

    /// <summary>書き込みの種類（画面に並べる順。軽い物から）。</summary>
    public static IReadOnlyList<string> Writes { get; } = [Marks, Recent, Notifications, Tags, Avatars, Modifications, Unity, Open];

    /// <summary>見張りの種類（画面に並べる順）。</summary>
    public static IReadOnlyList<string> Watches { get; } = [WatchItemOpened, WatchSelectionChanged, WatchImportFinished];

    /// <summary>
    /// 控えに書ける種類の全部（書き込み → 見張りの順）。権限を書き換えるときもこの順で並べ直す
    /// （書き込みだけで並べ直すと、チェックを1つ入れた時に見張りの許しが黙って消える）
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [.. Writes, .. Watches];

    public static bool IsKnown(string scope) => All.Contains(scope, StringComparer.Ordinal);

    public static bool IsWrite(string scope) => Writes.Contains(scope, StringComparer.Ordinal);

    public static bool IsWatch(string scope) => Watches.Contains(scope, StringComparer.Ordinal);

    /// <summary>出来事の種類（<c>itemOpened</c> など）を見るのに要る許しの名前。</summary>
    public static string WatchOf(string kind) => WatchPrefix + kind;

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
        WatchItemOpened => "商品ページを開いたこと",
        WatchSelectionChanged => "選んだ商品",
        WatchImportFinished => "取り込みが終わったこと",
        _ => scope,
    };
}
