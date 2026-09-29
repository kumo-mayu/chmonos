using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BoothAssetManager.Core.Booth;

namespace ResolveAccuracyProbe;

/// <summary>
/// BOOTH の検索と商品JSONの答えを、検索語・商品IDごとに手元へ控える。2回目からは控えを返し、通信しない。
///
/// 並べ直し・点数の直しは検索の答えさえあれば試せる。直すたびに数百本を問い合わせ直すと、
/// 相手に同じ負担を何度もかけることになる（CLAUDE.md の決め事1）。問い合わせるのは、検索語を変えて
/// 控えに無い語が出たときだけにする。
///
/// 控えに無いときだけ本物の <see cref="BoothClient"/> を通す（1本ずつ・1.5秒以上空く）。
/// 本物は初めて要るまで作らない——控えだけで済む測定では通信の口そのものを開かない。
/// 404 は控える（消えた商品を毎回聞きに行かない）。一時的な失敗は控えない（次に聞き直せば取れる）。
/// </summary>
sealed class CachingBoothClient : IBoothClient
{
    private readonly string _directory;
    private readonly bool _offline;
    private BoothClient? _inner;
    private HttpClient? _http;

    public CachingBoothClient(string directory, bool offline)
    {
        _directory = directory;
        _offline = offline;
        Directory.CreateDirectory(Path.Combine(directory, "search"));
        Directory.CreateDirectory(Path.Combine(directory, "json"));
    }

    /// <summary>実際に BOOTH へ出た問い合わせの本数。</summary>
    public int NetworkRequests { get; private set; }

    public int CacheHits { get; private set; }

    /// <summary>控えに無く、通信もしなかった（--offline）本数。</summary>
    public int Misses { get; private set; }

    public int CurrentIntervalMs => _inner?.CurrentIntervalMs ?? 0;

    public bool IsThrottled => _inner?.IsThrottled ?? false;

    public event Action<BoothActivity>? ActivityChanged
    {
        add { }
        remove { }
    }

    public Task<BoothFetchResult<string>> SearchAsync(string query, CancellationToken cancellationToken = default)
        => CachedAsync("search", query, inner => inner.SearchAsync(query, cancellationToken));

    public Task<BoothFetchResult<string>> GetItemJsonAsync(string itemId, CancellationToken cancellationToken = default)
        => CachedAsync("json", itemId, inner => inner.GetItemJsonAsync(itemId, cancellationToken));

    public Task<BoothFetchResult<string>> GetItemHtmlAsync(string itemId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("自動検索は商品ページを取らない");

    public Task<BoothFetchResult<byte[]>> GetBinaryAsync(string url, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("自動検索は画像を取らない");

    public Task<BoothFetchResult<string>> GetTextUntilAsync(
        string url, Func<string, bool> found, int maxBytes = 262144, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("自動検索はこの取得を使わない");

    private async Task<BoothFetchResult<string>> CachedAsync(
        string kind, string key, Func<BoothClient, Task<BoothFetchResult<string>>> fetch)
    {
        var path = Path.Combine(_directory, kind, Hash(key) + ".json");
        if (File.Exists(path))
        {
            var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(path, Encoding.UTF8))!;
            CacheHits++;
            return entry.Status == BoothFetchStatus.NotFound
                ? BoothFetchResult<string>.NotFound()
                : BoothFetchResult<string>.Success(entry.Body ?? string.Empty);
        }

        if (_offline)
        {
            Misses++;
            return BoothFetchResult<string>.Temporary("控えに無い（--offline）");
        }

        _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _inner ??= new BoothClient(_http, new BoothAssetManager.Core.Models.AppSettings());

        NetworkRequests++;
        var result = await fetch(_inner);
        if (result.IsSuccess || result.Status == BoothFetchStatus.NotFound)
        {
            var entry = new Entry { Kind = kind, Key = key, Status = result.Status, Body = result.Value };
            File.WriteAllText(path, JsonSerializer.Serialize(entry), new UTF8Encoding(false));
        }

        return result;
    }

    private static string Hash(string key)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24].ToLowerInvariant();

    private sealed class Entry
    {
        public required string Kind { get; init; }

        public required string Key { get; init; }

        public BoothFetchStatus Status { get; init; }

        public string? Body { get; init; }
    }
}
