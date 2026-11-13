using System.Net;
using System.Text.Json;

namespace Chmonos.Api.Tests.Robustness;

/// <summary>項目の宣言の聞き直しが、ほかのソフトへ見えず、拒否・取り消しの後に宣言やソフトを戻さないこと。</summary>
public sealed class MenuReapprovalTests : LocalApiFixture
{
    private const string Changed = """{"items":[{"id":"changed","label":"変更後","where":["item"],"needs":["files"]}]}""";

    [Fact]
    public async Task 別ソフトの許可待ち宣言は_自分のGETに現れず_拒否なら旧宣言が残る()
    {
        using var response = await Client.SendAsync(Request(HttpMethod.Put, "/v1/plugin/menu", Changed));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var decision = await Approvals.Next();
        var mine = await GetJson("/v1/plugin/menu");
        Assert.Equal("preview", mine.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal("changed", mine.GetProperty("pending")[0].GetProperty("id").GetString());
        var other = await GetJson("/v1/plugin/menu", TokenB);
        Assert.Equal(JsonValueKind.Null, other.GetProperty("pending").ValueKind);
        decision.SetResult(ApiClientDecision.Refusal);
        for (var i = 0; i < 200; i++)
        {
            mine = await GetJson("/v1/plugin/menu");
            if (mine.GetProperty("state").GetString() == "denied") break;
            await Task.Delay(10);
        }
        Assert.Equal("denied", mine.GetProperty("state").GetString());
        Assert.Equal("preview", mine.GetProperty("items")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task 宣言を聞いている間の取消後に許可しても_ソフトも宣言も復活しない()
    {
        using var response = await Client.SendAsync(Request(HttpMethod.Put, "/v1/plugin/menu", Changed));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var decision = await Approvals.Next();
        await ReplaceClients(Record("b", TokenB));
        decision.SetResult(new ApiClientDecision(true, []));
        for (var i = 0; i < 200 && Requests.MenuStateOf("a").Pending is not null; i++) await Task.Delay(10);
        Assert.Null(Requests.MenuStateOf("a").Pending);
        Assert.DoesNotContain(Store.ApiClients.Load().Clients, client => client.Id == "a");
        Assert.Empty(State.Clients.MenuOf("a"));
        using var revoked = await Client.SendAsync(Request(HttpMethod.Get, "/v1/plugin/menu"));
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }
}
