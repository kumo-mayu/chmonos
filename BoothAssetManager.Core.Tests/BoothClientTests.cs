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

    private static BoothClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler),
            new AppSettings { FetchIntervalMs = 0 },
            delay: (_, _) => Task.CompletedTask);

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
