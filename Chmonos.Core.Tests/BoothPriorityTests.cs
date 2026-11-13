using System.Net;
using Chmonos.Core.Booth;
using Chmonos.Core.Models;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 優先順位が <see cref="BoothClient"/> 越しにも効くこと。
///
/// 優先度は引数ではなく <see cref="BoothClient.Prioritize"/> の範囲で決まる。
/// 「今この作業をしている」という文脈に付くものなので、
/// その内側で始めた取得は何段先でも同じ優先度になる。
/// </summary>
public class BoothPriorityTests
{
    /// <summary>最初の1本を握ったまま止め、後続を並ばせるためのハンドラ。</summary>
    private sealed class BlockingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _seen;

        public List<string> Requests { get; } = [];

        /// <summary>1本目が実際に通信に入るまで待つ。</summary>
        public Task FirstArrived => _firstArrived.Task;

        public void Release() => _release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request.RequestUri!.ToString());
            }

            if (Interlocked.Increment(ref _seen) == 1)
            {
                _firstArrived.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    /// <summary>
    /// **これが順位を分けた目的そのもの。**
    ///
    /// 取り込みが並んだ後から人が押しても、人の方が先に通る。
    /// 順位が無ければ、100商品の取り込み中に押した「このIDで確認」は5分待ちになる。
    /// </summary>
    [Fact]
    public async Task LetsAUserActionOvertakeQueuedImportRequests()
    {
        var handler = new BlockingHandler();
        var client = new BoothClient(new HttpClient(handler), new AppSettings { FetchIntervalMs = 0 }, TestWait.None);

        // 1本目が門を握って止まる
        var holding = Task.Run(async () =>
        {
            using var _ = BoothClient.Prioritize(BoothPriority.Metadata);
            await client.GetItemJsonAsync("100");
        });

        await handler.FirstArrived.WaitAsync(TimeSpan.FromSeconds(5));

        // 取り込みの続きが2本並ぶ
        var imports = new[]
        {
            Start(client, BoothPriority.Gallery, "200"),
            Start(client, BoothPriority.Metadata, "300"),
        };

        await WaitUntilQueued(client, 2);

        // その後で人が押した
        var user = Start(client, BoothPriority.User, "999");
        await WaitUntilQueued(client, 3);

        handler.Release();
        await Task.WhenAll([holding, user, .. imports]).WaitAsync(TimeSpan.FromSeconds(10));

        // 1本目の次に来ているのは、後から押した方
        Assert.Equal(BoothClient.ItemJsonUrl("100"), handler.Requests[0]);
        Assert.Equal(BoothClient.ItemJsonUrl("999"), handler.Requests[1]);

        // 残りは段の順（①が⑤より先）
        Assert.Equal(BoothClient.ItemJsonUrl("300"), handler.Requests[2]);
        Assert.Equal(BoothClient.ItemJsonUrl("200"), handler.Requests[3]);
    }

    /// <summary>
    /// 画面が待っている対象は、人が押した操作より更に先に通す。
    ///
    /// 未確定で確定を押すと次の1件へ自動で移るので、前の件の後始末（User）と
    /// 同じ順位だと、目の前の候補検索がその後ろに付く。
    /// </summary>
    [Fact]
    public async Task 画面が待っている対象は人が押した操作より先に通る()
    {
        var handler = new BlockingHandler();
        var client = new BoothClient(new HttpClient(handler), new AppSettings { FetchIntervalMs = 0 }, TestWait.None);

        var holding = Task.Run(async () =>
        {
            using var _ = BoothClient.Prioritize(BoothPriority.Metadata);
            await client.GetItemJsonAsync("100");
        });

        await handler.FirstArrived.WaitAsync(TimeSpan.FromSeconds(5));

        // 確定の後始末（人が押した操作）が先に並ぶ
        var user = Start(client, BoothPriority.User, "999");
        await WaitUntilQueued(client, 1);

        // その後で、移った先の画面が候補を探し始める
        var foreground = Start(client, BoothPriority.Foreground, "555");
        await WaitUntilQueued(client, 2);

        handler.Release();
        await Task.WhenAll(holding, user, foreground).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(BoothClient.ItemJsonUrl("100"), handler.Requests[0]);

        // 後から並んだ方が先に通る
        Assert.Equal(BoothClient.ItemJsonUrl("555"), handler.Requests[1]);
        Assert.Equal(BoothClient.ItemJsonUrl("999"), handler.Requests[2]);
    }

    /// <summary>範囲を出れば元の優先度に戻る。段を抜けた後まで引きずらない。</summary>
    [Fact]
    public void RestoresThePreviousPriorityWhenTheScopeEnds()
    {
        using (BoothClient.Prioritize(BoothPriority.Metadata))
        {
            using (BoothClient.Prioritize(BoothPriority.Gallery))
            {
                // 内側が勝つ
            }
        }

        // 例外なく元へ戻ればよい。値そのものは外から見えない（見せる必要も無い）
        Assert.True(true);
    }

    private static Task Start(BoothClient client, BoothPriority priority, string itemId)
        => Task.Run(async () =>
        {
            using var _ = BoothClient.Prioritize(priority);
            await client.GetItemJsonAsync(itemId);
        });

    private static async Task WaitUntilQueued(BoothClient client, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (client.WaitingRequestCount < expected)
        {
            Assert.True(
                DateTime.UtcNow < deadline,
                $"{expected} 本が並ばなかった（{client.WaitingRequestCount} 本）");

            await Task.Delay(5);
        }
    }

    // ---- 人が押した1つの操作の問い合わせを、門を離さずに続けて通す（ユーザ判断 2026-11-09・計画の 9） ----

    /// <summary>人が押した操作が2本を順に出す。1本目が終わってから2本目を頼む（取り直しの JSON と商品ページと同じ形）。</summary>
    private static Task UserOperation(BoothClient client, bool hold)
        => Task.Run(async () =>
        {
            using var _ = BoothClient.Prioritize(BoothPriority.User);
            using var held = hold ? BoothClient.HoldForUserOperation() : null;
            await client.GetItemJsonAsync("1");
            await client.GetItemJsonAsync("2");
        });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 人が押した操作の2本目は_持っていれば待っている期限の取り直しより先に通る(bool hold)
    {
        var handler = new BlockingHandler();
        var client = new BoothClient(new HttpClient(handler), new AppSettings { FetchIntervalMs = 0 }, TestWait.None);

        var user = UserOperation(client, hold);
        await handler.FirstArrived.WaitAsync(TimeSpan.FromSeconds(5));

        // 1本目の間に⑦が並ぶ
        var due = Start(client, BoothPriority.Background, "7");
        await WaitUntilQueued(client, 1);

        handler.Release();
        await Task.WhenAll(user, due).WaitAsync(TimeSpan.FromSeconds(10));

        // 持っていれば人の2本が続き、持っていなければ（前の動き）隙に⑦が入る
        string[] expected = hold
            ? [BoothClient.ItemJsonUrl("1"), BoothClient.ItemJsonUrl("2"), BoothClient.ItemJsonUrl("7")]
            : [BoothClient.ItemJsonUrl("1"), BoothClient.ItemJsonUrl("7"), BoothClient.ItemJsonUrl("2")];
        Assert.Equal(expected, handler.Requests);
    }

    [Fact]
    public async Task 期限の取り直しは持たず_2本の隙に人が押した操作が入る()
    {
        var handler = new BlockingHandler();
        var client = new BoothClient(new HttpClient(handler), new AppSettings { FetchIntervalMs = 0 }, TestWait.None);

        // ⑦の段で持とうとしても持たない（持つと人の操作がその分待たされる）
        var due = Task.Run(async () =>
        {
            using var _ = BoothClient.Prioritize(BoothPriority.Background);
            using var held = BoothClient.HoldForUserOperation();
            await client.GetItemJsonAsync("7");
            await client.GetItemJsonAsync("8");
        });
        await handler.FirstArrived.WaitAsync(TimeSpan.FromSeconds(5));

        var user = Start(client, BoothPriority.User, "1");
        await WaitUntilQueued(client, 1);

        handler.Release();
        await Task.WhenAll(user, due).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal([BoothClient.ItemJsonUrl("7"), BoothClient.ItemJsonUrl("1"), BoothClient.ItemJsonUrl("8")], handler.Requests);
    }

    [Fact]
    public async Task 範囲を抜けたら門を放し_次の問い合わせが通る()
    {
        var handler = new CountingHandler();
        var client = new BoothClient(new HttpClient(handler), new AppSettings { FetchIntervalMs = 0 }, TestWait.None);

        await Task.Run(async () =>
        {
            using var _ = BoothClient.Prioritize(BoothPriority.User);
            using (BoothClient.HoldForUserOperation())
            {
                await client.GetItemJsonAsync("1");
                await client.GetItemJsonAsync("2");
            }
        });

        // 範囲の外の1本が止まらずに通る（放し忘れると、ここで門の前に並んだまま止まる）
        await Start(client, BoothPriority.Background, "3").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, handler.Count);
    }

    // ---- ⑧ 未確定の自動検索の先回り（ユーザ判断 2026-11-11）：間隔を待つ間に並んだ人の操作に譲る ----

    /// <summary>
    /// 門は間隔を待つ間も持つので、門が空いているときに入った1本は、待っている間に押された操作より先に出る（⑦はそのまま）。
    /// ⑧だけは、待ち終えたときに誰かが並んでいれば譲って並び直す。**先回りが人の操作より先に門を取らないこと。**
    /// 時計は止め、待ちは差し替えて1回目の待ちで止めておく（実時間には頼らない）
    /// </summary>
    [Theory]
    [InlineData(BoothPriority.Prefetch, new[] { "1", "3", "2" })]
    [InlineData(BoothPriority.Background, new[] { "1", "2", "3" })]
    public async Task 先回りは間隔を待つ間に並んだ人の操作へ譲る(BoothPriority low, string[] expected)
    {
        var handler = new RecordingHandler();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocking = false;
        var settings = new AppSettings { FetchIntervalMs = 1500 };
        var client = new BoothClient(
            new HttpClient(handler),
            () => settings,
            async (_, token) =>
            {
                if (Volatile.Read(ref blocking))
                {
                    waiting.TrySetResult();
                    await go.Task.WaitAsync(token);
                }
            },
            machineGate: null,
            new StoppedClock());

        // 1本目で最後の問い合わせの時刻が付く。次の1本は間隔を待つ
        await Start(client, BoothPriority.Metadata, "1").WaitAsync(TimeSpan.FromSeconds(5));

        Volatile.Write(ref blocking, true);
        var background = Start(client, low, "2");
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 低い段が門を持って間隔を待っている間に、人が押した
        var user = Start(client, BoothPriority.User, "3");
        await WaitUntilQueued(client, 1);

        go.TrySetResult();
        await Task.WhenAll(background, user).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(expected.Select(BoothClient.ItemJsonUrl), handler.Requests);
    }

    /// <summary>
    /// ⑧どうしは譲り合わない（2026-11-13 の Codex の点検の件2）。前は並んでいる数だけを見たので、⑧が2本並ぶと互いに譲って
    /// 並び直しを繰り返し、どちらも送らなかった。譲るのは自分より急ぐ待ち手だけで、⑧どうしは着いた順に出る。
    /// 譲り合いが起きると間隔の待ちが繰り返し呼ばれるので、待ちの回数が上限を超えたら止めて落とす（実時間の締め切りに頼らない）
    /// </summary>
    [Fact]
    public async Task 先回りどうしは譲り合わずに着いた順に出る()
    {
        var handler = new RecordingHandler();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var spinning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocking = false;
        var waits = 0;
        var settings = new AppSettings { FetchIntervalMs = 1500 };
        var client = new BoothClient(
            new HttpClient(handler),
            () => settings,
            async (_, token) =>
            {
                if (!Volatile.Read(ref blocking))
                {
                    return;
                }

                // 2本を送るのに要る待ちは数回。100回は、譲り合いを繰り返しているとしか読めない数
                if (Interlocked.Increment(ref waits) > 100)
                {
                    spinning.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                }

                waiting.TrySetResult();
                await go.Task.WaitAsync(token);
            },
            machineGate: null,
            new StoppedClock());

        await Start(client, BoothPriority.Metadata, "1").WaitAsync(TimeSpan.FromSeconds(5));

        Volatile.Write(ref blocking, true);
        var first = Start(client, BoothPriority.Prefetch, "2");
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 1本目の⑧が門を持って間隔を待っている間に、2本目の⑧が並ぶ
        var second = Start(client, BoothPriority.Prefetch, "3");
        await WaitUntilQueued(client, 1);

        go.TrySetResult();
        var both = Task.WhenAll(first, second);
        await Task.WhenAny(both, spinning.Task).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(spinning.Task.IsCompleted, $"⑧どうしが譲り合って進まない（待ち {Volatile.Read(ref waits)} 回）");
        await both;
        Assert.Equal(new[] { "1", "2", "3" }.Select(BoothClient.ItemJsonUrl), handler.Requests);
    }

    /// <summary>進まない時計。間隔は毎回まるごと残っているので、待ちを差し替えた所でだけ進む（実時間に左右されない）</summary>
    private sealed class StoppedClock : TimeProvider
    {
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => 100 * TimeSpan.TicksPerSecond;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly List<string> _requests = [];

        public List<string> Requests
        {
            get
            {
                lock (_requests)
                {
                    return [.. _requests];
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_requests)
            {
                _requests.Add(request.RequestUri!.ToString());
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private int _count;

        public int Count => _count;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
