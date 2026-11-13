using System.Net;
using System.Text.Json;
using Chmonos.Core.Models;

namespace Chmonos.Api.Tests.Robustness;

/// <summary>
/// 許してから受け取るまでの間に人が設定で権限を外したら、古い許しの結果で権限を戻さないこと（Codex の点検 2026-11-13 の A1）。
/// 使った印の書き込みは人の変更ではないので、許しを失効させない
/// </summary>
public sealed class StaleApprovalTests : LocalApiFixture
{
    /// <summary>ソフト a が同じ名前で求め直し、人が求めた物をそのまま許す。窓を出した（行を控えた）後に返るので、続く書き換えは許しより後の人の操作になる。</summary>
    private async Task<string> AskAgainAsync()
    {
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Approvals.AnswerClient = request =>
        {
            asked.TrySetResult();
            return Task.FromResult(new ApiClientDecision(true, request.Scopes));
        };

        var body = JsonSerializer.Serialize(new { name = "作り物の道具a", scopes = ApiScopes.Watches });
        using var response = await Client.SendAsync(Request(HttpMethod.Post, "/v1/clients/request", body, token: null));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        return document.RootElement.GetProperty("requestId").GetString()!;
    }

    private async Task<HttpResponseMessage> CollectAsync(string requestId)
        => await Client.SendAsync(Request(HttpMethod.Post, "/v1/clients/request/result", JsonSerializer.Serialize(new { requestId, wait = 5 }), token: null));

    [Fact]
    public async Task 許した後_受け取る前に設定で権限を外すと_古い許しは渡さず_外した権限は戻らない()
    {
        var requestId = await AskAgainAsync();

        // 設定の画面と同じく、行の ID はそのまま権限だけを書き換える
        await ReplaceClients(Record("a", TokenA) with { Scopes = [ApiScopes.WatchSelectionChanged] }, Record("b", TokenB));

        using var response = await CollectAsync(requestId);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var saved = Store.ApiClients.Load().Clients.Single(client => client.Id == "a");
        Assert.Equal([ApiScopes.WatchSelectionChanged], saved.Scopes);

        // 行は置き換わっていないので、前のトークンはそのまま使える
        using var me = await Client.SendAsync(Request(HttpMethod.Get, "/v1/clients/me"));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task 許した後_受け取る前に使った日時だけが書かれても_許しは渡す()
    {
        var requestId = await AskAgainAsync();
        await ReplaceClients(Record("a", TokenA) with { LastUsedAt = Now.AddMinutes(30) }, Record("b", TokenB));

        using var response = await CollectAsync(requestId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("approved", document.RootElement.GetProperty("state").GetString());
        Assert.False(string.IsNullOrEmpty(document.RootElement.GetProperty("token").GetString()));
    }
}
