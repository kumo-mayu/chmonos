using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Chmonos.Api.Tests.Robustness;

/// <summary>
/// Host の確かめ（DNS rebinding）と、トークンの要らない口が決めた物だけである事（2026-11-28 の点検で、壊しても落ちなかった所）。
/// ドメインは作り物
/// </summary>
public sealed class HostAndOpenPathTests : LocalApiFixture
{
    /// <summary>
    /// 自分に見えるが自分でない Host。前方一致（localhost.evil.example）やポートの比べ外しに変えても、
    /// evil.example だけの試験では気付けなかった。{port} は待っているポート、{other} は違うポートに置き換える
    /// </summary>
    [Theory]
    [InlineData("localhost.evil.example:{port}")]
    [InlineData("127.0.0.1.evil.example:{port}")]
    [InlineData("localhost:{other}")]
    [InlineData("127.0.0.1:{other}")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    public async Task 自分に似たHostも断る(string host)
    {
        var other = Port == 65535 ? Port - 1 : Port + 1;
        using (var good = Request(HttpMethod.Get, "/v1/health", token: null))
        {
            good.Headers.Host = $"localhost:{Port}";
            using var answer = await Client.SendAsync(good);
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        }

        using var request = Request(HttpMethod.Get, "/v1/health", token: null);
        request.Headers.Host = host.Replace("{port}", Port.ToString()).Replace("{other}", other.ToString());
        using var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalidHost", JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
    }

    /// <summary>
    /// トークン無しで答える口は、docs/spec/local-api.md の5本だけ。本体の OpenPaths とは別に手で書いた一覧と、
    /// 道の一覧（EndpointDataSource）を照らすので、OpenPaths に口を足すと落ちる
    /// </summary>
    private static readonly HashSet<string> Open = new(StringComparer.Ordinal)
    {
        "/v1/health", "/v1/version", "/v1/openapi.json", "/v1/clients/request", "/v1/clients/request/result",
    };

    [Fact]
    public async Task トークンの要らない口は決めた5本だけで_ほかは全部トークン無しを断る()
    {
        var routes = Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [HttpMethods.Get])
                .Select(method => (Method: method, Pattern: endpoint.RoutePattern.RawText ?? string.Empty)))
            .Distinct().ToList();
        Assert.True(routes.Count > 30, $"道の一覧が引けていない（{routes.Count} 本）");
        Assert.All(Open, path => Assert.Contains(routes, route => route.Pattern == path));

        var wrong = new List<string>();
        foreach (var (method, pattern) in routes)
        {
            var path = System.Text.RegularExpressions.Regex.Replace(pattern, "{[^}]+}", "1");
            using var request = Request(new HttpMethod(method), path, method == HttpMethods.Get ? null : "{}", token: null);

            // 見張り（SSE）の口が開いたままにならないよう、頭だけ読んで期限を付ける
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).WaitAsync(TimeSpan.FromSeconds(10));
            var unauthorized = response.StatusCode == HttpStatusCode.Unauthorized;
            if (unauthorized == Open.Contains(pattern))
            {
                wrong.Add($"{method} {pattern}：{(int)response.StatusCode}");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
}
