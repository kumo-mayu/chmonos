using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// ショップ一覧が開いたときに取るアイコン（点検 2026-09-29・8）。
///
/// 届かない失敗が続いても全店を回り、取れなかった店にも「確かめた」を書いて30日休ませていた。
/// **届かない失敗が3件続いたら残りは取りに行かない。一時的に取れなかった店には「確かめた」を書かない。**
/// 404（BOOTH が無いと答えた）は今までどおり書く。
/// 通信はしない（作り物の答え）。
/// </summary>
public class ShopIconSyncTests : IDisposable
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly string _root;
    private readonly DataStore _store;
    private readonly ScriptedClient _client = new();
    private readonly ShopService _shops;
    private readonly ImagePipeline _images;

    public ShopIconSyncTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-shop-icon-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);

        var settings = new AppSettings { SaveImages = true, ShopBannerRecheckDays = 30 };
        _images = new ImagePipeline(_client, paths, settings);
        _shops = new ShopService(_store, settings, _client);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private enum Answer
    {
        Ok,
        Missing,
        Unreachable,
        ServerDown,
        Busy,
        Broken,
    }

    /// <summary>アイコンの URL に含まれる店の名前ごとに答えを決める。問い合わせた順も控える。</summary>
    private sealed class ScriptedClient : IBoothClient
    {
        public Dictionary<string, Answer> Answers { get; } = [];

        public List<string> Asked { get; } = [];

        public Task<BoothFetchResult<byte[]>> GetBinaryAsync(string url, CancellationToken cancellationToken = default)
        {
            var shop = Answers.Keys.First(key => url.Contains($"/{key}.", StringComparison.Ordinal));
            Asked.Add(shop);

            return Task.FromResult(Answers[shop] switch
            {
                Answer.Ok => BoothFetchResult<byte[]>.Success(TinyPng),
                Answer.Missing => BoothFetchResult<byte[]>.NotFound(),
                Answer.Unreachable => BoothFetchResult<byte[]>.Unreachable("つながらない"),
                Answer.ServerDown => new BoothFetchResult<byte[]>
                {
                    Status = BoothFetchStatus.TemporaryFailure,
                    Error = "HTTP 503",
                    IsServerError = true,
                },
                Answer.Busy => BoothFetchResult<byte[]>.RateLimited("HTTP 429", null),
                _ => BoothFetchResult<byte[]>.Success([1, 2, 3]),
            });
        }

        public Task<BoothFetchResult<string>> GetItemJsonAsync(string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BoothFetchResult<string>> GetItemHtmlAsync(string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BoothFetchResult<string>> SearchAsync(string query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BoothFetchResult<string>> GetTextUntilAsync(
            string url, Func<string, bool> found, int maxBytes = 262144, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public int CurrentIntervalMs => 0;

        public bool IsThrottled => false;

#pragma warning disable CS0067
        public event Action<BoothActivity>? ActivityChanged;
#pragma warning restore CS0067
    }

    private ShopSummary Shop(string subdomain, Answer answer)
    {
        _client.Answers[subdomain] = answer;
        return new ShopSummary
        {
            Subdomain = subdomain,
            Name = subdomain,
            ThumbnailUrl = $"https://booth.pximg.net/c/48x48/users/1/icon_image/{subdomain}.png",
            KnownCount = 1,
            OwnedCount = 1,
            SpentYen = 0,
        };
    }

    private DateTimeOffset? IconCheckedAt(string subdomain)
        => _store.ShopBanners.Load()
            .FirstOrDefault(record => string.Equals(record.Subdomain, subdomain, StringComparison.OrdinalIgnoreCase))
            ?.IconCheckedAt;

    [Fact]
    public async Task StopsAfterThreeUnreachableShopsInARow()
    {
        var shops = new[]
        {
            Shop("a", Answer.Unreachable),
            Shop("b", Answer.ServerDown),
            Shop("c", Answer.Unreachable),
            Shop("d", Answer.Ok),
            Shop("e", Answer.Ok),
        };

        Assert.Equal(0, await _shops.SyncIconsAsync(shops, _images));

        Assert.Equal(["a", "b", "c"], _client.Asked);
    }

    /// <summary>間に BOOTH が答えた店があれば数え直す。1店だけ届かないのでは止めない</summary>
    [Fact]
    public async Task KeepsGoingWhenBoothAnswersInBetween()
    {
        var shops = new[]
        {
            Shop("a", Answer.Unreachable),
            Shop("b", Answer.Unreachable),
            Shop("c", Answer.Missing),
            Shop("d", Answer.Unreachable),
            Shop("e", Answer.Ok),
        };

        Assert.Equal(1, await _shops.SyncIconsAsync(shops, _images));

        Assert.Equal(["a", "b", "c", "d", "e"], _client.Asked);
    }

    /// <summary>
    /// 一時的に取れなかった店には「確かめた」を書かない。書くと、つながった後も確かめ直す日まで取りに行かない。
    /// BOOTH が答えた店（取れた・404・読めない絵）には書く
    /// </summary>
    [Fact]
    public async Task NotesOnlyTheShopsBoothAnswered()
    {
        var shops = new[]
        {
            Shop("ok", Answer.Ok),
            Shop("gone", Answer.Missing),
            Shop("broken", Answer.Broken),
            Shop("offline", Answer.Unreachable),
            Shop("busy", Answer.Busy),
            Shop("down", Answer.ServerDown),
        };

        await _shops.SyncIconsAsync(shops, _images);

        Assert.NotNull(IconCheckedAt("ok"));
        Assert.NotNull(IconCheckedAt("gone"));
        Assert.NotNull(IconCheckedAt("broken"));
        Assert.Null(IconCheckedAt("offline"));
        Assert.Null(IconCheckedAt("busy"));
        Assert.Null(IconCheckedAt("down"));

        // 一時的に取れなかった店は、次に開いたときにまた取りに行く。404 の店は休む
        var again = _shops.ShopsNeedingIcons(shops.Select(shop => shop with { IconPath = null })).Select(shop => shop.Subdomain);
        Assert.Equal(["offline", "busy", "down"], again);
    }

    /// <summary>打ち切った後の店も、問い合わせていないので「確かめた」を書かない</summary>
    [Fact]
    public async Task DoesNotNoteShopsLeftAfterStopping()
    {
        var shops = new[]
        {
            Shop("a", Answer.Unreachable),
            Shop("b", Answer.Unreachable),
            Shop("c", Answer.Unreachable),
            Shop("d", Answer.Ok),
        };

        await _shops.SyncIconsAsync(shops, _images);

        Assert.All(["a", "b", "c", "d"], subdomain => Assert.Null(IconCheckedAt(subdomain)));
    }
}
