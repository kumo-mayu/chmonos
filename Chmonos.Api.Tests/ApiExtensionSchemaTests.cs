using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Microsoft.AspNetCore.Builder;

namespace Chmonos.Api.Tests;

/// <summary>
/// 外のソフトの情報の宣言（docs/spec/local-api-extensions.md）：名前空間・書き込みの種類 extensions・宣言の確かめ・宣言を変えた時・変化の知らせ。
/// 命令の口は本物の <see cref="CommandHandler"/>（錠の中で今の宣言に当てる所まで通す）。ソフトの名前・名前空間は作り物
/// </summary>
public sealed class ApiExtensionSchemaTests : IAsyncLifetime
{
    private const string Legacy = "extension-legacy-token-0123456789";
    private const string TokenA = "extension-token-a-0123456789";
    private const string TokenB = "extension-token-b-0123456789";
    private const string TokenRead = "extension-token-r-0123456789";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-api-tests", Guid.NewGuid().ToString("N"));
    private WebApplication? _app;
    private DataStore _store = null!;
    private HttpClient _client = null!;

    private static ApiClientRecord Client(string id, string token, string? ns, params string[] scopes) => new()
    {
        Id = id, Name = "作り物の道具" + id, TokenHash = ApiClientBook.HashOf(token), Scopes = scopes, Namespace = ns,
        GrantedAt = DateTimeOffset.UnixEpoch,
    };

    public async Task InitializeAsync()
    {
        _store = new DataStore(new AppPaths(_root));
        JsonStore.Write(_store.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, AllowWrites = true });
        JsonStore.Write(_store.Paths.ApiClientsFile, new ApiClientList
        {
            Clients =
            [
                Client("a", TokenA, "outfit-checker", ApiScopes.Extensions),
                Client("b", TokenB, "other-tool", ApiScopes.Extensions),
                Client("r", TokenRead, "reader-tool"),
            ],
        });
        var handler = new CommandHandler(null!, null!) { Extensions = _store.Extensions };
        var hooks = new ApiWriteHooks((command, token) => handler.ExecuteAsync(command, cancellationToken: token));
        (_app, _, _) = await ApiHost.BuildAsync(new ApiHostOptions(_root, 0, Legacy, [], Watch: false, Store: _store, Writes: hooks));
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{new Uri(_app.Urls.Single()).Port}") };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private const string Schema = """
        { "label": "試着チェッカー",
          "fields": [
            { "key": "testedOn",  "label": "試着したアバター", "type": "avatarList" },
            { "key": "polyCount", "label": "ポリゴン数", "type": "number", "unit": "ポリゴン", "min": 0, "scale": "log", "search": true },
            { "key": "checkedAt", "label": "調べた日", "type": "date" },
            { "key": "fitsWell",  "label": "よく合う", "type": "bool", "editable": true, "search": true, "group": "合うか" },
            { "key": "results",   "label": "試着の結果", "type": "table",
              "columns": [ { "key": "avatar", "label": "アバター", "type": "avatarList" }, { "key": "note", "label": "メモ", "type": "text" } ] },
            { "key": "license",   "label": "利用規約", "type": "choice", "options": [ { "value": "ok", "label": "使える" }, { "value": "ng", "label": "使えない" } ] } ] }
        """;

    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpMethod method, string path, string? body = null, string token = TokenA)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    [Fact]
    public async Task 持ち主が宣言を置くと_ファイルに書かれ_どのソフトも読める()
    {
        var (status, body) = await SendAsync(HttpMethod.Put, "/v1/extensions/Outfit-Checker/schema", Schema);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("outfit-checker", body.GetProperty("namespace").GetString());
        Assert.Equal("作り物の道具a", body.GetProperty("software").GetString());
        Assert.Equal(6, body.GetProperty("fields").GetArrayLength());
        Assert.True(File.Exists(Path.Combine(_root, "extensions", "outfit-checker", "schema.json")));

        // 偽の印はファイルに書かない（既定と同じ。人が読む時に余計な行を出さない）
        var file = File.ReadAllText(Path.Combine(_root, "extensions", "outfit-checker", "schema.json"));
        Assert.DoesNotContain("\"search\": false", file, StringComparison.Ordinal);
        Assert.Contains("試着チェッカー", file, StringComparison.Ordinal);

        foreach (var token in new[] { TokenB, TokenRead, Legacy })
        {
            var (listStatus, list) = await SendAsync(HttpMethod.Get, "/v1/extensions", token: token);
            Assert.Equal(HttpStatusCode.OK, listStatus);
            var entry = Assert.Single(list.GetProperty("extensions").EnumerateArray());
            Assert.Equal("試着チェッカー", entry.GetProperty("label").GetString());
            Assert.False(entry.GetProperty("mine").GetBoolean());
        }

        var (_, one) = await SendAsync(HttpMethod.Get, "/v1/extensions/outfit-checker", token: TokenB);
        Assert.Equal("log", one.GetProperty("fields")[1].GetProperty("scale").GetString());
        Assert.Equal(JsonValueKind.Null, one.GetProperty("hiddenFields").ValueKind);
    }

    [Fact]
    public async Task 持ち主でないソフト_古いトークン_extensions_の無いソフトは書けない()
    {
        var (other, otherBody) = await SendAsync(HttpMethod.Put, "/v1/extensions/outfit-checker/schema", Schema, TokenB);
        Assert.Equal((HttpStatusCode.Forbidden, "notOwner"), (other, Code(otherBody)));

        var (legacy, legacyBody) = await SendAsync(HttpMethod.Put, "/v1/extensions/outfit-checker/schema", Schema, Legacy);
        Assert.Equal((HttpStatusCode.Forbidden, "scopeNotGranted"), (legacy, Code(legacyBody)));

        var (reader, readerBody) = await SendAsync(HttpMethod.Put, "/v1/extensions/reader-tool/schema", Schema, TokenRead);
        Assert.Equal((HttpStatusCode.Forbidden, "scopeNotGranted"), (reader, Code(readerBody)));

        Assert.False(Directory.Exists(Path.Combine(_root, "extensions")));

        // 古いトークンが名乗る権限にも extensions を入れない（書けない物を書けると言わない）
        var (_, me) = await SendAsync(HttpMethod.Get, "/v1/clients/me", token: Legacy);
        Assert.DoesNotContain("extensions", me.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()));
        var (_, mine) = await SendAsync(HttpMethod.Get, "/v1/clients/me");
        Assert.Equal("outfit-checker", mine.GetProperty("namespace").GetString());
    }

    [Theory]
    [InlineData("""{ "label": "x", "fields": [ { "key": "a", "label": "A", "type": "color" } ] }""", "invalidField")]
    [InlineData("""{ "label": "x", "fields": [ { "key": "1a", "label": "A", "type": "text" } ] }""", "invalidField")]
    [InlineData("""{ "label": "x", "fields": [ { "key": "a", "label": "A", "type": "text", "unit": "個" } ] }""", "invalidField")]
    [InlineData("""{ "label": "x", "fields": [ { "key": "a", "label": "A", "type": "number", "min": 5, "max": 1 } ] }""", "invalidField")]
    [InlineData("""{ "label": "x", "fields": [ { "key": "a", "label": "A", "type": "number", "min": -1, "scale": "log" } ] }""", "invalidScale")]
    [InlineData("""{ "label": "x", "fields": [ { "key": "a", "label": "A", "type": "number", "foldBelow": 100 } ] }""", "invalidScale")]
    [InlineData("""{ "label": "x", "fields": [ { "key": "a", "label": "A", "type": "number", "search": true, "group": "g" } ] }""", "invalidGroup")]
    [InlineData("""{ "label": "x", "fields": [ { "key": "a", "label": "A", "type": "bool", "group": "g" } ] }""", "invalidGroup")]
    [InlineData("""{ "label": "x", "fields": [ { "key": "a", "label": "A", "type": "choice" } ] }""", "invalidField")]
    [InlineData("""{ "label": "x", "fields": [ { "key": "a", "label": "A", "type": "choice", "options": [ { "value": "v", "label": "1" }, { "value": "v", "label": "2" } ] } ] }""", "invalidField")]
    [InlineData("""{ "label": "x", "fields": [ { "key": "t", "label": "T", "type": "table", "search": true, "columns": [ { "key": "c", "label": "C", "type": "text" } ] } ] }""", "invalidField")]
    [InlineData("""{ "label": "x", "fields": [ { "key": "t", "label": "T", "type": "table", "columns": [ { "key": "c", "label": "C", "type": "table" } ] } ] }""", "invalidField")]
    [InlineData("""{ "label": "x", "fields": [ { "key": "a", "label": "A", "type": "text" }, { "key": "A", "label": "B", "type": "text" } ] }""", "duplicateField")]
    [InlineData("""{ "fields": [] }""", "invalidSchema")]
    public async Task 合わない宣言は400で断り_書かない(string schema, string code)
    {
        var (status, body) = await SendAsync(HttpMethod.Put, "/v1/extensions/outfit-checker/schema", schema);

        Assert.Equal((HttpStatusCode.BadRequest, code), (status, Code(body)));
        Assert.False(File.Exists(Path.Combine(_root, "extensions", "outfit-checker", "schema.json")));
    }

    [Fact]
    public async Task 欄は65個から断る()
    {
        var fields = string.Join(",", Enumerable.Range(0, ExtensionLimits.Fields + 1).Select(i => $$"""{ "key": "f{{i}}", "label": "欄{{i}}", "type": "text" }"""));
        var (status, body) = await SendAsync(HttpMethod.Put, "/v1/extensions/outfit-checker/schema", $$"""{ "label": "x", "fields": [ {{fields}} ] }""");

        Assert.Equal((HttpStatusCode.BadRequest, "invalidSchema"), (status, Code(body)));
    }

    [Fact]
    public async Task 外した欄は隠して覚え_持ち主だけが読め_違う型で宣言し直すと409()
    {
        await SendAsync(HttpMethod.Put, "/v1/extensions/outfit-checker/schema", Schema);

        // polyCount を外し、表の列 note も外す
        var (status, written) = await SendAsync(HttpMethod.Put, "/v1/extensions/outfit-checker/schema", """
            { "label": "試着チェッカー2", "fields": [
                { "key": "results", "label": "試着の結果", "type": "table", "columns": [ { "key": "avatar", "label": "アバター", "type": "avatarList" } ] } ] }
            """);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("polyCount", written.GetProperty("hiddenFields").EnumerateArray().Select(field => field.GetProperty("key").GetString()));
        Assert.Equal("note", written.GetProperty("fields")[0].GetProperty("hiddenColumns")[0].GetProperty("key").GetString());

        var (_, other) = await SendAsync(HttpMethod.Get, "/v1/extensions/outfit-checker?include=hidden", token: TokenB);
        Assert.Equal(JsonValueKind.Null, other.GetProperty("hiddenFields").ValueKind);
        Assert.Equal(JsonValueKind.Null, other.GetProperty("fields")[0].GetProperty("hiddenColumns").ValueKind);

        var (_, own) = await SendAsync(HttpMethod.Get, "/v1/extensions/outfit-checker?include=hidden");
        Assert.Equal(5, own.GetProperty("hiddenFields").GetArrayLength());

        // 隠した欄を違う型で・表の隠した列を違う型で宣言し直すのは断る
        var (retype, retypeBody) = await SendAsync(HttpMethod.Put, "/v1/extensions/outfit-checker/schema",
            """{ "label": "x", "fields": [ { "key": "polyCount", "label": "ポリゴン数", "type": "text" } ] }""");
        Assert.Equal((HttpStatusCode.Conflict, "fieldTypeChanged"), (retype, Code(retypeBody)));
        var (column, columnBody) = await SendAsync(HttpMethod.Put, "/v1/extensions/outfit-checker/schema",
            """{ "label": "x", "fields": [ { "key": "results", "label": "R", "type": "table", "columns": [ { "key": "note", "label": "N", "type": "number" } ] } ] }""");
        Assert.Equal((HttpStatusCode.Conflict, "fieldTypeChanged"), (column, Code(columnBody)));

        // 同じ型で宣言し直せば、また出る（隠した方からは外れる）
        var (back, backBody) = await SendAsync(HttpMethod.Put, "/v1/extensions/outfit-checker/schema",
            """{ "label": "x", "fields": [ { "key": "polyCount", "label": "ポリゴン", "type": "number" } ] }""");
        Assert.Equal(HttpStatusCode.OK, back);
        Assert.DoesNotContain("polyCount", backBody.GetProperty("hiddenFields").EnumerateArray().Select(field => field.GetProperty("key").GetString()));
    }

    [Fact]
    public async Task 宣言を変えたら変化の知らせに流す()
    {
        await SendAsync(HttpMethod.Put, "/v1/extensions/outfit-checker/schema", Schema);

        var (_, changes) = await SendAsync(HttpMethod.Get, "/v1/changes?since=0", token: TokenB);
        var change = Assert.Single(changes.GetProperty("changes").EnumerateArray(), change => change.GetProperty("kind").GetString() == "extensions");
        Assert.Equal(["outfit-checker", "schema"], change.GetProperty("parts").EnumerateArray().Select(part => part.GetString()!).ToArray());
    }

    [Theory]
    [InlineData("""{ "name": "作り物の道具x", "scopes": ["extensions"] }""", HttpStatusCode.BadRequest, "namespaceRequired")]
    [InlineData("""{ "name": "作り物の道具x", "scopes": [], "namespace": "Bad_ns" }""", HttpStatusCode.BadRequest, "invalidNamespace")]
    [InlineData("""{ "name": "作り物の道具x", "scopes": ["extensions"], "namespace": "Outfit-Checker" }""", HttpStatusCode.Conflict, "namespaceTaken")]
    public async Task 許しを求める時の名前空間を確かめる(string body, HttpStatusCode expected, string code)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync("/v1/clients/request", content);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal((expected, code), (response.StatusCode, Code(problem)));
    }

    [Theory]
    [InlineData("nul")]
    [InlineData("chmonos-x")]
    [InlineData("ab")]
    [InlineData("1abc")]
    public async Task 決まりに合わない名前空間は400(string ns)
    {
        var (status, body) = await SendAsync(HttpMethod.Get, $"/v1/extensions/{ns}");

        Assert.Equal((HttpStatusCode.BadRequest, "invalidNamespace"), (status, Code(body)));
    }
}
