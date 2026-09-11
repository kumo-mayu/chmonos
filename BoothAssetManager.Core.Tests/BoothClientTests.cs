using System.Net;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Models;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class BoothClientTests
{
    /// <summary>用意した応答を順に返すだけのハンドラ。実際の通信はしない。</summary>
    private sealed class QueuedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public QueuedHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(_responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            throw new HttpRequestException("接続できません");
        }
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    /// <summary>429応答。<paramref name="retryAfter"/> が null ならヘッダを付けない。</summary>
    private static HttpResponseMessage TooManyRequests(TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        if (retryAfter is { } wait)
        {
            response.Headers.Add("Retry-After", ((int)wait.TotalSeconds).ToString());
        }

        return response;
    }

    private static BoothClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler),
            new AppSettings { FetchIntervalMs = 0 },
            delay: (_, _) => Task.CompletedTask);

    /// <summary>
    /// ブラウザを名乗らず、アプリ名で名乗る（#46）。
    /// ブラウザの顔をした自動の通信は、セキュリティソフトにスクレイパーと見られ得る。
    /// </summary>
    [Fact]
    public void IntroducesItselfByAppNameNotAsBrowser()
    {
        using var http = new HttpClient(new QueuedHandler());
        _ = new BoothClient(http, new AppSettings { FetchIntervalMs = 0 });

        var sent = http.DefaultRequestHeaders.UserAgent.ToString();

        Assert.StartsWith("BoothAssetManager/", sent);
        Assert.DoesNotContain("Mozilla", sent);
    }

    /// <summary>
    /// 自動減速を見るためのクライアント。間隔は0にできない（倍にしても0のままなので）。
    /// 実際には待たせず、<paramref name="waits"/> に指示された待ち時間だけ記録する。
    /// </summary>
    private static BoothClient CreateThrottleClient(
        HttpMessageHandler handler,
        List<TimeSpan> waits,
        AppSettings? settings = null)
        => new(new HttpClient(handler),
            settings ?? new AppSettings { FetchIntervalMs = 1000, FetchIntervalMaxMs = 8000 },
            delay: (duration, _) =>
            {
                waits.Add(duration);
                return Task.CompletedTask;
            });

    /// <summary>
    /// 再試行までの待ちを、ユーザに知らせた内容から読む。
    ///
    /// 待ちは残り時間を刻んで知らせるので、内部の待ち呼び出し1回ぶんを見ても長さが分からない。
    /// 「何秒待つと伝えたか」で確かめる方が、意図にも合っている。
    /// </summary>
    private static List<TimeSpan> RetryWaitsOf(BoothClient client)
    {
        var waits = new List<TimeSpan>();

        client.ActivityChanged += activity =>
        {
            if (activity.Kind == BoothActivityKind.Retrying && activity.Total is { } total && !waits.Contains(total))
            {
                waits.Add(total);
            }
        };

        return waits;
    }

    [Fact]
    public async Task ReturnsBodyOnSuccess()
    {
        var handler = new QueuedHandler(Ok("""{"id":123}"""));

        var result = await CreateClient(handler).GetItemJsonAsync("123");

        Assert.True(result.IsSuccess);
        Assert.Equal("""{"id":123}""", result.Value);
        Assert.Equal(1, handler.RequestCount);
    }

    /// <summary>404は非公開判定のカウント対象なので、再試行せずそのまま返す。</summary>
    [Fact]
    public async Task ReturnsNotFoundWithoutRetrying()
    {
        var handler = new QueuedHandler(new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await CreateClient(handler).GetItemJsonAsync("123");

        Assert.Equal(BoothFetchStatus.NotFound, result.Status);
        Assert.Equal(1, handler.RequestCount);
    }

    /// <summary>5xxは一時エラー。同一実行内で最大2回まで再試行する（合計3回）。</summary>
    [Fact]
    public async Task RetriesServerErrorsTwiceThenGivesUp()
    {
        var handler = new QueuedHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.BadGateway),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await CreateClient(handler).GetItemJsonAsync("123");

        Assert.Equal(BoothFetchStatus.TemporaryFailure, result.Status);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task RecoversWhenRetrySucceeds()
    {
        var handler = new QueuedHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            Ok("ok"));

        var result = await CreateClient(handler).GetItemJsonAsync("123");

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
        Assert.Equal(2, handler.RequestCount);
    }

    /// <summary>接続失敗も一時エラーとして扱い、非公開判定には数えない。</summary>
    [Fact]
    public async Task TreatsConnectionFailureAsTemporary()
    {
        var handler = new ThrowingHandler();

        var result = await CreateClient(handler).GetItemJsonAsync("123");

        Assert.Equal(BoothFetchStatus.TemporaryFailure, result.Status);
        Assert.Equal(3, handler.RequestCount);
    }

    /// <summary>429は「出し過ぎ」の申告なので、以降のリクエスト間隔そのものを倍にする。</summary>
    [Fact]
    public async Task SlowsDownAfterRateLimit()
    {
        var waits = new List<TimeSpan>();
        var client = CreateThrottleClient(new QueuedHandler(TooManyRequests(), Ok("ok")), waits);

        var result = await client.GetItemJsonAsync("123");

        Assert.True(result.IsSuccess);
        Assert.Equal(2000, client.CurrentIntervalMs);
        Assert.True(client.IsThrottled);
    }

    /// <summary>減速は上限で頭打ちにする。際限なく広がると実質フリーズするため。</summary>
    [Fact]
    public async Task CapsSlowdownAtConfiguredMaximum()
    {
        var waits = new List<TimeSpan>();
        var settings = new AppSettings { FetchIntervalMs = 1000, FetchIntervalMaxMs = 3000 };
        var client = CreateThrottleClient(
            new QueuedHandler(TooManyRequests(), TooManyRequests(), TooManyRequests()),
            waits,
            settings);

        var result = await client.GetItemJsonAsync("123");

        Assert.Equal(BoothFetchStatus.TemporaryFailure, result.Status);
        Assert.True(result.IsRateLimited);
        Assert.Equal(3000, client.CurrentIntervalMs);
    }

    /// <summary>Retry-Afterがこちらの既定より長ければ、素直にそちらに従う。</summary>
    [Fact]
    public async Task HonorsRetryAfterWhenLongerThanDefaultDelay()
    {
        var waits = new List<TimeSpan>();
        var client = CreateThrottleClient(
            new QueuedHandler(TooManyRequests(TimeSpan.FromSeconds(30)), Ok("ok")),
            waits);
        var retries = RetryWaitsOf(client);

        var result = await client.GetItemJsonAsync("123");

        Assert.True(result.IsSuccess);
        Assert.Contains(TimeSpan.FromSeconds(30), retries);
    }

    /// <summary>Retry-Afterが短くても、こちらの再試行間隔より前倒しはしない。</summary>
    [Fact]
    public async Task KeepsOwnDelayWhenRetryAfterIsShorter()
    {
        var waits = new List<TimeSpan>();
        var client = CreateThrottleClient(
            new QueuedHandler(TooManyRequests(TimeSpan.FromSeconds(1)), Ok("ok")),
            waits);
        var retries = RetryWaitsOf(client);

        await client.GetItemJsonAsync("123");

        Assert.Contains(TimeSpan.FromSeconds(2), retries);
        Assert.DoesNotContain(TimeSpan.FromSeconds(1), retries);
    }

    /// <summary>
    /// 指示された待ち時間が長すぎるときは、粘らずに1回で諦める。
    /// 待ち続けても取り込み全体が止まるだけなので、次回の実行に回した方がよい。
    /// </summary>
    [Fact]
    public async Task GivesUpWithoutWaitingWhenRetryAfterIsTooLong()
    {
        var waits = new List<TimeSpan>();
        var handler = new QueuedHandler(TooManyRequests(TimeSpan.FromMinutes(30)), Ok("ok"));
        var client = CreateThrottleClient(handler, waits);

        var result = await client.GetItemJsonAsync("123");

        Assert.Equal(BoothFetchStatus.TemporaryFailure, result.Status);
        Assert.True(result.IsRateLimited);
        Assert.Equal(1, handler.RequestCount);
        Assert.DoesNotContain(TimeSpan.FromMinutes(30), waits);
    }

    /// <summary>503もRetry-Afterを付けてくることがあるので従う。ただし減速はしない（こちらの責任ではない）。</summary>
    [Fact]
    public async Task HonorsRetryAfterOnServerErrorWithoutSlowingDown()
    {
        var waits = new List<TimeSpan>();
        var unavailable = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        unavailable.Headers.Add("Retry-After", "20");
        var client = CreateThrottleClient(new QueuedHandler(unavailable, Ok("ok")), waits);
        var retries = RetryWaitsOf(client);

        var result = await client.GetItemJsonAsync("123");

        Assert.True(result.IsSuccess);
        Assert.Contains(TimeSpan.FromSeconds(20), retries);
        Assert.False(client.IsThrottled);
    }

    /// <summary>
    /// 設定画面で間隔を変えたら、起動し直さなくてもその場で効く。
    /// 以前は起動時の値を抱えていて、縮めても広げても次に開くまで変わらなかった。
    /// </summary>
    [Fact]
    public void 設定で間隔を変えるとその場で効く()
    {
        var settings = new AppSettings { FetchIntervalMs = 1500 };
        var client = new BoothClient(new HttpClient(new QueuedHandler()), () => settings);

        settings = settings with { FetchIntervalMs = 3000 };
        Assert.Equal(3000, client.CurrentIntervalMs);
        Assert.False(client.IsThrottled);

        // 縮めたときに元の値が「広げている最中」に見え続けない
        settings = settings with { FetchIntervalMs = 2000 };
        Assert.Equal(2000, client.CurrentIntervalMs);
        Assert.False(client.IsThrottled);
    }

    /// <summary>429で広げている最中に設定を縮めても、相手が求めた分は詰めない。</summary>
    [Fact]
    public async Task 広げている最中に設定を縮めても広げた分は保つ()
    {
        var settings = new AppSettings { FetchIntervalMs = 1000, FetchIntervalMaxMs = 8000 };
        var client = new BoothClient(
            new HttpClient(new QueuedHandler(TooManyRequests(), Ok("ok"))),
            () => settings,
            (_, _) => Task.CompletedTask);

        await client.GetItemJsonAsync("123");
        Assert.Equal(2000, client.CurrentIntervalMs);

        settings = settings with { FetchIntervalMs = 1500 };
        Assert.Equal(2000, client.CurrentIntervalMs);
        Assert.True(client.IsThrottled);
    }

    /// <summary>ふつうの5xxでは減速しない。BOOTH側の不調にこちらが付き合う理由はない。</summary>
    [Fact]
    public async Task DoesNotSlowDownOnOrdinaryServerErrors()
    {
        var waits = new List<TimeSpan>();
        var client = CreateThrottleClient(
            new QueuedHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError), Ok("ok")),
            waits);

        await client.GetItemJsonAsync("123");

        Assert.False(client.IsThrottled);
    }

    [Theory]
    [InlineData("5813187", "https://booth.pm/ja/items/5813187.json")]
    public void BuildsItemJsonUrl(string itemId, string expected)
    {
        Assert.Equal(expected, BoothClient.ItemJsonUrl(itemId));
    }

    [Theory]
    [InlineData("5813187", "https://booth.pm/ja/items/5813187")]
    public void BuildsItemPageUrl(string itemId, string expected)
    {
        Assert.Equal(expected, BoothClient.ItemPageUrl(itemId));
    }
}
