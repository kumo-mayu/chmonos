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

    public bool IsSuccess => Status == BoothFetchStatus.Success;

    public static BoothFetchResult<T> Success(T value) => new() { Status = BoothFetchStatus.Success, Value = value };

    public static BoothFetchResult<T> NotFound() => new() { Status = BoothFetchStatus.NotFound };

    public static BoothFetchResult<T> Temporary(string error)
        => new() { Status = BoothFetchStatus.TemporaryFailure, Error = error };
}

public interface IBoothClient
{
    Task<BoothFetchResult<string>> GetItemJsonAsync(string itemId, CancellationToken cancellationToken = default);

    Task<BoothFetchResult<string>> GetItemHtmlAsync(string itemId, CancellationToken cancellationToken = default);

    Task<BoothFetchResult<byte[]>> GetBinaryAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>BOOTH内検索。手掛かりが無いファイルの候補を出すために使う。</summary>
    Task<BoothFetchResult<string>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

/// <summary>
/// BOOTHへの取得。サーバに負荷をかけないよう、リクエストは必ず直列で、1件ごとに間隔を空ける。
/// 並列化はしない（pixiv共通規約の「短時間の機械的な大量操作」を避けるため）。
///
/// 失敗の扱いは2種類に分ける。404だけが非公開判定のカウント対象で、
/// タイムアウトや5xxは一時エラーとして再試行し、カウントには数えない。
/// BOOTH側の一時的な障害で商品が「非公開」と誤判定されるのを防ぐため。
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

    /// <param name="delay">待機処理。テストでは実際に待たせないよう差し替える。</param>
    public BoothClient(
        HttpClient httpClient,
        AppSettings? settings = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _httpClient = httpClient;
        _settings = settings ?? new AppSettings();
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));

        if (!_httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd(UserAgent))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        }
    }

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

    private Task<BoothFetchResult<string>> GetStringAsync(string url, CancellationToken cancellationToken)
        => SendWithRetryAsync(url, response => response.Content.ReadAsStringAsync(cancellationToken), cancellationToken);

    private async Task<BoothFetchResult<T>> SendWithRetryAsync<T>(
        string url,
        Func<HttpResponseMessage, Task<T>> readBody,
        CancellationToken cancellationToken)
    {
        string lastError = "不明なエラー";

        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            if (attempt > 0)
            {
                await _delay(RetryDelays[attempt - 1], cancellationToken);
            }

            var result = await SendOnceAsync(url, readBody, cancellationToken);
            if (result.Status != BoothFetchStatus.TemporaryFailure)
            {
                return result;
            }

            lastError = result.Error ?? lastError;
        }

        return BoothFetchResult<T>.Temporary(lastError);
    }

    private async Task<BoothFetchResult<T>> SendOnceAsync<T>(
        string url,
        Func<HttpResponseMessage, Task<T>> readBody,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await WaitForIntervalAsync(cancellationToken);

            using var response = await _httpClient.GetAsync(url, cancellationToken);
            _lastRequestAt = DateTimeOffset.UtcNow;

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return BoothFetchResult<T>.NotFound();
            }

            if (!response.IsSuccessStatusCode)
            {
                return BoothFetchResult<T>.Temporary($"HTTP {(int)response.StatusCode}");
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
        }
    }

    /// <summary>前回のリクエストから設定の間隔が空くまで待つ。</summary>
    private async Task WaitForIntervalAsync(CancellationToken cancellationToken)
    {
        if (_lastRequestAt == DateTimeOffset.MinValue)
        {
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - _lastRequestAt;
        var interval = TimeSpan.FromMilliseconds(_settings.FetchIntervalMs);
        if (elapsed < interval)
        {
            await _delay(interval - elapsed, cancellationToken);
        }
    }
}
