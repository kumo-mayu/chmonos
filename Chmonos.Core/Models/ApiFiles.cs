namespace Chmonos.Core.Models;

/// <summary>
/// 外から使う API が動いている所（保存先の <c>api.json</c>。docs/spec/local-api.md「守り」）。**API が書く。人は書かない。**
/// 外の道具とアプリはこれを読んでつなぐ。トークンが入るので、バックアップの書き出しには入れない。
/// </summary>
public sealed record ApiEndpointRecord
{
    /// <summary>待っているポート（<c>127.0.0.1</c> だけで待つ）。</summary>
    public int Port { get; init; }

    /// <summary>
    /// トークン（<c>Authorization: Bearer …</c>）。起動のたびには変えない（外の道具が毎回読み直さずに済むように）。作り直す操作で変える。
    /// </summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>動いている API のプロセス。止まっていれば null（止まるときに消す）。</summary>
    public int? ProcessId { get; init; }

    /// <summary>API のバージョン（アプリと同じ番号）。</summary>
    public string? Version { get; init; }

    public DateTimeOffset? StartedAt { get; init; }
}

/// <summary>
/// 外から使う API の設定（保存先の <c>api-settings.json</c>）。Chmonos の設定画面からも、ファイルを手で書いても変えられる（ユーザ判断 2026-10-12）。
/// </summary>
public sealed record ApiSettings
{
    /// <summary>決まったポートの既定。他のソフトとぶつかりにくい、登録の無い番号から選んだ。ぶつかるときは設定で変える。</summary>
    public const int DefaultPort = 39847;

    /// <summary>
    /// API を使うか。オンならアプリ（画面あり・画面無しのどちらでも）と一緒に待ち受ける。既定はオフ（ユーザ判断 2026-10-13：
    /// ポートを開けてトークンを持つ道具に読ませるかは使う人が選ぶ。画面無しで起動しても、オフなら立ち上げない）
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// 外から書き込みを許すか（タグ・属性・お気に入り・Unity へ送る など。docs/history/headless-core-2026-10-13.md 段3）。既定はオフ。
    /// トークンは読むのと同じ1本（同じ Windows ユーザーの道具なら api.json を読めれば2本目も読めるので、分ける効き目が小さい。ユーザ判断 2026-10-13）。
    /// 変えたらその場で効く（API は api-settings.json の変化を見て読み直す）
    /// </summary>
    public bool AllowWrites { get; init; }

    public int Port { get; init; } = DefaultPort;

    /// <summary>
    /// CORS を許す住所（自作の Web ページ。例 <c>http://localhost:5173</c>）。空なら、ブラウザのページからは読めない。
    /// 読むときは欠けた配列（null）を空として受ける（手で直した JSON で落ちないように）。
    /// </summary>
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

    /// <summary>
    /// 画面を出さずに起動したとき（<c>--headless</c>）、監視フォルダの新しいファイルと前回の続きを取り込む。既定はオフ。
    /// 画面ありの起動の設定（<see cref="AppSettings.StartImportOnLaunch"/>）とは別に持つ（ユーザ判断 2026-10-13：経路は1本、設定は画面無し用を別に立てる）。
    /// 画面無しの起動は API を使う人の物なので、API の設定に置く
    /// </summary>
    public bool ImportOnHeadlessLaunch { get; init; }
}
