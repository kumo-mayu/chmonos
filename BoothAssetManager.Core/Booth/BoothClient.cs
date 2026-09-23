using System.Text;
using System.Net;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Booth;

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

    public bool IsSuccess => Status == BoothFetchStatus.Success;

    public static BoothFetchResult<T> Success(T value) => new() { Status = BoothFetchStatus.Success, Value = value };

    public static BoothFetchResult<T> NotFound() => new() { Status = BoothFetchStatus.NotFound };

    public static BoothFetchResult<T> Temporary(string error, TimeSpan? retryAfter = null)
        => new() { Status = BoothFetchStatus.TemporaryFailure, Error = error, RetryAfter = retryAfter };

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

    private readonly HttpClient _httpClient;
    private readonly Func<AppSettings> _currentSettings;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly PriorityGate _gate = new();
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;
    private int _currentIntervalMs;

    /// <summary><see cref="_currentIntervalMs"/> を決めたときの、設定の間隔。</summary>
    private int _intervalBaseMs;

    /// <param name="delay">待機処理。テストでは実際に待たせないよう差し替える。</param>
    public BoothClient(
        HttpClient httpClient,
        AppSettings? settings = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
        : this(httpClient, SettingsSource.Fixed(settings), delay)
    {
    }

    /// <param name="currentSettings">
    /// 使うたびに今の設定を返すもの（<see cref="SettingsSource"/>）。取得の間隔も保存した直後から効く。
    /// </param>
    /// <param name="delay">待機処理。テストでは実際に待たせないよう差し替える。</param>
    public BoothClient(
        HttpClient httpClient,
        Func<AppSettings> currentSettings,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _httpClient = httpClient;
        _currentSettings = currentSettings;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
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

    public Task<BoothFetchResult<string>> SearchAsync(string query, CancellationToken cancellationToken = default)
        => GetStringAsync(SearchUrl(query), cancellationToken);

    public Task<BoothFetchResult<string>> GetItemJsonAsync(string itemId, CancellationToken cancellationToken = default)
        => GetStringAsync(ItemJsonUrl(itemId), cancellationToken);

    public Task<BoothFetchResult<string>> GetItemHtmlAsync(string itemId, CancellationToken cancellationToken = default)
        => GetStringAsync(ItemPageUrl(itemId), cancellationToken);

    public Task<BoothFetchResult<byte[]>> GetBinaryAsync(string url, CancellationToken cancellationToken = default)
        => SendWithRetryAsync(url, response => response.Content.ReadAsByteArrayAsync(cancellationToken), cancellationToken);

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
            response => ReadUntilAsync(response, found, maxBytes, cancellationToken),
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
        => SendWithRetryAsync(url, response => response.Content.ReadAsStringAsync(cancellationToken), cancellationToken);

    private async Task<BoothFetchResult<T>> SendWithRetryAsync<T>(
        string url,
        Func<HttpResponseMessage, Task<T>> readBody,
        CancellationToken cancellationToken)
    {
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

            var result = await SendOnceAsync(url, readBody, attempt, cancellationToken);
            if (result.Status != BoothFetchStatus.TemporaryFailure)
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
        Func<HttpResponseMessage, Task<T>> readBody,
        int attempt,
        CancellationToken cancellationToken)
    {
        var target = DescribeTarget(url);

        await _gate.EnterAsync(CurrentPriority, cancellationToken);

        // 送り出したか。**送った後は、どう抜けても最後の問い合わせの時刻を更新する**（finally）。
        // 前は成功・HTTPの失敗・タイムアウトの道でしか更新しておらず、送った後の中断
        // （OperationCanceledException）や想定外の例外で抜けると古い時刻のまま残り、
        // 次の1本が 1.5 秒を空けずに出得た（絶対に破らない決め事1）
        var sent = false;
        try
        {
            await WaitForIntervalAsync(target, attempt, cancellationToken);

            Report(new BoothActivity
            {
                Kind = BoothActivityKind.Sending,
                Target = target,
                IsThrottled = IsThrottled,
            });

            // 送る直前に立てる。GetAsync の中で投げても、相手に届いているかは分からないので
            // 「届いた」側に倒す（間を空けすぎても困る人はいない）
            sent = true;

            // ヘッダだけ先に受け取る。本文を途中で打ち切る呼び出し（ショップのバナー探し）が
            // 実際に通信を止められるようにするため。全部読む呼び出しの動きは変わらない。
            using var response = await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return BoothFetchResult<T>.NotFound();
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
                        ? $"HTTP 429（{wait.TotalSeconds:0}秒待つよう指示されました。以降の間隔を{_currentIntervalMs}msに広げます）"
                        : $"HTTP 429（以降の間隔を{_currentIntervalMs}msに広げます）",
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

                return BoothFetchResult<T>.Temporary($"HTTP {(int)response.StatusCode}", retryAfter);
            }

            // 広げた間隔は、成功が続いたら少しずつ戻す（C16）
            EaseBack();
            return BoothFetchResult<T>.Success(await readBody(response));
        }
        catch (HttpRequestException exception)
        {
            // 理由は画面や取り込みの結果にそのまま出る。.NET の文（英語・内部の名前）は出さず、中身はログへ
            Diagnostics.AppLog.Error("BOOTHへの問い合わせ", exception);
            return BoothFetchResult<T>.Temporary(Services.FailureText.Cause(exception));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return BoothFetchResult<T>.Temporary("タイムアウトしました");
        }
        finally
        {
            if (sent)
            {
                _lastRequestAt = DateTimeOffset.UtcNow;
            }

            _gate.Release();

            // 直列なので、ゲートを出た時点で「何もしていない」に戻せる。
            // 次の1本がすぐ入るならそちらが上書きする
            Report(BoothActivity.Idle);
        }
    }

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
    private DateTimeOffset _quietUntil = DateTimeOffset.MinValue;

    private void HoldQuiet(TimeSpan instructed)
    {
        var wait = instructed > MaxRetryAfterWait ? MaxRetryAfterWait : instructed;
        var until = DateTimeOffset.UtcNow + wait;
        if (until > _quietUntil)
        {
            _quietUntil = until;
        }
    }

    /// <summary>
    /// 前回のリクエストから現在の間隔が空き、相手に指示された待ちも明けるまで待つ。
    /// **ゲートを持ったまま待つ**ので、その間は他の問い合わせも出ない。中断はそのまま効く。
    /// </summary>
    private async Task WaitForIntervalAsync(string? target, int attempt, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var intervalEnd = _lastRequestAt == DateTimeOffset.MinValue
            ? now
            : _lastRequestAt + TimeSpan.FromMilliseconds(SyncedIntervalMs());
        var quiet = _quietUntil > intervalEnd;
        var wait = (quiet ? _quietUntil : intervalEnd) - now;
        if (wait <= TimeSpan.Zero)
        {
            return;
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
    }

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
    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
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
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }
}
