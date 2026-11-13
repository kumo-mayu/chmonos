using System.Net;
using System.Text.Json;

namespace Chmonos.Api.Tests.Robustness;

/// <summary>入口の断り：Host・Origin の偽り、壊れた・多すぎる・巨大な宣言が、今の許しと控えの宣言を変えないこと。</summary>
public sealed class RequestBoundaryTests : LocalApiFixture
{
    [Theory]
    [InlineData("host")]
    [InlineData("origin")]
    [InlineData("multiple-origins")]
    [InlineData("forwarded")]
    public async Task 許可要求も_HostとOriginの偽装を拒む(string attack)
    {
        using var request = Request(HttpMethod.Post, "/v1/clients/request", """{"name":"試験の道具","scopes":[]}""", token: null);
        if (attack == "host") request.Headers.Host = "evil.example:" + Client.BaseAddress!.Port;
        else if (attack == "multiple-origins") request.Headers.TryAddWithoutValidation("Origin", new[] { AllowedOrigin, "https://evil.example" });
        else
        {
            request.Headers.Add("Origin", "https://evil.example");
            if (attack == "forwarded")
            {
                request.Headers.Add("X-Forwarded-Host", "127.0.0.1:" + Client.BaseAddress!.Port);
                request.Headers.Add("X-Forwarded-For", "127.0.0.1");
            }
        }
        using var response = await Client.SendAsync(request);
        Assert.Equal(attack == "host" ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"items\":[null]}")]
    [InlineData("{\"items\":[{\"id\":\"x\",\"label\":\"X\",\"where\":[\"item\"],\"when\":{\"futureRestriction\":true}}]}")]
    public async Task 壊れた宣言は_現行の許可を変えない(string body)
    {
        using var response = await Client.SendAsync(Request(HttpMethod.Put, "/v1/plugin/menu", body));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("preview", (await GetJson("/v1/plugin/menu")).GetProperty("items")[0].GetProperty("id").GetString());
    }

    [Theory]
    [InlineData(17, 0)]
    [InlineData(1, 32)]
    public async Task 宣言の段ごとの数と総数の上限は_許しを求める前に断る(int topCount, int childCount)
    {
        object Leaf(int i) => new { id = "x" + i, label = "確認", where = new[] { "item" } };
        object[] items = childCount == 0
            ? Enumerable.Range(0, topCount).Select(Leaf).ToArray()
            : [new { id = "parent", label = "親", children = Enumerable.Range(0, childCount).Select(Leaf).ToArray() }];
        using var response = await Client.SendAsync(Request(HttpMethod.Put, "/v1/plugin/menu", JsonSerializer.Serialize(new { items })));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("tooManyItems", document.RootElement.GetProperty("code").GetString());
        Assert.Equal("preview", (await GetJson("/v1/plugin/menu")).GetProperty("items")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task 巨大な宣言本文は_許可と保存済みメニューを変えない()
    {
        // Kestrelの既定上限30,000,000バイトより少し大きい、内容を持たない空白。
        var body = new string(' ', 30_000_001) + "{\"items\":[]}";
        try
        {
            using var response = await Client.SendAsync(Request(HttpMethod.Put, "/v1/plugin/menu", body));
            Assert.True((int)response.StatusCode is >= 400 and < 500, $"HTTP {(int)response.StatusCode}");
        }
        catch (HttpRequestException exception) when (
            exception.InnerException is IOException { InnerException: System.Net.Sockets.SocketException socket }
            && socket.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionReset)
        {
            // 本文を読まずに接続を閉じる拒否も認める。直後のGETで生存と保存内容を確かめる。
        }
        Assert.Equal("preview", (await GetJson("/v1/plugin/menu")).GetProperty("items")[0].GetProperty("id").GetString());
    }
}
