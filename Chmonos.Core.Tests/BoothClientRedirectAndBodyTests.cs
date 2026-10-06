using System.Net;
using Chmonos.Core.Booth;
using Chmonos.Core.Models;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 門の外で通信が起きない・門を握ったまま戻らない、を止める（2026-10-06 の外部の点検）。
///
/// 前は出口の自動の転送が転送先へ門を通らずに出ていた。見出しの後に本文が止まると、
/// 30秒の期限が効かずに PC で1つの門を握り続けた。
/// </summary>
public sealed class BoothClientRedirectAndBodyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chmonos-redirect-tests-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<BoothMachineGate> _gates = [];

    public BoothClientRedirectAndBodyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var gate in _gates)
        {
            gate.Dispose();
        }

        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>問い合わせごとに応答を作る作り物。届いた URL を控える。</summary>
    private sealed class ScriptedHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request.RequestUri!.AbsoluteUri);
            }

            return respond(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Redirect(HttpStatusCode status, string location)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    /// <summary>待ちを差し替えたクライアント。言われた待ちの長さを <paramref name="waits"/> に足す。</summary>
    private static BoothClient Client(HttpMessageHandler handler, List<TimeSpan>? waits = null, int intervalMs = 1500)
        => new(new HttpClient(handler),
            new AppSettings { FetchIntervalMs = intervalMs },
            delay: (duration, _) =>
            {
                waits?.Add(duration);
                return Task.CompletedTask;
            });

    private static TimeSpan Sum(IEnumerable<TimeSpan> waits) => waits.Aggregate(TimeSpan.Zero, (sum, wait) => sum + wait);

    /// <summary>試験が固まらないように。直す前は戻ってこないので、期限で落とす。</summary>
    private static Task<T> Bounded<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(15));

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    public async Task BOOTHの中への転送は門を通して間隔を空けて取り直す(HttpStatusCode status)
    {
        var handler = new ScriptedHandler((request, _) => Task.FromResult(
            request.RequestUri!.Query == ""
                ? Redirect(status, "https://booth.pm/ja/items/1.json?moved")
                : Ok("{}")));
        var waits = new List<TimeSpan>();

        var result = await Client(handler, waits).GetItemJsonAsync("1");

        Assert.True(result.IsSuccess);
        Assert.Equal(["https://booth.pm/ja/items/1.json", "https://booth.pm/ja/items/1.json?moved"], handler.Requests);

        // 転送先への1本も、1本目から間隔を空けて出た（待ちを差し替えているので、言われた長さで見る。
        // 長さは1本目が終わってから実際に過ぎた分だけ間隔より短い。直す前は待ちが0だった）
        Assert.True(Sum(waits) >= TimeSpan.FromMilliseconds(1400), $"待ち {Sum(waits).TotalMilliseconds}ms");
    }

    [Fact]
    public async Task 転送先の相対の指定は今の問い合わせ先から読む()
    {
        var handler = new ScriptedHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/ja/items/1"
                ? Redirect(HttpStatusCode.Found, "/ja/items/2")
                : Ok("<html></html>")));

        var result = await Client(handler, intervalMs: 0).GetItemHtmlAsync("1");

        Assert.True(result.IsSuccess);
        Assert.Equal("https://booth.pm/ja/items/2", handler.Requests[1]);
    }

    [Theory]
    [InlineData("https://example.com/ja/items/1.json")]
    [InlineData("https://booth.pm.example.com/x")]
    [InlineData("https://notbooth.pm/x")]
    [InlineData("http://booth.pm/ja/items/1.json")]
    public async Task 許さない先への転送は追わずに失敗で返し取り直さない(string location)
    {
        var handler = new ScriptedHandler((_, _) => Task.FromResult(Redirect(HttpStatusCode.Found, location)));

        var result = await Client(handler, intervalMs: 0).GetItemJsonAsync("1");

        Assert.Equal(BoothFetchStatus.TemporaryFailure, result.Status);
        Assert.True(result.IsRejected);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("https://shop.booth.pm/")]
    [InlineData("https://booth.pximg.net/c/300x300/i/1/a.jpg")]
    public void BOOTHの置き場への転送は許す(string location)
        => Assert.True(BoothClient.IsAllowedRedirectTarget(new Uri(location)));

    [Fact]
    public async Task 転送の連鎖は上限までたどる()
    {
        // 1 → 2 → 3 → … → 6 で中身。転送は5回で、上限ちょうど
        var handler = new ScriptedHandler((request, _) =>
        {
            var step = int.Parse(request.RequestUri!.Query.TrimStart('?', 's', '='));
            return Task.FromResult(step < 1 + BoothClient.MaxRedirects
                ? Redirect(HttpStatusCode.Found, $"https://booth.pm/hop?s={step + 1}")
                : Ok("end"));
        });

        var result = await Client(handler, intervalMs: 0).GetBrowsePageAsync("https://booth.pm/hop?s=1");

        Assert.True(result.IsSuccess);
        Assert.Equal("end", result.Value);
        Assert.Equal(1 + BoothClient.MaxRedirects, handler.Requests.Count);
    }

    [Fact]
    public async Task 転送が上限を超えたら打ち切って失敗で返す()
    {
        // 輪になった転送。上限が効かないと回り続けて試験ごと固まるので、50本目で中身を返して止める
        var count = 0;
        var handler = new ScriptedHandler((_, _) => Task.FromResult(++count < 50
            ? Redirect(HttpStatusCode.Found, "https://booth.pm/loop")
            : Ok("runaway")));

        var result = await Bounded(Client(handler, intervalMs: 0).GetBrowsePageAsync("https://booth.pm/loop"));

        Assert.True(result.IsRejected);
        Assert.Equal(1 + BoothClient.MaxRedirects, handler.Requests.Count);
    }

    /// <summary>
    /// 自動の転送を切っていない出口を渡されたら、出口が門の外で転送先へ出てしまう。
    /// 受け取った応答の先が頼んだ先と違えば捨てる（付け忘れの見張り）。
    /// </summary>
    [Fact]
    public async Task 出口が勝手に転送をたどった応答は受け取らない()
    {
        var handler = new ScriptedHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}"),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.com/elsewhere"),
        }));

        var result = await Client(handler, intervalMs: 0).GetItemJsonAsync("1");

        Assert.True(result.IsRejected);
    }

    [Fact]
    public void 組み立ては自動の転送を切る()
    {
        var plain = new HttpClientHandler();
        var inner = new SocketsHttpHandler();
        using var _ = BoothClient.CreateHttpClient(plain);
        using var __ = BoothClient.CreateHttpClient(new PassThrough(inner));

        Assert.False(plain.AllowAutoRedirect);
        Assert.False(inner.AllowAutoRedirect);
    }

    private sealed class PassThrough(HttpMessageHandler inner) : DelegatingHandler(inner);

    /// <summary>見出しを返した後、本文を1バイトも返さずに止まる流れ。</summary>
    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <summary>いくらでも続く本文。読んだ量を数える。</summary>
    private sealed class EndlessStream : Stream
    {
        public long Served { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)'a', offset, count);
            Served += count;
            return count;
        }
    }

    private BoothMachineGate Gate()
    {
        var gate = new BoothMachineGate(Path.Combine(_dir, "booth-gate.txt"));
        _gates.Add(gate);
        return gate;
    }

    /// <summary>PC の門に入るクライアント。期限は試験のために短くする。</summary>
    private static BoothClient GatedClient(HttpMessageHandler handler, BoothMachineGate gate, TimeSpan timeout)
        => new(
            new HttpClient(handler),
            SettingsSource.Fixed(new AppSettings { FetchIntervalMs = 1500 }),
            (_, _) => Task.CompletedTask,
            gate,
            TimeProvider.System)
        {
            RequestTimeout = timeout,
        };

    [Fact]
    public async Task 見出しの後に本文が止まっても期限で戻りPCの門が空く()
    {
        var stalled = new ScriptedHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StalledStream()),
        }));
        var other = new ScriptedHandler((_, _) => Task.FromResult(Ok("ok")));
        var stalledClient = GatedClient(stalled, Gate(), TimeSpan.FromMilliseconds(200));
        var otherClient = GatedClient(other, Gate(), TimeSpan.FromSeconds(30));

        var stuck = stalledClient.GetItemJsonAsync("1");
        var next = otherClient.GetItemJsonAsync("2");

        var result = await Bounded(stuck);
        Assert.Equal(BoothFetchStatus.TemporaryFailure, result.Status);
        Assert.Equal("タイムアウトしました", result.Error);

        // 同じ PC の門に並んでいた別のアプリも出られる
        Assert.True((await Bounded(next)).IsSuccess);
    }

    [Fact]
    public async Task 見出しが来なくても期限で戻る()
    {
        var silent = new ScriptedHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Ok("never");
        });

        var result = await Bounded(GatedClient(silent, Gate(), TimeSpan.FromMilliseconds(100)).GetItemHtmlAsync("1"));

        Assert.True(result.IsUnreachable);
    }

    [Fact]
    public async Task 長さを名乗った大きすぎる本文は読まずに捨てる()
    {
        var body = new EndlessStream();
        var handler = new ScriptedHandler((_, _) =>
        {
            var content = new StreamContent(body);
            content.Headers.ContentLength = BoothClient.MaxImageBytes + 1L;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });

        var result = await Bounded(Client(handler, intervalMs: 0).GetBinaryAsync("https://booth.pximg.net/a.jpg"));

        Assert.True(result.IsRejected);
        Assert.Equal(0, body.Served);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task 長さを名乗らずに続く本文は上限で受けるのをやめる()
    {
        var body = new EndlessStream();
        var handler = new ScriptedHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(body),
        }));

        var result = await Bounded(Client(handler, intervalMs: 0).GetItemHtmlAsync("1"));

        Assert.True(result.IsRejected);
        Assert.True(body.Served <= BoothClient.MaxTextBytes + 128 * 1024, $"読んだ量 {body.Served}");
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task 上限で捨てた後も次の問い合わせは出られる()
    {
        var first = true;
        var handler = new ScriptedHandler((_, _) =>
        {
            if (!first)
            {
                return Task.FromResult(Ok("ok"));
            }

            first = false;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new EndlessStream()) });
        });
        var client = GatedClient(handler, Gate(), TimeSpan.FromSeconds(30));

        Assert.True((await Bounded(client.GetItemJsonAsync("1"))).IsRejected);
        Assert.True((await Bounded(client.GetItemJsonAsync("2"))).IsSuccess);
    }
}
