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
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) BoothAssetManager/0.1 (personal library manager)";

    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8)];

    private readonly HttpClient _httpClient;
    private readonly AppSettings _settings;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;
    private int _currentIntervalMs;

    /// <param name="delay">待機処理。テストでは実際に待たせないよう差し替える。</param>
    public BoothClient(
        HttpClient httpClient,
        AppSettings? settings = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _httpClient = httpClient;
        _settings = settings ?? new AppSettings();
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
        _currentIntervalMs = _settings.FetchIntervalMs;

        if (!_httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd(UserAgent))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        }
    }

    public int CurrentIntervalMs => _currentIntervalMs;

    public bool IsThrottled => _currentIntervalMs > _settings.FetchIntervalMs;

    public static string ItemJsonUrl(string itemId) => $"https://booth.pm/ja/items/{itemId}.json";

    public static string ItemPageUrl(string itemId) => $"https://booth.pm/ja/items/{itemId}";

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
                // 相手がRetry-Afterで待ち時間を指示してきたら、こちらの既定より長い限りそちらに従う。
                var wait = RetryDelays[attempt - 1];
                if (previous?.RetryAfter is { } instructed && instructed > wait)
                {
                    wait = instructed;
                }

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

            var result = await SendOnceAsync(url, readBody, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var target = DescribeTarget(url);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await WaitForIntervalAsync(target, cancellationToken);

            Report(new BoothActivity
            {
                Kind = BoothActivityKind.Sending,
                Target = target,
                IsThrottled = IsThrottled,
            });

            // ヘッダだけ先に受け取る。本文を途中で打ち切る呼び出し（ショップのバナー探し）が
            // 実際に通信を止められるようにするため。全部読む呼び出しの動きは変わらない。
            using var response = await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            _lastRequestAt = DateTimeOffset.UtcNow;

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return BoothFetchResult<T>.NotFound();
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = ReadRetryAfter(response);
                SlowDown();
                return BoothFetchResult<T>.RateLimited(
                    retryAfter is { } wait
                        ? $"HTTP 429（{wait.TotalSeconds:0}秒待つよう指示されました。以降の間隔を{_currentIntervalMs}msに広げます）"
                        : $"HTTP 429（以降の間隔を{_currentIntervalMs}msに広げます）",
                    retryAfter);
            }

            if (!response.IsSuccessStatusCode)
            {
                // 503などもRetry-Afterを付けてくることがあるので、あれば従う。
                return BoothFetchResult<T>.Temporary($"HTTP {(int)response.StatusCode}", ReadRetryAfter(response));
            }

            return BoothFetchResult<T>.Success(await readBody(response));
        }
        catch (HttpRequestException exception)
        {
            _lastRequestAt = DateTimeOffset.UtcNow;
            return BoothFetchResult<T>.Temporary(exception.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _lastRequestAt = DateTimeOffset.UtcNow;
            return BoothFetchResult<T>.Temporary("タイムアウトしました");
        }
        finally
        {
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

    /// <summary>前回のリクエストから現在の間隔が空くまで待つ。</summary>
    private async Task WaitForIntervalAsync(string? target, CancellationToken cancellationToken)
    {
        if (_lastRequestAt == DateTimeOffset.MinValue)
        {
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - _lastRequestAt;
        var interval = TimeSpan.FromMilliseconds(_currentIntervalMs);
        if (elapsed >= interval)
        {
            return;
        }

        await CountDownAsync(
            interval - elapsed,
            remaining => new BoothActivity
            {
                Kind = BoothActivityKind.Waiting,
                Target = target,
                Total = interval - elapsed,
                Remaining = remaining,
                IsThrottled = IsThrottled,
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
        var doubled = Math.Min((long)_currentIntervalMs * 2, _settings.FetchIntervalMaxMs);
        _currentIntervalMs = (int)Math.Max(doubled, _settings.FetchIntervalMs);
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
