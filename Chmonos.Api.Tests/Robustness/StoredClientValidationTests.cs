using System.Net;
using System.Text.Json.Nodes;
using Chmonos.Core.Models;

namespace Chmonos.Api.Tests.Robustness;

/// <summary>
/// 手で書いた・壊れた・同時に読み直した許しの控え（<c>api-clients.json</c>）が、許しを広げないこと：重複した ID・壊れた JSON・知らない権限と宣言、
/// 2本の読み直しが逆の順に終わったとき（件10・12。docs/feedback/review-2026-11-13-codex.md）
/// </summary>
public sealed class StoredClientValidationTests : LocalApiFixture
{
    [Fact]
    public async Task 取り消す前の一覧を読んだ読み直しが後から終わっても_取り消したトークンは通らない()
    {
        // 1本目（見張りなど）は取り消す前の一覧を読み終えたところで止める。止めるのは1本目だけ
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = 0;
        State.Clients.AfterLoadForTest = () =>
        {
            if (Interlocked.Exchange(ref first, 1) == 0)
            {
                loaded.SetResult();
                release.Task.Wait();
            }
        };
        var stale = Task.Run(State.Clients.Reload);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 取り消して読み直す（設定の画面の道）。これが先に終わる
        await ReplaceClients(Record("b", TokenB));
        release.SetResult();
        await stale.WaitAsync(TimeSpan.FromSeconds(5));

        using var revoked = await Client.SendAsync(Request(HttpMethod.Get, "/v1/plugin/menu"));
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Null(State.Clients.ById("a"));
    }

    [Fact]
    public async Task 控えの重複ソフトIDは_例外を出さず隔離する()
    {
        await Store.ApiClients.UpdateAsync(list => list with { Clients = [Record("a", TokenA), Record("a", TokenB)] });
        var exception = Xunit.Record.Exception(() => State.Clients.Reload());
        Assert.Null(exception);
        // IDで列を共有する2つのトークンを、そのまま有効にしない。
        using var a = await Client.SendAsync(Request(HttpMethod.Get, "/v1/plugin/events"));
        using var b = await Client.SendAsync(Request(HttpMethod.Get, "/v1/plugin/events", token: TokenB));
        Assert.False(a.StatusCode == HttpStatusCode.OK && b.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task 重複IDの読み直しが失敗しても_別トークンへ既存の列を渡さない()
    {
        await Subscribe();
        Hub.PublishItemOpened("9600901");
        await Store.ApiClients.UpdateAsync(list => list with { Clients = [Record("a", TokenA), Record("a", TokenB)] });
        // 例外の有無とは別に、失敗後の認証索引が既に変わっていないかをHTTPで見る。
        try { State.Clients.Reload(); }
        catch (ArgumentException) { }
        using var response = await Client.SendAsync(Request(HttpMethod.Get, "/v1/plugin/events", token: TokenB));
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Empty(document.RootElement.GetProperty("events").EnumerateArray());
        }
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"clients\":null,\"refusals\":null}")]
    public async Task 壊れた控えと欠けた配列は_旧トークンとメニューを無効にする(string text)
    {
        await Subscribe();
        Hub.PublishItemOpened("9600901");
        await File.WriteAllTextAsync(Store.Paths.ApiClientsFile, text);
        State.Clients.Reload();
        using var response = await Client.SendAsync(Request(HttpMethod.Get, "/v1/plugin/events"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(Hub.MenusFor("item", ["9600901"]));
        Assert.False(Hub.Watches("itemOpened"));
    }

    [Fact]
    public async Task 控えの不正メニューと未知の権限は_別のソフトの許可を変えない()
    {
        await ReplaceClients(Record("a", TokenA) with
        {
            Scopes = ["future.write"],
            Menu = [new PluginMenuItemRecord { Id = "invalid", Label = "不正", Where = ["future-place"] }],
        }, Record("b", TokenB));
        Assert.Empty((await GetJson("/v1/plugin/menu")).GetProperty("items").EnumerateArray());
        Assert.Equal(new[] { "read" }, (await GetJson("/v1/clients/me")).GetProperty("scopes").EnumerateArray().Select(value => value.GetString()));
        Assert.Single((await GetJson("/v1/plugin/menu", TokenB)).GetProperty("items").EnumerateArray());
    }
}
