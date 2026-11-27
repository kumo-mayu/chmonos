using System.Net;
using System.Text.Json;
using Chmonos.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Chmonos.Api.Tests.Robustness;

/// <summary>
/// トークンの比べと、書き込みの口ごとのスコープの割り当て（2026-11-28 の点検で、壊しても落ちなかった所。docs/feedback/review-2026-11-28.md）。
/// トークン・ID は作り物
/// </summary>
public sealed class TokenAndScopeTests : LocalApiFixture
{
    /// <summary>
    /// 本物から少しだけ外した値。違うトークンの試験が "wrong-token" だけだと、前方一致・後ろの付け足し・大文字小文字を
    /// 無視する比べに変えても通ってしまう。古い1本（固定の長さで比べる）とソフトごと（ハッシュで引く）の両方に当てる
    /// </summary>
    public static TheoryData<string, string> NearMisses()
    {
        var data = new TheoryData<string, string>();
        foreach (var token in new[] { LegacyToken, TokenA })
        {
            data.Add(token, "Bearer " + token[..8]);
            data.Add(token, "Bearer " + token[..^1]);
            data.Add(token, "Bearer " + token + "x");
            data.Add(token, "Bearer " + token + token);
            data.Add(token, "bearer " + token);
            data.Add(token, "BEARER " + token);
            data.Add(token, "Bearer  " + token);
            data.Add(token, "Bearer\t" + token);
            data.Add(token, "Bearer " + token.ToUpperInvariant());
            data.Add(token, "Bearer " + token[..10] + " " + token[10..]);
            // 値の前後の空白は入れない：HTTP の決まり（RFC 9110 の OWS）で、本体に届く前に Kestrel が削り、本物と同じ値になる（実測 2026-11-28）。
            // 空白の確かめは、間の空白（2つ・タブ・トークンの途中）で行う
            data.Add(token, token);
            data.Add(token, "Bearer");
            data.Add(token, "Basic " + token);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(NearMisses))]
    public async Task 本物に近いだけのトークンは断る(string real, string authorization)
    {
        // 空回りしないよう、同じ口が本物のトークンでは答える事も見る
        using (var good = new HttpRequestMessage(HttpMethod.Get, "/v1/items"))
        {
            good.Headers.TryAddWithoutValidation("Authorization", "Bearer " + real);
            using var answer = await Client.SendAsync(good);
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/items");
        Assert.True(request.Headers.TryAddWithoutValidation("Authorization", authorization));
        using var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
    }

    /// <summary>
    /// 書き込みの口ごとに要るスコープ（docs/spec/local-api.md の書き込みの種類）。本体の割り当てとは別に手で書いた表で、
    /// 割り当てを取り違える（Unity の口を marks の許しで通すなど）と、下の試験が落ちる。拡張データの口は道に /extensions/ を含む物で全部 extensions
    /// </summary>
    private static readonly Dictionary<string, string> ScopeOf = new(StringComparer.Ordinal)
    {
        ["PUT /v1/items/{id}/favorite"] = ApiScopes.Marks,
        ["PUT /v1/items/{id}/hidden"] = ApiScopes.Marks,
        ["POST /v1/items/{id}/recent"] = ApiScopes.Recent,
        ["PUT /v1/items/{id}/user-tags"] = ApiScopes.Tags,
        ["POST /v1/items/{id}/user-tags"] = ApiScopes.Tags,
        ["POST /v1/items/{id}/user-tags/remove"] = ApiScopes.Tags,
        ["PATCH /v1/items/{id}/attributes"] = ApiScopes.Tags,
        ["PUT /v1/items/{id}/memo"] = ApiScopes.Tags,
        ["POST /v1/user-tags"] = ApiScopes.Tags,
        ["POST /v1/attributes"] = ApiScopes.Tags,
        ["POST /v1/items/{id}/show"] = ApiScopes.Open,
        ["POST /v1/items/{id}/reveal"] = ApiScopes.Open,
        ["PUT /v1/notifications/{id}/read"] = ApiScopes.Notifications,
        ["POST /v1/notifications/read"] = ApiScopes.Notifications,
        ["POST /v1/notifications/resolve"] = ApiScopes.Notifications,
        ["POST /v1/items/{id}/avatars"] = ApiScopes.Avatars,
        ["POST /v1/items/{id}/avatars/remove"] = ApiScopes.Avatars,
        ["POST /v1/items/{id}/avatars/confirm"] = ApiScopes.Avatars,
        ["POST /v1/items/{id}/avatar-bases"] = ApiScopes.Avatars,
        ["POST /v1/items/{id}/avatar-bases/remove"] = ApiScopes.Avatars,
        ["POST /v1/modifications"] = ApiScopes.Modifications,
        ["PUT /v1/modifications/{id}/memo"] = ApiScopes.Modifications,
        ["PUT /v1/modifications/{id}/blueprint-id"] = ApiScopes.Modifications,
        ["PUT /v1/modifications/{id}/unity-project"] = ApiScopes.Modifications,
        ["POST /v1/modifications/{id}/members"] = ApiScopes.Modifications,
        ["POST /v1/modifications/{id}/members/detach"] = ApiScopes.Modifications,
        ["POST /v1/modifications/{id}/members/restore"] = ApiScopes.Modifications,
        ["POST /v1/items/{id}/send-to-unity"] = ApiScopes.Unity,
        ["POST /v1/modifications/{id}/send-to-unity"] = ApiScopes.Unity,
        ["POST /v1/unity/sends/{sendId}/cancel"] = ApiScopes.Unity,
    };

    /// <summary>
    /// GET 以外でも書き込みのスコープを求めない口（読むだけの問い合わせ・許しを求める口・プラグインの口）。
    /// プラグインの口は見張りのスコープとメニューの許しで守る（EventAuthorizationTests・PluginMenuTests）
    /// </summary>
    private static readonly HashSet<string> NoWriteScope = new(StringComparer.Ordinal)
    {
        "POST /v1/items/batch", "POST /v1/search", "POST /v1/search/facets",
        "POST /v1/unitypackages/lookup", "POST /v1/files/lookup", "POST /v1/files/locations", "POST /v1/zip-images/lookup",
        "POST " + ApiClientEndpoints.RequestPath, "POST " + ApiClientEndpoints.ResultPath,
        "PUT " + PluginEndpoints.SubscriptionsPath, "PUT " + PluginEndpoints.MenuPath, "POST " + PluginEndpoints.ResultPath,
    };

    /// <summary>Guard より前に本文を読む口・本文を確かめる口に渡す、形だけ合った本文</summary>
    private static string BodyOf(string route) => route switch
    {
        "POST /v1/items/{id}/avatars/confirm" => """{"avatarIds":["7000001"]}""",
        "POST /v1/items/{id}/recent" => """{"kind":"used"}""",
        "POST /v1/items/{id}/user-tags/remove" or "POST /v1/user-tags" => """{"top":"作り物"}""",
        "POST /v1/attributes" => """{"name":"作り物"}""",
        "POST /v1/notifications/read" or "POST /v1/notifications/resolve" => """{"ids":["n1"]}""",
        _ => "{}",
    };

    private IReadOnlyList<(string Key, string Method, string Pattern)> Routes()
    {
        var routes = new List<(string, string, string)>();
        foreach (var endpoint in Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var pattern = endpoint.RoutePattern.RawText ?? string.Empty;
            foreach (var method in endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
            {
                routes.Add((method + " " + pattern, method, pattern));
            }
        }

        return routes;
    }

    private static string Fill(string pattern) => pattern
        .Replace("{id}", "9600901").Replace("{key}", "9600901").Replace("{ns}", "tool-a").Replace("{sendId}", "send-1");

    private static string? ExpectedScope(string key)
        => ScopeOf.TryGetValue(key, out var scope) ? scope : key.Contains("/extensions/", StringComparison.Ordinal) ? ApiScopes.Extensions : null;

    [Fact]
    public void 書き込みの口は全部_スコープの表かスコープの要らない口の一覧に載っている()
    {
        // 新しい書き込みの口を足したら、ここに要るスコープを書く（書かずに足すと割り当ての確かめから漏れる）
        var writes = Routes().Where(route => route.Method != HttpMethods.Get).Select(route => route.Key).Distinct().ToList();
        var unlisted = writes.Where(key => ExpectedScope(key) is null && !NoWriteScope.Contains(key)).ToList();
        Assert.True(unlisted.Count == 0, string.Join(Environment.NewLine, unlisted));

        // 表の側の消し忘れ（道を変えたのに表が古いまま）も拾う
        var stale = ScopeOf.Keys.Concat(NoWriteScope).Where(key => !writes.Contains(key)).ToList();
        Assert.True(stale.Count == 0, string.Join(Environment.NewLine, stale));
        Assert.Contains(writes, key => key.Contains("/extensions/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 書き込みの口は_その口のスコープだけを外したソフトを断り_そのスコープだけのソフトは断らない()
    {
        var rows = Routes().Where(route => route.Method != HttpMethods.Get && ExpectedScope(route.Key) is not null).ToList();
        var wrong = new List<string>();
        foreach (var scope in ApiScopes.Writes)
        {
            var minusToken = "minus-" + scope + "-0123456789";
            var onlyToken = "only-" + scope + "-0123456789";
            await ReplaceClients(
                Record("minus", minusToken) with { Scopes = [.. ApiScopes.All.Where(granted => granted != scope)] },
                Record("only", onlyToken) with { Scopes = [scope] });

            foreach (var (key, method, pattern) in rows.Where(row => ExpectedScope(row.Key) == scope))
            {
                var (minusStatus, minusCode) = await SendAsync(method, pattern, BodyOf(key), minusToken);
                if (minusStatus != HttpStatusCode.Forbidden || minusCode != "scopeNotGranted")
                {
                    wrong.Add($"{key}：{scope} を外しても {(int)minusStatus} {minusCode}");
                }

                var (onlyStatus, onlyCode) = await SendAsync(method, pattern, BodyOf(key), onlyToken);
                if (onlyCode == "scopeNotGranted")
                {
                    wrong.Add($"{key}：{scope} だけでは {(int)onlyStatus} {onlyCode}");
                }
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    private async Task<(HttpStatusCode Status, string? Code)> SendAsync(string method, string pattern, string body, string token)
    {
        using var request = Request(new HttpMethod(method), Fill(pattern), body, token);
        using var response = await Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        string? code = null;
        if (response.Content.Headers.ContentType?.MediaType == "application/problem+json")
        {
            code = JsonDocument.Parse(text).RootElement.GetProperty("code").GetString();
        }

        return (response.StatusCode, code);
    }
}
