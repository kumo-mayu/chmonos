using System.Net;
using Chmonos.Core.Booth;
using Chmonos.Core.Models;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 最初に渡された URL も、転送の先と同じ一覧（booth.pm・*.booth.pm・booth.pximg.net。https）で縛る（外部の点検 2026-10-06・ユーザ判断「縛る」）。
/// 画像の URL は商品の JSON（<c>booth.images[].originalUrl</c>）から来るので、前は相手が書いた先へそのまま出ていた。
/// </summary>
public sealed class BoothClientFirstUrlTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Sent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        }
    }

    private static BoothClient Client(HttpMessageHandler handler, bool loopback = false)
        => new(new HttpClient(handler), new AppSettings { FetchIntervalMs = 0 }, delay: (_, _) => Task.CompletedTask)
        {
            AllowsLoopbackForProbe = loopback,
        };

    [Theory]
    [InlineData("https://example.com/a.png")]
    [InlineData("https://booth.pximg.net.example.com/a.png")]
    [InlineData("https://notbooth.pm/a.png")]
    [InlineData("http://booth.pximg.net/a.png")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("http://127.0.0.1/a.png")]
    [InlineData("/relative/a.png")]
    [InlineData("")]
    public async Task 許さない先の画像は送らずに失敗で返す(string url)
    {
        var handler = new CountingHandler();

        var result = await Client(handler).GetBinaryAsync(url);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsRejected);
        Assert.Equal(0, handler.Sent);
    }

    [Fact]
    public async Task 許さない先の文字の取得も送らない()
    {
        var handler = new CountingHandler();

        var result = await Client(handler).GetTextUntilAsync("https://example.com/shop", _ => true);

        Assert.True(result.IsRejected);
        Assert.Equal(0, handler.Sent);
    }

    [Theory]
    [InlineData("https://booth.pximg.net/c/72a0x72/9900001/a.png")]
    [InlineData("https://s2.booth.pm/9900001/a.png")]
    [InlineData("https://BOOTH.PXIMG.NET/a.png")]
    public async Task BOOTHの画像の先へは送る(string url)
    {
        var handler = new CountingHandler();

        var result = await Client(handler).GetBinaryAsync(url);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, handler.Sent);
    }

    [Fact]
    public async Task 試験台の口を立てた組み立てだけが手元の作り物へ出られる()
    {
        var handler = new CountingHandler();

        var result = await Client(handler, loopback: true).GetBinaryAsync("http://127.0.0.1:5000/x");
        var outside = await Client(handler, loopback: true).GetBinaryAsync("https://example.com/x");

        Assert.True(result.IsSuccess);
        Assert.True(outside.IsRejected);
        Assert.Equal(1, handler.Sent);
    }
}
