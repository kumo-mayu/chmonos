using System.Text;
using System.Net;
using Chmonos.Core.Models;

namespace Chmonos.Core.Booth;

public enum BoothFetchStatus
{
    Success,

    /// <summary>404。非公開・削除の判定カウントに数える唯一の結果。</summary>
    NotFound,

    /// <summary>タイムアウト・5xx・接続失敗。BOOTH側の一時的な不調なので非公開とは判定しない。</summary>
    TemporaryFailure,
}

public sealed class BoothFetchResult<T>
{
    public required BoothFetchStatus Status { get; init; }

    public T? Value { get; init; }

    public string? Error { get; init; }

    /// <summary>429だったか。状態としては一時エラーだが、こちらの出し過ぎなので扱いを変える。</summary>
    public bool IsRateLimited { get; init; }

    /// <summary>Retry-Afterで指示された待ち時間。ヘッダが無ければnull。</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>
    /// BOOTH から応答が1つも来なかったか（接続できない・タイムアウト）。状態としては一時エラー。
    ///
    /// 5xx・429・読めない応答は BOOTH が応答しているので立てない。
    /// 取り込みはこれが続いたら段を打ち切り（ネットにつながっていないのに全件を再試行で回ると、止まって見える）、
    /// 画面は「BOOTHの不調」ではなく「ネットにつながっていない」と言い分ける（ユーザ判断 2026-09-29）。
    /// </summary>
    public bool IsUnreachable { get; init; }

    /// <summary>
    /// BOOTH が 5xx（サーバの不調）を返したか。状態としては一時エラー。
    /// 続いたら打ち切りに数える（<see cref="BoothOutageWatch"/>）。落ちている間も全件を再試行で回ると、
    /// 復旧に時間の掛かる相手へ問い合わせを重ね続けるため（ユーザ判断 2026-09-29）
    /// </summary>
    public bool IsServerError { get; init; }

    /// <summary>
    /// 応答を受け取らずに捨てたか（許さない先への転送・転送の重ねすぎ・大きすぎる本文）。状態としては一時エラー。
    /// 取り直しても同じ応答が来るだけなので、再試行しない。
    /// </summary>
    public bool IsRejected { get; init; }

    /// <summary>相手が転送を指示した先（3xx の Location）。中の受け渡しだけに使い、呼び元へは返さない。</summary>
    internal Uri? RedirectTo { get; init; }

    public bool IsSuccess => Status == BoothFetchStatus.Success;

    public static BoothFetchResult<T> Rejected(string error)
        => new() { Status = BoothFetchStatus.TemporaryFailure, Error = error, IsRejected = true };

    public static BoothFetchResult<T> Success(T value) => new() { Status = BoothFetchStatus.Success, Value = value };

    public static BoothFetchResult<T> NotFound() => new() { Status = BoothFetchStatus.NotFound };

    public static BoothFetchResult<T> Temporary(string error, TimeSpan? retryAfter = null)
        => new() { Status = BoothFetchStatus.TemporaryFailure, Error = error, RetryAfter = retryAfter };

    public static BoothFetchResult<T> Unreachable(string error)
        => new() { Status = BoothFetchStatus.TemporaryFailure, Error = error, IsUnreachable = true };

    public static BoothFetchResult<T> RateLimited(string error, TimeSpan? retryAfter)
        => new()
        {
            Status = BoothFetchStatus.TemporaryFailure,
            Error = error,
            IsRateLimited = true,
            RetryAfter = retryAfter,
        };
}

public interface IBoothClient
{
    Task<BoothFetchResult<string>> GetItemJsonAsync(string itemId, CancellationToken cancellationToken = default);

    Task<BoothFetchResult<string>> GetItemHtmlAsync(string itemId, CancellationToken cancellationToken = default);

    Task<BoothFetchResult<byte[]>> GetBinaryAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>BOOTH内検索。手掛かりが無いファイルの候補を出すために使う。</summary>
    Task<BoothFetchResult<string>> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// BOOTH内検索の n ページ目（1ページ60件）。自動検索が1語で引き直した結果の続きを見るときだけ使う。
    /// 既定は1ページ目だけ答える（検索の2ページ目を使わない試験の作り物が、全部書き足さずに済むように）。
    /// </summary>
    Task<BoothFetchResult<string>> SearchAsync(string query, int page, CancellationToken cancellationToken = default)
        => page <= 1
            ? SearchAsync(query, cancellationToken)
            : Task.FromResult(BoothFetchResult<string>.NotFound());

    /// <summary>探しているものが見つかった時点で受信をやめる取得。</summary>
    Task<BoothFetchResult<string>> GetTextUntilAsync(
        string url,
        Func<string, bool> found,
        int maxBytes = 262144,
        CancellationToken cancellationToken = default);

    /// <summary>現在のリクエスト間隔（ミリ秒）。429を受けると設定値より広がる。</summary>
    int CurrentIntervalMs { get; }

    /// <summary>429を受けて自動減速している最中か。</summary>
    bool IsThrottled { get; }

    /// <summary>
    /// 今なにをしているかが変わったときに知らせる。
    /// 取得は直列なので、どの瞬間も起きていることは1つだけ。
    /// </summary>
    event Action<BoothActivity>? ActivityChanged;
}

/// <summary>
/// BOOTHへの取得。サーバに負荷をかけないよう、リクエストは必ず直列で、1件ごとに間隔を空ける。
/// 並列化はしない（pixiv共通規約の「短時間の機械的な大量操作」を避けるため）。
///
/// **直列と間隔は、同じ PC で動く全部のプロセスを合わせて守る**（<see cref="BoothMachineGate"/>。2026-09-30）。
/// このクラスの中の門（<see cref="PriorityGate"/>）は1つのプロセスの中の順番（優先順位）を決め、
/// PC の門は、保存先の違うアプリ・評価台・道具が同時に出ないようにする。
///
/// 失敗の扱いは2種類に分ける。404だけが非公開判定のカウント対象で、
/// タイムアウトや5xxは一時エラーとして再試行し、カウントには数えない。
/// BOOTH側の一時的な障害で商品が「非公開」と誤判定されるのを防ぐため。
///
/// 429は「こちらが出し過ぎ」という相手からの申告なので、固定の再試行間隔ではなく
/// Retry-Afterに従い、さらに以降のリクエスト間隔自体を倍にする（自動減速）。
/// 減速はプロセスが生きている間ずっと維持し、自動では戻さない。
/// 戻す条件を機械的に決めると、結局また叩きに行って同じことを繰り返すため。
/// </summary>
public sealed class BoothClient : IBoothClient
{
    // ブラウザを名乗らない。自動で取りに行く通信がブラウザの顔をしていると、
    // 通信を見る型のセキュリティソフトに「スクレイパー」と見られ得る（#46）。
    // 相手にとっても、誰が来ているかが名乗りだけで分かる方が行儀がよい
    public const string UserAgent = "Chmonos/0.1 (personal library manager)";

    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8)];

    /// <summary>
    /// 1本の問い合わせの期限。**送ってから本文を読み終えるまで**を合わせて数える。
    ///
    /// 前は HttpClient.Timeout（30秒）だけで、見出しだけ先に受ける読み方では見出しまでしか効かなかった。
    /// 本文が途中で止まると、PC で1つの門を握ったまま戻らず、ほかのアプリも全部止まる。
    /// 長さは前と同じ30秒：応答は手元の実測で 0.16〜0.3 秒、画像の上限（<see cref="MaxImageBytes"/>）を
    /// 30秒で受けるのに要る速さは約 5.6 Mbps で、普通の回線なら届く。
    /// </summary>
    internal static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 転送をたどる回数の上限。BOOTH の転送は「言語の付かない URL → /ja/」のような1〜2段で、
    /// 5段を超えるのは輪になっているか、相手の作りが変わったとき。
    /// </summary>
    internal const int MaxRedirects = 5;

    /// <summary>
    /// 文字の応答（商品の JSON・ページ・検索）の受信の上限。商品ページは100KB超・ショップのバナー探しは
    /// 先頭の256KBで足りている。桁を1つ以上空けて8MB——これを超えるのは BOOTH のページではない。
    /// </summary>
    internal const int MaxTextBytes = 8 * 1024 * 1024;

    /// <summary>
    /// 画像の受信の上限。いちばん大きいショップのバナーが実測で4MB（<c>ImagePipeline</c> の注記）、
    /// 商品画像の原寸（3000×3000 前後の JPEG）も数MB。5倍の余裕を見て20MB。
    /// </summary>
    internal const int MaxImageBytes = 20 * 1024 * 1024;

    /// <summary>
    /// 転送してよい先。今の問い合わせ先から決める：商品の JSON・ページ・検索・一覧は booth.pm、
    /// ショップのページは &lt;ショップ&gt;.booth.pm、画像は booth.pximg.net。
    /// ほかの先へ出る道を作ると、門の外の相手に BOOTH と同じ顔で問い合わせることになる。
    /// </summary>
    internal static bool IsAllowedRedirectTarget(Uri target)
    {
        if (!target.IsAbsoluteUri || target.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var host = target.IdnHost.ToLowerInvariant();
        return host == "booth.pm"
            || host.EndsWith(".booth.pm", StringComparison.Ordinal)
            || host == "booth.pximg.net";
    }

    /// <summary>
    /// 手元の作り物のサーバ（<c>http://127.0.0.1</c> などのループバック）へも出してよいか。
    /// **2つのプロセスから門を叩く確かめ（<c>experiments/BoothGateProbe</c>）と本体の試験だけが立てる。**
    /// 外に出していない口（internal の init）なので、アプリ・道具・評価台の組み立てからは開けない
    /// </summary>
    internal bool AllowsLoopbackForProbe { get; init; }

    /// <summary>問い合わせてよい先か。最初の先にも転送の先にも同じ一覧を当てる。</summary>
    private bool IsAllowedTarget(Uri target)
        => IsAllowedRedirectTarget(target) || (AllowsLoopbackForProbe && target.IsAbsoluteUri && target.IsLoopback);

    /// <summary>ログに書く先。URL の残り（商品の番号や問い合わせの中身）は書かない。</summary>
    private static string DescribeHost(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? $"{parsed.Scheme}://{parsed.Host}" : "URL として読めない";

    /// <summary>
    /// BOOTH への問い合わせに使う HttpClient を組む。**自動の転送は切る。**
    ///
    /// 自動の転送は通信の層の中で転送先へ出るので、門（間隔・1本ずつ）を通らず、先のホストも見られない。
    /// 転送は <see cref="BoothClient"/> が受けて、先を確かめてから門を通して取り直す。
    /// 期限は <see cref="DefaultRequestTimeout"/> で BoothClient が持つので、HttpClient の期限は切る（時計を2つにしない）。
    /// </summary>
    /// <param name="handler">試験の作り物。null なら本物の通信。渡した物が転送する型なら、そこでも切る。</param>
    public static HttpClient CreateHttpClient(HttpMessageHandler? handler = null)
    {
        var outlet = handler ?? new SocketsHttpHandler();
        TurnOffAutoRedirect(outlet);
        return new HttpClient(outlet) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private static void TurnOffAutoRedirect(HttpMessageHandler? handler)
    {
        while (handler is not null)
        {
            switch (handler)
            {
                case SocketsHttpHandler sockets:
                    sockets.AllowAutoRedirect = false;
                    return;
                case HttpClientHandler client:
                    client.AllowAutoRedirect = false;
                    return;
                case DelegatingHandler delegating:
                    handler = delegating.InnerHandler;
                    break;
                default:
                    return;
            }
        }
    }

    /// <summary>1本の問い合わせの期限。試験だけが短くする（30秒を実際に待たないため）。</summary>
    internal TimeSpan RequestTimeout { get; init; } = DefaultRequestTimeout;

    private readonly HttpClient _httpClient;
    private readonly Func<AppSettings> _currentSettings;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly PriorityGate _gate = new();

    /// <summary>
    /// PC で1つの門（<see cref="BoothMachineGate"/>）。null なら、このクライアントの中の門だけで進む。
    /// </summary>
    private readonly BoothMachineGate? _machineGate;

    /// <summary>
    /// 間を測る時計。**壁の時計ではなく、起動してからの刻みで測る**（<see cref="TimeProvider.GetTimestamp"/>）。
    /// 壁の時計で測ると、時計が戻ったときに戻った分だけ待ち、進んだときに間を空けずに出る。
    /// 刻みは PC で1つなので、別のプロセスが書いた値とも比べられる。
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>PC の門に書く、このクライアントの印。最後に問い合わせたのが自分かを見分ける。</summary>
    private readonly string _id = Guid.NewGuid().ToString("N")[..12];

    /// <summary>最後に問い合わせた刻み。まだ問い合わせていなければ null。ゲートの中でしか読み書きしない。</summary>
    private long? _lastRequestAt;
    private int _currentIntervalMs;

    /// <summary><see cref="_currentIntervalMs"/> を決めたときの、設定の間隔。</summary>
    private int _intervalBaseMs;

    /// <param name="delay">
    /// 待機処理。**相手が作り物の試験だけが差し替える**（実際に待たせないため）。差し替えた組み立ては PC の門に入らない（下の組み立てに理由）。
    /// </param>
    public BoothClient(
        HttpClient httpClient,
        AppSettings? settings = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
        : this(httpClient, SettingsSource.Fixed(settings), delay)
    {
    }

    /// <summary>
    /// **待ちを差し替えなければ、この Windows のユーザで1つの門に入る**（<see cref="BoothMachineGate.ForThisUser"/>）。
    /// 門を選ぶ引数は外に出していないので、アプリ・コマンドライン・評価台・道具のどれも、作れば入る
    /// （付け忘れが決め事を破る側に倒れない。門を避ける作り方を残さない）。
    ///
    /// **待ちを差し替えた組み立て（<paramref name="delay"/> を渡した物）だけは入らない。**実際には待たない物が入ると、
    /// 待たずに本物の門の中身を書き換え、隣で動いている本物のアプリを待たせる。試験の結果も、その PC で
    /// アプリが動いているかで変わってしまう。待ちを差し替えるのは相手が作り物の試験だけで、
    /// **本物の BOOTH を相手に待ちを差し替えるのは、門より前に決め事1そのものを破る**（間隔が無くなる）。
    /// </summary>
    /// <param name="currentSettings">
    /// 使うたびに今の設定を返すもの（<see cref="SettingsSource"/>）。取得の間隔も保存した直後から効く。
    /// </param>
    /// <param name="delay">待機処理。相手が作り物の試験だけが差し替える。</param>
    public BoothClient(
        HttpClient httpClient,
        Func<AppSettings> currentSettings,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
        : this(httpClient, currentSettings, delay, delay is null ? BoothMachineGate.ForThisUser : null, TimeProvider.System)
    {
    }

    /// <summary>
    /// 門と時計を渡して組む。**外には出さない**（本体の試験と、2つのプロセスから叩く確かめ <c>experiments/BoothGateProbe</c> だけ）。
    /// どちらも相手は作り物で、門は自分の置き場の物を渡す。外に出すと、本物の BOOTH を相手に別の門を渡す作り方ができてしまう。
    /// </summary>
    /// <param name="machineGate">PC で1つの門。null なら、このクライアントの中の門だけで進む。</param>
    /// <param name="clock">間を測る時計。試験では進め方を決められる物を渡す。</param>
    internal BoothClient(
        HttpClient httpClient,
        Func<AppSettings> currentSettings,
        Func<TimeSpan, CancellationToken, Task>? delay,
        BoothMachineGate? machineGate,
        TimeProvider clock)
    {
        _httpClient = httpClient;
        _currentSettings = currentSettings;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
        _machineGate = machineGate;
        _clock = clock;
        _currentIntervalMs = _intervalBaseMs = ConfiguredIntervalMs;

        if (!_httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd(UserAgent))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        }
    }

    /// <summary>
    /// 今どの優先度で取りに行っているか。
    ///
    /// 引数で持ち回さないのは、優先度が**呼び出しの1本ごと**ではなく
    /// 「今この作業をしている」という文脈に付くものだから。
    /// 引数にすると <c>ItemService</c> → <c>ImagePipeline</c> → <c>BoothClient</c> の
    /// 全段に通す必要があり、1箇所渡し忘れても黙って既定に落ちる。
    /// <see cref="AsyncLocal{T}"/> なら <see cref="Prioritize"/> の内側で始めた取得は
    /// 何段先でも同じ優先度になる。
    /// </summary>
    private static readonly AsyncLocal<BoothPriority?> Ambient = new();

    /// <summary>
    /// この範囲で始める取得の優先順位を決める。
    ///
    /// <code>using var _ = client.Prioritize(BoothPriority.User);</code>
    /// </summary>
    public static IDisposable Prioritize(BoothPriority priority)
    {
        var previous = Ambient.Value;
        Ambient.Value = priority;
        return new PriorityScope(previous);
    }

    private sealed class PriorityScope(BoothPriority? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }

    /// <summary>
    /// 範囲が指定されていなければ**一番下の段**として扱う（ユーザ判断 2026-09-21・Q4）。
    ///
    /// 前は取り込みの本体（②）を既定にしていたので、**段を付け忘れた問い合わせが
    /// 他を押しのける側に倒れていた**。一番下にしておけば、付け忘れは人を待たせない側に倒れる。
    /// </summary>
    private static BoothPriority CurrentPriority => Ambient.Value ?? BoothPriority.Background;

    /// <summary>入っている PC の門。試験で「待ちを差し替えた物は本物の門に入らない」を確かめる。</summary>
    internal BoothMachineGate? MachineGate => _machineGate;

    /// <summary>順番待ちの本数。溜まり具合を見るためのもので、判断には使わない。</summary>
    public int WaitingRequestCount => _gate.WaitingCount;

    public int CurrentIntervalMs => SyncedIntervalMs();

    /// <summary>設定より広げている最中か。**床を踏んだ分は「広げた」ではない**（設定が下限より短いだけ）。</summary>
    public bool IsThrottled => SyncedIntervalMs() > ConfiguredIntervalMs;

    /// <summary>今の設定。**抱えずに毎回読む。**</summary>
    private AppSettings _settings => _currentSettings();

    /// <summary>
    /// 設定で間隔が変わっていたら、今の間隔をそこへ合わせ直してから返す。
    ///
    /// 合わせ直さないと、間隔を縮めたときに元の値が「429で広げている最中」に見え続け、
    /// 広げたときには新しい値が効かない。
    /// **429で広げている最中なら、広げた分は保つ**——相手が待てと言っているのに、
    /// 設定を触っただけで詰めて問い合わせることになる。
    /// </summary>
    /// <summary>
    /// 設定の間隔。**床（1.5秒）はここで踏む。**
    ///
    /// 下限を守っていたのは設定の入口（<see cref="AppSettings.Normalized"/>）だけで、
    /// 設定を通さずに組み立てる道が増えると守れなくなる。
    /// 相手に負担をかけないための決め事なので、通信をする側にも床を置く。
    /// </summary>
    private int ConfiguredIntervalMs => Math.Max(_settings.FetchIntervalMs, AppSettings.MinFetchIntervalMs);

    private int SyncedIntervalMs()
    {
        var configured = ConfiguredIntervalMs;
        if (configured != _intervalBaseMs)
        {
            var throttled = _currentIntervalMs > _intervalBaseMs;
            _currentIntervalMs = throttled ? Math.Max(_currentIntervalMs, configured) : configured;
            _intervalBaseMs = configured;
        }

        return _currentIntervalMs;
    }

    public static string ItemJsonUrl(string itemId) => $"https://booth.pm/ja/items/{itemId}.json";

    public static string ItemPageUrl(string itemId) => $"https://booth.pm/ja/items/{itemId}";

    /// <summary>
    /// この商品のBOOTHページ。**BOOTHに無い商品として登録したものには無い（null）。**
    ///
    /// 仮IDでURLを組むと、存在しない商品の404ページへ送ることになる。
    /// 「BOOTHで開く」も「リンクをコピー」も、ここがnullなら出さない。
    /// </summary>
    public static string? PageUrlFor(ItemRecord item)
        => item.IsLocalOnly ? null : item.Booth.Url ?? ItemPageUrl(item.Id);

    public static string SearchUrl(string query) => $"https://booth.pm/ja/search/{Uri.EscapeDataString(query)}";

    /// <summary>検索の n ページ目。BOOTH の検索ページのページ送りのリンクと同じ書き方（<c>?page=2</c>）。</summary>
    public static string SearchUrl(string query, int page)
        => page <= 1 ? SearchUrl(query) : $"{SearchUrl(query)}?page={page}";

    public Task<BoothFetchResult<string>> SearchAsync(string query, CancellationToken cancellationToken = default)
        => GetStringAsync(SearchUrl(query), cancellationToken);

    public Task<BoothFetchResult<string>> SearchAsync(string query, int page, CancellationToken cancellationToken = default)
        => GetStringAsync(SearchUrl(query, page), cancellationToken);

    public Task<BoothFetchResult<string>> GetItemJsonAsync(string itemId, CancellationToken cancellationToken = default)
        => GetStringAsync(ItemJsonUrl(itemId), cancellationToken);

    public Task<BoothFetchResult<string>> GetItemHtmlAsync(string itemId, CancellationToken cancellationToken = default)
        => GetStringAsync(ItemPageUrl(itemId), cancellationToken);

    public Task<BoothFetchResult<byte[]>> GetBinaryAsync(string url, CancellationToken cancellationToken = default)
        => SendWithRetryAsync(url, (response, token) => ReadBytesAsync(response, MaxImageBytes, token), cancellationToken);

    /// <summary>本文が上限を超えた。受信をやめて捨てる。</summary>
    private sealed class BodyTooLargeException(int limit) : Exception
    {
        public int Limit { get; } = limit;
    }

    /// <summary>
    /// 本文を上限まで受ける。**上限を超えたら、そこで受けるのをやめる**（全部読んでから捨てない）。
    /// 長さが見出しに書いてあれば、読み始める前に断る。
    /// </summary>
    private static async Task<byte[]> ReadBytesAsync(HttpResponseMessage response, int limit, CancellationToken cancellationToken)
    {
        var declared = response.Content.Headers.ContentLength;
        if (declared > limit)
        {
            throw new BodyTooLargeException(limit);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var body = new MemoryStream(declared is { } length ? (int)length : 0);
        var chunk = new byte[64 * 1024];

        while (true)
        {
            var count = await stream.ReadAsync(chunk, cancellationToken);
            if (count == 0)
            {
                return body.ToArray();
            }

            if (body.Length + count > limit)
            {
                throw new BodyTooLargeException(limit);
            }

            body.Write(chunk, 0, count);
        }
    }

    /// <summary>
    /// 文字の本文を上限まで受けて読む。文字の符号は見出しの charset、無ければ UTF-8（BOM があればそちら）。
    /// ReadAsStringAsync と同じ読み方で、上限と期限だけ足した物。
    /// </summary>
    private static async Task<string> ReadTextAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var bytes = await ReadBytesAsync(response, MaxTextBytes, cancellationToken);

        var encoding = Encoding.UTF8;
        if (response.Content.Headers.ContentType?.CharSet is { Length: > 0 } charset)
        {
            try
            {
                encoding = Encoding.GetEncoding(charset.Trim('"'));
            }
            catch (ArgumentException)
            {
                // 知らない符号名なら UTF-8 で読む（BOOTH は UTF-8 で返している）
            }
        }

        using var reader = new StreamReader(new MemoryStream(bytes), encoding, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>
    /// 探しているものが見つかった時点で受信をやめる取得。
    ///
    /// ショップのバナーはHTMLの先頭付近にしか無いのに、ページ全体は100KB超ある。
    /// 最後まで受け取る理由が無いので、見つかったら切る。
    /// <paramref name="found"/> が一度も真にならなければ、上限まで読んだものを返す。
    /// </summary>
    public Task<BoothFetchResult<string>> GetTextUntilAsync(
        string url,
        Func<string, bool> found,
        int maxBytes = 262144,
        CancellationToken cancellationToken = default)
        => SendWithRetryAsync(
            url,
            (response, token) => ReadUntilAsync(response, found, maxBytes, token),
            cancellationToken);

    private static async Task<string> ReadUntilAsync(
        HttpResponseMessage response,
        Func<string, bool> found,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        var decoder = Encoding.UTF8.GetDecoder();
        var buffer = new byte[16 * 1024];
        var characters = new char[buffer.Length];
        var text = new StringBuilder();
        var read = 0;

        while (read < maxBytes)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            {
                break;
            }

            read += count;

            // 文字の途中で切れることがあるので、状態を持つデコーダで継ぎ足していく
            var written = decoder.GetChars(buffer, 0, count, characters, 0);
            text.Append(characters, 0, written);

            if (found(text.ToString()))
            {
                break;
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// BOOTHの一覧ページを1枚取る。**カテゴリ表を取り出すためだけにある。**
    ///
    /// 商品の取得と同じゲートを通るので、1本ずつ・1.5秒以上空けて出る。
    /// booth.pm 以外は受け付けない——ここを汎用のGETにすると、
    /// ゲートを通さない通信を書く道ができてしまう。
    /// </summary>
    public Task<BoothFetchResult<string>> GetBrowsePageAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        if (!url.StartsWith("https://booth.pm/", StringComparison.Ordinal))
        {
            throw new ArgumentException("booth.pm のページだけを取ります。", nameof(url));
        }

        return GetStringAsync(url, cancellationToken);
    }

    private Task<BoothFetchResult<string>> GetStringAsync(string url, CancellationToken cancellationToken)
        => SendWithRetryAsync(url, ReadTextAsync, cancellationToken);

    /// <summary>
    /// 1本を取り、転送されたら先を確かめて取り直す。**転送先への問い合わせも、1本ずつ門を通す**
    /// （間隔を空けて・優先度は元と同じ。優先度は文脈に付いているので、ここで取り直しても変わらない）。
    /// </summary>
    private async Task<BoothFetchResult<T>> SendFollowingRedirectsAsync<T>(
        string url,
        Func<HttpResponseMessage, CancellationToken, Task<T>> readBody,
        int attempt,
        CancellationToken cancellationToken)
    {
        var current = url;
        for (var hops = 0; ; hops++)
        {
            var result = await SendOnceAsync(current, readBody, attempt, cancellationToken);
            if (result.RedirectTo is not { } next)
            {
                return result;
            }

            if (hops >= MaxRedirects)
            {
                Diagnostics.AppLog.Warn("BOOTHへの問い合わせ", $"転送が {MaxRedirects} 回を超えたので打ち切った");
                return BoothFetchResult<T>.Rejected("BOOTHからの転送が多すぎます");
            }

            if (!IsAllowedTarget(next))
            {
                Diagnostics.AppLog.Warn("BOOTHへの問い合わせ", $"BOOTH の外への転送は追わない（転送先 {next.Scheme}://{next.Host}）");
                return BoothFetchResult<T>.Rejected("BOOTHの外へ転送されました");
            }

            current = next.AbsoluteUri;
        }
    }

    private async Task<BoothFetchResult<T>> SendWithRetryAsync<T>(
        string url,
        Func<HttpResponseMessage, CancellationToken, Task<T>> readBody,
        CancellationToken cancellationToken)
    {
        // 最初に渡された先も、転送の先と同じ一覧で縛る（外部の点検 2026-10-06・ユーザ判断「縛る」）。
        // 画像の URL は商品の JSON（booth.images[].originalUrl）から来るので、相手の書いた先へそのまま出ていた。
        // 門に並ぶ前に断る（外れた先のために、ほかの問い合わせを待たせない）
        if (!Uri.TryCreate(url, UriKind.Absolute, out var first) || !IsAllowedTarget(first))
        {
            Diagnostics.AppLog.Warn("BOOTHへの問い合わせ", $"BOOTH の外の先には問い合わせない（{DescribeHost(url)}）");
            return BoothFetchResult<T>.Rejected("BOOTHの外の場所なので取りに行きませんでした");
        }

        BoothFetchResult<T>? previous = null;

        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            if (attempt > 0)
            {
                // ここで待つのは**こちらの決めた間合い**（2秒→8秒）だけ。ゲートの外なので、その間は他の問い合わせが進む。
                // 相手が Retry-After で指示した待ちは、ゲートの中で全員に守らせる（<see cref="_quietUntil"/>・C15）。
                // 前はここで指示の分まで待っていたので、この1本が待つ間に他の商品が取りに行っていた
                var wait = RetryDelays[attempt - 1];

                var attemptNumber = attempt;
                await CountDownAsync(
                    wait,
                    remaining => new BoothActivity
                    {
                        Kind = BoothActivityKind.Retrying,
                        Target = DescribeTarget(url),
                        Total = wait,
                        Remaining = remaining,
                        Attempt = attemptNumber,
                        MaxAttempts = RetryDelays.Length,
                        IsThrottled = IsThrottled,
                    },
                    cancellationToken);
            }

            // 取り直しは元の URL から（転送の回数も数え直す）
            var result = await SendFollowingRedirectsAsync(url, readBody, attempt, cancellationToken);
            if (result.Status != BoothFetchStatus.TemporaryFailure || result.IsRejected)
            {
                return result;
            }

            // 指示された待ち時間が長すぎる場合は、粘らずに諦めて次回の実行に回す。
            if (result.RetryAfter > MaxRetryAfterWait)
            {
                return result;
            }

            previous = result;
        }

        return previous ?? BoothFetchResult<T>.Temporary("不明なエラー");
    }

    private async Task<BoothFetchResult<T>> SendOnceAsync<T>(
        string url,
        Func<HttpResponseMessage, CancellationToken, Task<T>> readBody,
        int attempt,
        CancellationToken cancellationToken)
    {
        var target = DescribeTarget(url);

        // 期限は送る直前から数える（門の順番待ち・間隔の待ちは入れない）。本文を読み終えるまで効かせる
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await _gate.EnterAsync(CurrentPriority, cancellationToken);

        // PC の門を握った物。握るのは間隔を待ち終えてからで、問い合わせが終わるまで持つ（この間、ほかのアプリは出られない）
        BoothMachineGate.Turn? turn = null;

        // 送り出したか。**送った後は、どう抜けても最後の問い合わせの時刻を更新する**（finally）。
        // 前は成功・HTTPの失敗・タイムアウトの道でしか更新しておらず、送った後の中断
        // （OperationCanceledException）や想定外の例外で抜けると古い時刻のまま残り、
        // 次の1本が 1.5 秒を空けずに出得た（絶対に破らない決め事1）
        var sent = false;
        try
        {
            turn = await WaitForTurnAsync(target, attempt, cancellationToken);

            Report(new BoothActivity
            {
                Kind = BoothActivityKind.Sending,
                Target = target,
                IsThrottled = IsThrottled,
            });

            // 送る直前に立てる。GetAsync の中で投げても、相手に届いているかは分からないので
            // 「届いた」側に倒す（間を空けすぎても困る人はいない）
            sent = true;

            // **送る前に「送っている最中」と書く。**送ったまま落ちると、終わった刻みを書く人が居ない。
            // 印が残っていれば、次に握ったアプリが「今終わった」とみなして間を空ける
            if (turn is not null)
            {
                // 前に Windows を起動していたときの残りは、ここで捨てる（刻みを書き直すと、後からは見分けられない）
                var sentAt = _clock.GetTimestamp();
                turn.Write(new BoothGateState
                {
                    EndedAt = sentAt,
                    InFlight = true,
                    IntervalMs = SyncedIntervalMs(),
                    QuietUntil = LaterQuiet(turn.State, sentAt),
                    Sender = _id,
                });
            }

            // ヘッダだけ先に受け取る。本文を途中で打ち切る呼び出し（ショップのバナー探し）が
            // 実際に通信を止められるようにするため。全部読む呼び出しの動きは変わらない。
            deadline.CancelAfter(RequestTimeout);
            using var response = await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                deadline.Token);

            // 出口が自分で転送をたどっていた（自動の転送を切っていない HttpClient を渡された）。
            // 転送先へはもう門の外で出てしまったので、せめて受け取らずに捨て、ログに残して組み立てを直させる
            if (response.RequestMessage?.RequestUri is { } answered
                && Uri.TryCreate(url, UriKind.Absolute, out var asked)
                && answered != asked)
            {
                Diagnostics.AppLog.Warn(
                    "BOOTHへの問い合わせ",
                    "通信の出口が自動で転送をたどった。HttpClient は BoothClient.CreateHttpClient で組む");
                return BoothFetchResult<T>.Rejected("BOOTHからの転送を受け取れませんでした");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return BoothFetchResult<T>.NotFound();
            }

            // 転送は、ここでは追わずに先を返す。先を確かめて、門を通して取り直すのは呼び元（SendFollowingRedirectsAsync）
            if (IsRedirect(response.StatusCode))
            {
                return response.Headers.Location is { } location
                    && Uri.TryCreate(new Uri(url), location, out var next)
                    ? new BoothFetchResult<T> { Status = BoothFetchStatus.TemporaryFailure, RedirectTo = next }
                    : BoothFetchResult<T>.Rejected("BOOTHからの転送の先が読めません");
            }

            // 相手が待てと言ってきたら（429 に限らず 503 などでも）、**次の誰もが**その間は取りに行かない
            // （ユーザ判断 C15：「BOOTH に待てと言われたら、他の商品も取りに行かずに待つ」）
            var retryAfter = ReadRetryAfter(response);
            if (!response.IsSuccessStatusCode && retryAfter is { } instructed)
            {
                HoldQuiet(instructed);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                SlowDown();
                return BoothFetchResult<T>.RateLimited(
                    retryAfter is { } wait
                        ? $"BOOTHが混み合っています。{wait.TotalSeconds:0} 秒待つよう指示されました"
                        : "BOOTHが混み合っています",
                    retryAfter);
            }

            if (!response.IsSuccessStatusCode)
            {
                // 503 なども Retry-After を付けてくることがある。付いていれば「こちらが詰めすぎ」の申告として
                // 429 と同じく以降の間隔も広げる（待てと言われた直後に元の間隔で叩き直さない）。
                // 付いていない 5xx は相手の不調なので広げない
                if (retryAfter is not null)
                {
                    SlowDown();
                }

                var failed = BoothFetchResult<T>.Temporary($"HTTP {(int)response.StatusCode}", retryAfter);
                return (int)response.StatusCode >= 500
                    ? new BoothFetchResult<T>
                    {
                        Status = failed.Status,
                        Error = failed.Error,
                        RetryAfter = failed.RetryAfter,
                        IsServerError = true,
                    }
                    : failed;
            }

            // 広げた間隔は、成功が続いたら少しずつ戻す（C16）
            EaseBack();
            return BoothFetchResult<T>.Success(await readBody(response, deadline.Token));
        }
        catch (BodyTooLargeException tooLarge)
        {
            // 受けるのをやめた残りは、応答を捨てれば（using）つながりごと切られる
            Diagnostics.AppLog.Warn("BOOTHへの問い合わせ", $"応答が {tooLarge.Limit / (1024 * 1024)}MB を超えたので受信をやめた（{target}）");
            return BoothFetchResult<T>.Rejected("BOOTHからの応答が大きすぎます");
        }
        catch (HttpRequestException exception)
        {
            // 理由は画面や取り込みの結果にそのまま出る。.NET の文（英語・内部の名前）は出さず、中身はログへ
            Diagnostics.AppLog.Error("BOOTHへの問い合わせ", exception);
            // 応答が来なかったことも結果に載せる。受け取る側が「BOOTHの不調」と「つながっていない」を言い分け、
            // 取り込みは続いたら段を打ち切る（ユーザ判断 2026-09-29）
            return BoothFetchResult<T>.Unreachable(Services.FailureText.Cause(exception));
        }
        catch (OperationCanceledException canceled) when (
            !cancellationToken.IsCancellationRequested
            && (deadline.IsCancellationRequested || canceled is TaskCanceledException))
        {
            // 見出しの前でも本文の途中でも、期限で切れたらここ（本文の読み取りは TaskCanceled でない取り消しも投げる）。
            // TaskCanceled は渡された HttpClient の側の期限。期限でも呼び元の中断でもない取り消しは、そのまま上へ返す
            return BoothFetchResult<T>.Unreachable("タイムアウトしました");
        }
        catch (IOException exception)
        {
            // 本文の途中でつながりが切れた。BOOTH は応答しているので「つながっていない」とは言わない
            Diagnostics.AppLog.Error("BOOTHへの問い合わせ", exception);
            return BoothFetchResult<T>.Temporary("BOOTHからの受信が途中で切れました");
        }
        finally
        {
            if (sent)
            {
                var endedAt = _clock.GetTimestamp();
                _lastRequestAt = endedAt;

                // ほかのアプリにも、終わった刻み・今の間隔（429 で広げていれば広げた値）・待てと言われた刻みを伝える。
                // 待っていた人の印は消す——残すと、もう居ない相手に譲り続ける
                turn?.Write(new BoothGateState
                {
                    EndedAt = endedAt,
                    IntervalMs = SyncedIntervalMs(),
                    QuietUntil = LaterQuiet(turn.State, endedAt),
                    Sender = _id,
                });
            }

            // 握ったまま間隔を待たない。次の1本が間隔を待つ間に、ほかのアプリが握って中身を読める
            turn?.Dispose();
            _gate.Release();

            // 直列なので、ゲートを出た時点で「何もしていない」に戻せる。
            // 次の1本がすぐ入るならそちらが上書きする
            Report(BoothActivity.Idle);
        }
    }

    /// <summary>先を Location で示す転送か。300・304 などは先を示さないので、ふつうの失敗として扱う。</summary>
    private static bool IsRedirect(HttpStatusCode status)
        => status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    /// <summary>
    /// URLを人に見せる短い名前にする。生のURLを出しても読めないし、横にも収まらない。
    /// </summary>
    private static string DescribeTarget(string url)
    {
        if (url.Contains("/items/", StringComparison.Ordinal))
        {
            var tail = url[(url.LastIndexOf("/items/", StringComparison.Ordinal) + 7)..];
            var id = new string(tail.TakeWhile(char.IsAsciiDigit).ToArray());

            if (id.Length > 0)
            {
                return url.EndsWith(".json", StringComparison.Ordinal) ? $"商品 {id}" : $"商品 {id} のページ";
            }
        }

        if (url.Contains("/search/", StringComparison.Ordinal))
        {
            return "検索";
        }

        return url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                ? "画像"
                : "ページ";
    }

    /// <summary>
    /// 相手が Retry-After で指示した「この時刻までは来るな」。**ゲートの中で待つので、誰も追い越せない**（C15）。
    /// 付き合うのは設定の上限（既定60秒）まで。それより長い指示は、その1本は諦めて次回に回すが（前から同じ）、
    /// 他の問い合わせは上限の分だけは待つ——すぐ叩き直すと、同じ指示をもう一度受けるだけなので。
    /// ゲートの中でしか読み書きしない。
    /// </summary>
    private long? _quietUntil;

    private void HoldQuiet(TimeSpan instructed)
    {
        var wait = instructed > MaxRetryAfterWait ? MaxRetryAfterWait : instructed;
        var until = After(_clock.GetTimestamp(), wait);
        if (_quietUntil is not { } current || until > current)
        {
            _quietUntil = until;
        }
    }

    /// <summary>刻みに時間を足す。</summary>
    private long After(long stamp, TimeSpan span) => stamp + (long)(span.TotalSeconds * _clock.TimestampFrequency);

    private long After(long stamp, int milliseconds) => After(stamp, TimeSpan.FromMilliseconds(milliseconds));

    /// <summary>
    /// PC の門の中身を信じてよいか。**終わった刻みが今より先なら、前に Windows を起動していたときの残り**
    /// （刻みは起動からの経過で、起動している間は戻らない）。起動し直すのに間隔より長くかかっているので、無いものとして扱う。
    /// 信じると、前の起動の長さの分だけ待つことになる。
    /// </summary>
    private static bool IsFromThisBoot(BoothGateState state, long now) => state.EndedAt is not { } endedAt || endedAt <= now;

    /// <summary>PC の門に書く「この刻みまでは来るな」。自分が言われた分と、ほかのアプリが言われた分の遅い方。</summary>
    private long? LaterQuiet(BoothGateState read, long now)
    {
        var others = IsFromThisBoot(read, now) ? read.QuietUntil : null;
        var later = others is { } theirs && (_quietUntil is not { } mine || theirs > mine) ? others : _quietUntil;
        return later > now ? later : null;
    }

    /// <summary>
    /// 最後に問い合わせたのが自分で、ほかのアプリが順番を待っているときに、間隔に足して譲る間。
    ///
    /// 足さないと、間隔が同じ2本は同じ刻みに起きて早い方が握り、裏の作業を続けているアプリが続けて握り得る。
    /// 足すと、2本が動いている間は1本ずつ交互になり、もう1本の人が押した問い合わせは、こちらの1本の後に必ず出られる。
    /// 長さは、相手の起き遅れを覆う分：待ちは 0.2 秒刻みで数えるので（<see cref="CountDownAsync"/>）、
    /// 1.5 秒で8刻み・1刻みごとにタイマーの粗さ（約16ms）まで遅れて最大 0.13 秒。握り直しの間合い 50ms を足して 0.25 秒。
    /// **待っている相手が居るときだけ足す**ので、1本しか開いていないときは遅くならない。
    /// </summary>
    private static readonly TimeSpan YieldGrace = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// 送ったまま落ちたアプリの1本が、相手の側で終わるまでに見ておく長さ。
    ///
    /// 落ちたアプリの問い合わせは、相手の側ではまだ続いていることがある。「今終わった」とみなして間隔だけ空けると、
    /// 相手から見た間は応答にかかった分だけ短くなる（2つのプロセスで測ると、0.2 秒かかる応答の途中で落としたとき 1424 ms だった。
    /// 2026-09-30・<c>experiments/BoothGateProbe</c>）。応答は手元の実測で 0.16〜0.3 秒なので、遅いときの分を見て 2 秒
    /// （一時エラーの後に最初に待ち直す長さと同じ）。落ちるのは稀で、長めに取っても普段は効かない。
    /// </summary>
    private static readonly TimeSpan OrphanAllowance = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 今からどれだけ待てば出られるか。自分の最後の問い合わせ・相手に指示された待ち・PC の門の中身の、いちばん遅い物。
    /// </summary>
    /// <returns>待つ長さと、それが相手に指示された待ちか。</returns>
    private (TimeSpan Wait, bool Quiet) WaitNeeded(long now, BoothGateState? shared)
    {
        var interval = SyncedIntervalMs();
        var intervalEnd = _lastRequestAt is { } last ? After(last, interval) : now;
        var quietUntil = _quietUntil ?? now;

        if (shared is not null && IsFromThisBoot(shared, now))
        {
            // 送ったまま落ちたアプリが居た（か、中身が読めなかった）。いつ終わるかが分からないので、今から少し後に終わるとみなす
            if ((shared.InFlight ? After(now, OrphanAllowance) : shared.EndedAt) is { } endedAt)
            {
                // **間隔が違えば長い方に合わせる**（最後に問い合わせた側・待っている側・自分）。
                // 最後に問い合わせた側が 429 で広げていれば、その分も付き合う。
                // 待っている側の間隔も見ないと、間隔の短いアプリが出続けて、長いアプリが永久に出られない。
                // ほかのアプリの値は自分の設定の上限で頭を打つ（中身が壊れていても、長く止まらない）
                var waiting = shared.Waiter is not null && shared.Waiter != _id;
                var others = Math.Max(shared.IntervalMs, waiting ? shared.WaiterIntervalMs : 0);
                var effective = Math.Max(interval, Math.Min(others, Math.Max(interval, _settings.FetchIntervalMaxMs)));
                var end = After(endedAt, effective);
                if (waiting && shared.Sender == _id)
                {
                    end = After(end, YieldGrace);
                }

                intervalEnd = Math.Max(intervalEnd, end);
            }

            if (shared.QuietUntil is { } theirs)
            {
                quietUntil = Math.Max(quietUntil, Math.Min(theirs, After(now, MaxRetryAfterWait)));
            }
        }

        var quiet = quietUntil > intervalEnd;
        var until = quiet ? quietUntil : intervalEnd;
        return (until > now ? _clock.GetElapsedTime(now, until) : TimeSpan.Zero, quiet);
    }

    /// <summary>
    /// 前回のリクエストから現在の間隔が空き、相手に指示された待ちも明けるまで待ち、PC の門を握って返す。
    /// **このクライアントのゲートは持ったまま待つ**ので、その間はこのアプリの他の問い合わせも出ない。中断はそのまま効く。
    ///
    /// **PC の門は、待っている間は放す。**握るのは中身を読む一瞬と、出られると決まってから問い合わせが終わるまで。
    /// 待ち終えたら握り直して中身を読み、待つ前と同じなら出る。変わっていたら（その間にほかのアプリが問い合わせた・
    /// 順番待ちに並んだ）待つ長さを出し直す。
    ///
    /// 待ち終えた後に時計を見直さないのは前からで、待ち（<see cref="_delay"/>）を信じる。見直すと、
    /// 待ちを差し替えた試験が本物の時計が進むまで回り続ける。
    /// </summary>
    /// <returns>握った PC の門。門に入っていない・門が使えないときは null。</returns>
    private async Task<BoothMachineGate.Turn?> WaitForTurnAsync(string? target, int attempt, CancellationToken cancellationToken)
    {
        var waited = false;
        BoothGateState? waitedOn = null;

        while (true)
        {
            var turn = _machineGate is null ? null : await _machineGate.EnterAsync(cancellationToken);
            TimeSpan wait;
            bool quiet;
            long startedWaitingAt;
            var passed = false;
            try
            {
                var state = turn?.State;
                if (waited && state == waitedOn)
                {
                    passed = true;
                    return turn;
                }

                startedWaitingAt = _clock.GetTimestamp();
                (wait, quiet) = WaitNeeded(startedWaitingAt, state);
                if (wait <= TimeSpan.Zero)
                {
                    passed = true;
                    return turn;
                }

                // 最後に問い合わせたのが別のアプリなら、順番待ちに並ぶ（相手はこれを見て1回譲り、間隔もこちらに合わせる）。
                // 自分が最後なら並ばない——譲ってもらう相手が居ない
                var interval = SyncedIntervalMs();
                if (turn is not null && state is not null && state.Sender != _id
                    && (state.Waiter != _id || state.WaiterIntervalMs != interval))
                {
                    state = state with { Waiter = _id, WaiterIntervalMs = interval };
                    turn.Write(state);
                }

                waitedOn = state;
                waited = true;
            }
            finally
            {
                if (!passed)
                {
                    turn?.Dispose();
                }
            }

            // 指示された待ちのうち、待てと言われた本人の再試行は「再試行まで待っている」と出す。
            // 巻き添えで待っている他の問い合わせは、再試行ではないので「混み合っているので間隔を広げています」側に出す
            var retrying = quiet && attempt > 0;
            await CountDownAsync(
                wait,
                remaining => new BoothActivity
                {
                    Kind = retrying ? BoothActivityKind.Retrying : BoothActivityKind.Waiting,
                    Target = target,
                    Total = wait,
                    Remaining = remaining,
                    Attempt = retrying ? attempt : 0,
                    MaxAttempts = retrying ? RetryDelays.Length : 0,
                    IsThrottled = quiet || IsThrottled,
                },
                cancellationToken);

            // **タイマーは、頼んだ長さより少し早く鳴ることがある**（OS の刻み約16msの粗さで数えるので、待ち始めが刻みの途中だと
            // その分だけ短い）。2つのプロセスで測ると、間が 1500 ms をわずかに切る回があった（2026-09-30・BoothGateProbe）。
            // 足りなければ、その分と刻み1つ分を待ち足す。
            // 大きく足りないときは待ち足さない——待ちを差し替えた試験（時計が進まない）で、前からどおり待ちを信じる
            var shortBy = wait - _clock.GetElapsedTime(startedWaitingAt, _clock.GetTimestamp());
            if (shortBy > TimeSpan.Zero && shortBy <= TimerSlack)
            {
                await _delay(shortBy + TimerTick, cancellationToken);
            }
        }
    }

    /// <summary>OS のタイマーの刻み（約 15.6 ms）を切り上げた長さ。</summary>
    private static readonly TimeSpan TimerTick = TimeSpan.FromMilliseconds(16);

    /// <summary>
    /// 「タイマーが早く鳴った」とみなす足りなさの上限。早く鳴るのは待ちの刻み1回につき OS の刻み1つ分までで、
    /// 重なっても数回分。これより大きく足りないのは、待ちを差し替えた試験
    /// </summary>
    private static readonly TimeSpan TimerSlack = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// 待ちながら残り時間を知らせる。
    ///
    /// 一気に待つと「止まって見える」時間になる。長さは分かっているので、
    /// 刻んで残りを出せば、待っていることと、あとどれだけかが伝わる。
    /// 刻みは0.2秒。これ以上細かくしても読めないし、粗いと数字が飛ぶ。
    /// </summary>
    private async Task CountDownAsync(
        TimeSpan total,
        Func<TimeSpan, BoothActivity> describe,
        CancellationToken cancellationToken)
    {
        var tick = TimeSpan.FromMilliseconds(200);
        var remaining = total;

        while (remaining > TimeSpan.Zero)
        {
            Report(describe(remaining));

            var step = remaining < tick ? remaining : tick;
            await _delay(step, cancellationToken);
            remaining -= step;
        }
    }

    private void Report(BoothActivity activity) => ActivityChanged?.Invoke(activity);

    public event Action<BoothActivity>? ActivityChanged;

    private TimeSpan MaxRetryAfterWait => TimeSpan.FromSeconds(_settings.MaxRetryAfterWaitSeconds);

    /// <summary>429を受けるたびに間隔を倍にする。上限に達したらそこで止める。呼び出しは必ずゲート内。</summary>
    private void SlowDown()
    {
        var doubled = Math.Min((long)SyncedIntervalMs() * 2, _settings.FetchIntervalMaxMs);
        _currentIntervalMs = (int)Math.Max(doubled, ConfiguredIntervalMs);
        _successesSinceSlowDown = 0;
    }

    /// <summary>
    /// 広げた間隔を戻すまでの、続けて成功した回数（ユーザ判断 2026-09-21・C16）。
    ///
    /// **戻す道が無く、一度広がるとプロセスを終えるまでそのままだった。**
    /// 相手が「待て」と言った直後に戻すのは筋が通らないので、
    /// **こちらが広げた間隔で十分に間を置き、成功し続けたこと**を条件にする。
    /// 20回＝広げた間隔（最短3秒）で1分前後。それだけ何事も無ければ、詰まっていた側は空いている。
    /// </summary>
    private const int SuccessesBeforeEasing = 20;

    private int _successesSinceSlowDown;

    /// <summary>
    /// 成功が続いたら、広げた分を半分ずつ戻す（下限は設定の間隔）。
    /// 一気に戻さないのは、戻した先でまた429を受けると往復するため。
    /// </summary>
    private void EaseBack()
    {
        if (_currentIntervalMs <= ConfiguredIntervalMs || ++_successesSinceSlowDown < SuccessesBeforeEasing)
        {
            return;
        }

        _successesSinceSlowDown = 0;
        _currentIntervalMs = Math.Max(_currentIntervalMs / 2, ConfiguredIntervalMs);
    }

    /// <summary>Retry-Afterを読む。秒数形式とHTTP日付形式の両方が来る。</summary>
    private TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null)
        {
            return null;
        }

        if (header.Delta is { } delta)
        {
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        }

        if (header.Date is { } date)
        {
            var wait = date - _clock.GetUtcNow();
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }
}
