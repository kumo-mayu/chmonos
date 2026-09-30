using System.Net;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Models;
using Xunit;

namespace BoothAssetManager.Core.Tests;

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
}
