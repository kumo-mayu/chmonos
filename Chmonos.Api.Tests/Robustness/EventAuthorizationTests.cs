using System.Net;
using Chmonos.Core.Models;

namespace Chmonos.Api.Tests.Robustness;

/// <summary>出来事の列を、許したソフトにだけ、許した種類だけ渡すこと（偽った続き・多数の長い待ちの取り消し・結果を受ける通番の上限）。</summary>
public sealed class EventAuthorizationTests : LocalApiFixture
{
    [Fact]
    public async Task 許可を外した種類は_未受信の出来事も返さない()
    {
        await Subscribe();
        Hub.PublishItemOpened("9600901");
        Hub.PublishSelection("search", ["9600901"]);
        await ReplaceClients(Record("a", TokenA) with { Scopes = [ApiScopes.WatchSelectionChanged] }, Record("b", TokenB));
        Assert.DoesNotContain("itemOpened", Hub.KindsOf("a"));
        var answer = await GetJson("/v1/plugin/events?after=0&wait=0");
        Assert.DoesNotContain(answer.GetProperty("events").EnumerateArray(), entry => entry.GetProperty("kind").GetString() == "itemOpened");
        Assert.Contains(answer.GetProperty("events").EnumerateArray(), entry => entry.GetProperty("kind").GetString() == "selectionChanged");

        // 除いた分を「あふれで捨てた」に数えない（外した種類が何件あったかを漏らさない）
        Assert.Equal(0, answer.GetProperty("dropped").GetInt64());

        // 許し直して購読し直しても、外す前の出来事は戻らない
        await ReplaceClients(Record("a", TokenA), Record("b", TokenB));
        await Subscribe();
        var again = await GetJson("/v1/plugin/events?after=0&wait=0");
        Assert.DoesNotContain(again.GetProperty("events").EnumerateArray(), entry => entry.GetProperty("kind").GetString() == "itemOpened");
    }

    [Fact]
    public async Task 購読を止めてから許可を外しても_止める前に積んだ出来事は返さない()
    {
        await GetJson("/v1/plugin/events?wait=0");
        await Subscribe();
        Hub.PublishItemOpened("9600901");
        Assert.True((await Hub.SendMenuAsync("a", "preview", "item", ["9600901"])).Sent);

        using (var stop = await Client.SendAsync(Request(HttpMethod.Put, "/v1/plugin/subscriptions", """{"kinds":[]}""")))
        {
            Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
        }

        await ReplaceClients(Record("a", TokenA) with { Scopes = [ApiScopes.WatchSelectionChanged] }, Record("b", TokenB));
        var answer = await GetJson("/v1/plugin/events?after=0&wait=0");
        Assert.DoesNotContain(answer.GetProperty("events").EnumerateArray(), entry => entry.GetProperty("kind").GetString() == "itemOpened");
        Assert.Equal(0, answer.GetProperty("dropped").GetInt64());

        // 項目を押した出来事は見張りの許しと別なので残る
        Assert.Contains(answer.GetProperty("events").EnumerateArray(), entry => entry.GetProperty("kind").GetString() == "menu");

        // 許し直して購読し直しても、外す前の出来事は戻らない
        await ReplaceClients(Record("a", TokenA), Record("b", TokenB));
        await Subscribe();
        var again = await GetJson("/v1/plugin/events?after=0&wait=0");
        Assert.DoesNotContain(again.GetProperty("events").EnumerateArray(), entry => entry.GetProperty("kind").GetString() == "itemOpened");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 購読の本文を待つ間に許しを外しても_外した種類を購読できない(bool revoke)
    {
        // 入口が要求の初めの控えを持ったまま本文を読み終えた所で、設定の画面が許しを外す（取り消す）。順番は差し込み口で決める
        Hub.BeforeSubscribeForTest = () =>
        {
            Hub.BeforeSubscribeForTest = null;
            var clients = revoke ? new[] { Record("b", TokenB) } : [Record("a", TokenA) with { Scopes = [ApiScopes.WatchSelectionChanged] }, Record("b", TokenB)];
            ReplaceClients(clients).GetAwaiter().GetResult();
        };

        using var request = Request(HttpMethod.Put, "/v1/plugin/subscriptions", """{"kinds":["itemOpened","selectionChanged"]}""");
        using var response = await Client.SendAsync(request);
        Assert.Equal(revoke ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(Hub.KindsOf("a"));
        Assert.False(Hub.Watches("itemOpened"));
    }

    [Theory]
    [InlineData("-9223372036854775808", "wrong-epoch")]
    [InlineData("9223372036854775807", "")]
    public async Task 偽った続きでも_別のソフトの列は返さない(string after, string epoch)
    {
        await Subscribe();
        Hub.PublishItemOpened("9600901");
        var other = await GetJson($"/v1/plugin/events?after={after}&epoch={epoch}", TokenB);
        Assert.Empty(other.GetProperty("events").EnumerateArray());
        Assert.True(other.GetProperty("reset").GetBoolean());
        Assert.Single((await GetJson("/v1/plugin/events")).GetProperty("events").EnumerateArray());
    }

    [Fact]
    public async Task 同時に多数の長い待ちがあっても_取消で全て解け_他のソフトへ漏れない()
    {
        await Subscribe();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var requests = Enumerable.Range(0, 32).Select(_ => Request(HttpMethod.Get, "/v1/plugin/events?wait=30")).ToArray();
        try
        {
            var waiting = requests.Select(request => Client.SendAsync(request, cancellation.Token)).ToArray();
            // 少なくとも1本が待ちに入ったことを見てから取り消す。未到着の要求は401でよい。
            for (var i = 0; i < 200 && !Hub.PollStateOf("a").Waiting; i++) await Task.Delay(10);
            Assert.True(Hub.PollStateOf("a").Waiting);
            await ReplaceClients(Record("b", TokenB));
            Hub.PublishItemOpened("9600901");
            foreach (var response in await Task.WhenAll(waiting).WaitAsync(TimeSpan.FromSeconds(5)))
            {
                using (response)
                {
                    Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                        Assert.Empty(document.RootElement.GetProperty("events").EnumerateArray());
                    }
                }
            }
            Assert.Empty((await GetJson("/v1/plugin/events", TokenB)).GetProperty("events").EnumerateArray());
            using var revoked = await Client.SendAsync(Request(HttpMethod.Get, "/v1/plugin/menu"));
            Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        }
        finally
        {
            cancellation.Cancel();
            foreach (var request in requests) request.Dispose();
        }
    }

    [Fact]
    public async Task 結果の最新32件を越えた通番は_同じソフトでも受けない()
    {
        await GetJson("/v1/plugin/events");
        var sends = new List<long>();
        for (var i = 0; i < 33; i++)
        {
            var sent = await Hub.SendMenuAsync("a", "preview", "item", ["9600901"]);
            Assert.True(sent.Sent);
            sends.Add(sent.Seq);
        }
        var results = new List<PluginMenuResult>();
        Hub.MenuResult += results.Add;
        using var oldest = await Client.SendAsync(Request(HttpMethod.Post, $"/v1/plugin/events/{sends[0]}/result", """{"ok":true,"message":"完了"}"""));
        Assert.Equal(HttpStatusCode.NotFound, oldest.StatusCode);
        using var next = await Client.SendAsync(Request(HttpMethod.Post, $"/v1/plugin/events/{sends[1]}/result", """{"ok":true,"message":"完了"}"""));
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        Assert.Single(results);
    }
}
