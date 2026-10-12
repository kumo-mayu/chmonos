using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Microsoft.AspNetCore.Builder;

namespace Chmonos.Api.Tests;

/// <summary>
/// API を本当に待ち受けさせて HTTP で叩く（守り・口・誤りの答え）。保存先は作り物の写し（一時フォルダ）、ポートは空いている番号。
/// 名前・ID は作り物。
/// </summary>
public sealed class ApiHostTests : IAsyncLifetime
{
    private const string Token = "test-token-0123456789abcdef";
    private const string Origin = "http://localhost:5173";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-api-tests", Guid.NewGuid().ToString("N"));
    private WebApplication? _app;
    private HttpClient _client = null!;
    private int _port;

    public async Task InitializeAsync()
    {
        var store = new DataStore(new AppPaths(_root));
        await store.Items.SaveAsync(new ItemRecord
        {
            Id = "9600001",
            Booth = new BoothBlock { Name = "作り物の夏服", Tags = ["夏"] },
            Local = new LocalBlock { LocalFiles = [new LocalFileRecord { Hash = new string('A', 64), Paths = [@"D:\files\summer.zip"], SizeBytes = 1 }] },
        });
        await store.Items.SaveAsync(new ItemRecord { Id = "9600002", Booth = new BoothBlock { Name = "作り物の冬服" } });

        var images = Path.Combine(_root, "images", "9600001");
        Directory.CreateDirectory(images);
        await File.WriteAllBytesAsync(Path.Combine(images, "0011223344.webp"), [1, 2, 3]);

        (_app, _, _) = await ApiHost.BuildAsync(new ApiHostOptions(_root, 0, Token, [Origin]));
        await _app.StartAsync();
        _port = new Uri(_app.Urls.Single()).Port;
        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_port}") };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string path, bool token = true, string? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, (await JsonOf(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task 動いているかとバージョンは_合言葉なしで答える()
    {
        var health = await JsonOf(await _client.SendAsync(Request(HttpMethod.Get, "/v1/health", token: false)));
        Assert.Equal("ok", health.GetProperty("status").GetString());
        Assert.Equal(2, health.GetProperty("itemCount").GetInt32());

        var version = await JsonOf(await _client.SendAsync(Request(HttpMethod.Get, "/v1/version", token: false)));
        Assert.Equal("v1", version.GetProperty("api").GetString());
        Assert.Contains("他の人に渡したり", version.GetProperty("notice").GetString());
    }

    [Fact]
    public async Task 仕様書は合言葉なしで返し_URLと答えの型が載っている()
    {
        var response = await _client.SendAsync(Request(HttpMethod.Get, "/v1/openapi.json", token: false));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await JsonOf(response);

        Assert.Equal("Chmonos API", document.GetProperty("info").GetProperty("title").GetString());
        var paths = document.GetProperty("paths");
        foreach (var path in new[] { "/v1/items/{id}", "/v1/search", "/v1/search/facets", "/v1/files/lookup", "/v1/changes", "/v1/events" })
        {
            Assert.True(paths.TryGetProperty(path, out _), path);
        }

        // アプリからの知らせの口は外の道具向けではないので載せない
        Assert.False(paths.TryGetProperty("/internal/busy", out _));
        // 合言葉の要る URL にだけ、Bearer の印を付ける
        Assert.Equal("bearer", document.GetProperty("components").GetProperty("securitySchemes").GetProperty("bearer").GetProperty("scheme").GetString());
        Assert.True(paths.GetProperty("/v1/items/{id}").GetProperty("get").TryGetProperty("security", out _));
        Assert.False(paths.GetProperty("/v1/health").GetProperty("get").TryGetProperty("security", out _));

        var schemas = document.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("ItemDetail", out var detail));
        Assert.True(detail.GetProperty("properties").TryGetProperty("compatibleAvatars", out _));
        Assert.True(schemas.TryGetProperty("SearchRequest", out _));
    }

    [Fact]
    public async Task 答えにETagを付け_変わっていなければ304を返す()
    {
        var first = await _client.SendAsync(Request(HttpMethod.Get, "/v1/items/9600001"));
        var tag = first.Headers.ETag;
        Assert.NotNull(tag);

        var again = Request(HttpMethod.Get, "/v1/items/9600001");
        again.Headers.IfNoneMatch.Add(tag);
        var unchanged = await _client.SendAsync(again);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        Assert.Empty(await unchanged.Content.ReadAsByteArrayAsync());

        // 頼む物（include）が違えば中身が違うので、印も違う
        var included = await _client.SendAsync(Request(HttpMethod.Get, "/v1/items/9600001?include=avatarLinks"));
        Assert.NotEqual(tag, included.Headers.ETag);
    }

    [Fact]
    public async Task 合言葉が無い_違うときは断る()
    {
        var none = await _client.SendAsync(Request(HttpMethod.Get, "/v1/items", token: false));
        await AssertProblem(none, HttpStatusCode.Unauthorized, "unauthorized");
        Assert.Equal("Bearer", none.Headers.WwwAuthenticate.Single().Scheme);

        var wrong = Request(HttpMethod.Get, "/v1/items", token: false);
        wrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");
        await AssertProblem(await _client.SendAsync(wrong), HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task Hostが自分でなければ断る()
    {
        // 他所のドメインを 127.0.0.1 へ向け直す手口（DNS rebinding）は、Host が他所のまま届く
        var request = Request(HttpMethod.Get, "/v1/health", token: false);
        request.Headers.Host = $"evil.example:{_port}";
        await AssertProblem(await _client.SendAsync(request), HttpStatusCode.BadRequest, "invalidHost");

        var localhost = Request(HttpMethod.Get, "/v1/health", token: false);
        localhost.Headers.Host = $"localhost:{_port}";
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(localhost)).StatusCode);
    }

    [Fact]
    public async Task 許した住所のページにだけCORSの許可を出す()
    {
        var preflight = Request(HttpMethod.Options, "/v1/items", token: false);
        preflight.Headers.Add("Origin", Origin);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization");
        var answer = await _client.SendAsync(preflight);
        Assert.Equal(HttpStatusCode.NoContent, answer.StatusCode);
        Assert.Equal(Origin, answer.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("Authorization", answer.Headers.GetValues("Access-Control-Allow-Headers").Single());

        var allowed = Request(HttpMethod.Get, "/v1/items");
        allowed.Headers.Add("Origin", Origin);
        var page = await _client.SendAsync(allowed);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(Origin, page.Headers.GetValues("Access-Control-Allow-Origin").Single());

        var other = Request(HttpMethod.Get, "/v1/health", token: false);
        other.Headers.Add("Origin", "https://evil.example");
        await AssertProblem(await _client.SendAsync(other), HttpStatusCode.Forbidden, "originNotAllowed");
    }

    [Fact]
    public async Task 商品と検索を答える()
    {
        var item = await JsonOf(await _client.SendAsync(Request(HttpMethod.Get, "/v1/items/9600001")));
        Assert.Equal("作り物の夏服", item.GetProperty("name").GetString());
        Assert.False(item.TryGetProperty("avatarLinks", out _));

        var included = await JsonOf(await _client.SendAsync(Request(HttpMethod.Get, "/v1/items/9600001?include=avatarLinks")));
        Assert.Equal(JsonValueKind.Array, included.GetProperty("avatarLinks").GetProperty("avatars").ValueKind);

        var search = await JsonOf(await _client.SendAsync(Request(HttpMethod.Post, "/v1/search", body: """{"where":{"text":"夏"}}""")));
        Assert.Equal(["9600001"], search.GetProperty("items").EnumerateArray().Select(entry => entry.GetProperty("id").GetString()));

        var list = await JsonOf(await _client.SendAsync(Request(HttpMethod.Get, "/v1/items?sort=id:desc&limit=1")));
        Assert.Equal("9600002", list.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.String, list.GetProperty("nextCursor").ValueKind);

        var batch = await JsonOf(await _client.SendAsync(Request(HttpMethod.Post, "/v1/items/batch", body: """{"ids":["9600002","9999999"]}""")));
        Assert.Equal("9600002", batch.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal("9999999", batch.GetProperty("missing")[0].GetString());

        var lookup = await JsonOf(await _client.SendAsync(Request(HttpMethod.Get, "/v1/files/lookup?path=" + Uri.EscapeDataString(@"d:\FILES\summer.zip"))));
        Assert.Equal("9600001", lookup[0].GetProperty("itemId").GetString());
    }

    [Fact]
    public async Task 読めない問い合わせは_決まった名前の誤りで返す()
    {
        await AssertProblem(await _client.SendAsync(Request(HttpMethod.Get, "/v1/items/9999999")), HttpStatusCode.NotFound, "itemNotFound");
        await AssertProblem(await _client.SendAsync(Request(HttpMethod.Get, "/v1/items/9600001?include=avatars")), HttpStatusCode.BadRequest, "unknownInclude");
        await AssertProblem(await _client.SendAsync(Request(HttpMethod.Post, "/v1/search", body: "{ 読めない")), HttpStatusCode.BadRequest, "invalidJson");
        await AssertProblem(await _client.SendAsync(Request(HttpMethod.Post, "/v1/search", body: """{"where":{"condition":{"kind":"無い"}}}""")), HttpStatusCode.BadRequest, "unknownCondition");
        await AssertProblem(await _client.SendAsync(Request(HttpMethod.Get, "/v1/items?limit=999")), HttpStatusCode.BadRequest, "invalidLimit");
    }

    [Fact]
    public async Task 画像は写しが知っている名前だけ返す()
    {
        var image = await _client.SendAsync(Request(HttpMethod.Get, "/v1/images/9600001/0011223344.webp"));
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/webp", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal([1, 2, 3], await image.Content.ReadAsByteArrayAsync());

        // 保存先の外を組ませない（名前で場所を組まず、写しの一覧から選ぶ）
        await AssertProblem(await _client.SendAsync(Request(HttpMethod.Get, "/v1/images/9600001/..%5C..%5Csettings.json")), HttpStatusCode.NotFound, "imageNotFound");
        await AssertProblem(await _client.SendAsync(Request(HttpMethod.Get, "/v1/images/9600001/settings.json")), HttpStatusCode.NotFound, "imageNotFound");
    }
}
