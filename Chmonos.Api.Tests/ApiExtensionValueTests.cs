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
/// 外のソフトの情報の値とメモ欄（docs/spec/local-api-extensions-api.md）：型の確かめ・PUT と PATCH・隠した欄・ETag と 412・413・持ち主・付け先。
/// 命令の口は本物の <see cref="CommandHandler"/>（錠の中で今の宣言に当てる所まで通す）。商品・ソフトの名前・名前空間は作り物
/// </summary>
public sealed class ApiExtensionValueTests : IAsyncLifetime
{
    private const string Legacy = "extension-legacy-token-0123456789";
    private const string TokenA = "extension-token-a-0123456789";
    private const string TokenB = "extension-token-b-0123456789";
    private const string ItemId = "9600001";
    private const string Path0 = "/v1/items/9600001/extensions/outfit-checker";

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
            Clients = [Client("a", TokenA, "outfit-checker", ApiScopes.Extensions), Client("b", TokenB, "other-tool", ApiScopes.Extensions)],
        });
        await _store.Items.SaveAsync(new ItemRecord { Id = ItemId, Booth = new BoothBlock { Name = "作り物の帽子" } });
        await _store.Extensions.ChangeSchemaAsync("outfit-checker", _ => new ExtensionSchemaFile
        {
            Label = "作り物",
            Software = "作り物の道具a",
            Fields =
            [
                new ExtensionField { Key = "polyCount", Label = "数", Type = ExtensionFieldTypes.Number, Min = 0, Max = 10 },
                new ExtensionField { Key = "page", Label = "ページ", Type = ExtensionFieldTypes.Url },
                new ExtensionField { Key = "checkedAt", Label = "日", Type = ExtensionFieldTypes.Date },
                new ExtensionField { Key = "testedOn", Label = "アバター", Type = ExtensionFieldTypes.AvatarList },
                new ExtensionField { Key = "license", Label = "規約", Type = ExtensionFieldTypes.Choice, Options = [new ExtensionChoice { Value = "ok", Label = "使える" }] },
                new ExtensionField
                {
                    Key = "results", Label = "結果", Type = ExtensionFieldTypes.Table,
                    Columns = [new ExtensionField { Key = "note", Label = "メモ", Type = ExtensionFieldTypes.Text }],
                },
                new ExtensionField { Key = "note", Label = "メモ", Type = ExtensionFieldTypes.Text },
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

    private async Task<(HttpStatusCode Status, JsonElement Body, string? Tag)> SendAsync(
        HttpMethod method, string path, string? body = null, string token = TokenA, string? ifMatch = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone(), response.Headers.ETag?.Tag);
    }

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private string ValueFile => Path.Combine(_root, "extensions", "outfit-checker", "items", ItemId + ".json");

    [Fact]
    public async Task 持ち主が値を書くと付け先のファイルに置かれ_ほかのソフトも読める()
    {
        var (status, body, _) = await SendAsync(HttpMethod.Put, Path0,
            """{ "values": { "polyCount": 70000, "page": "https://example.com/a", "checkedAt": "2026-11-27", "testedOn": ["4567"], "license": "ok", "results": [ { "note": "合う" } ] } }""");

        Assert.Equal(HttpStatusCode.OK, status);
        // min・max は目安なので、範囲の外の数も受ける（spec）
        Assert.Equal(70000, body.GetProperty("values").GetProperty("polyCount").GetInt32());
        Assert.True(File.Exists(ValueFile));

        var (readStatus, read, _) = await SendAsync(HttpMethod.Get, Path0, token: TokenB);
        Assert.Equal(HttpStatusCode.OK, readStatus);
        Assert.Equal("合う", read.GetProperty("values").GetProperty("results")[0].GetProperty("note").GetString());
        Assert.Equal("items", read.GetProperty("target").GetString());
    }

    [Theory]
    [InlineData("""{ "values": { "nothing": 1 } }""", "unknownField")]
    [InlineData("""{ "values": { "results": [ { "nothing": "x" } ] } }""", "unknownField")]
    [InlineData("""{ "values": { "polyCount": "多い" } }""", "invalidValue")]
    [InlineData("""{ "values": { "page": "file:///C:/a.txt" } }""", "invalidValue")]
    [InlineData("""{ "values": { "page": "javascript:alert(1)" } }""", "invalidValue")]
    [InlineData("""{ "values": { "checkedAt": "2026/11/27" } }""", "invalidValue")]
    [InlineData("""{ "values": { "license": "maybe" } }""", "invalidValue")]
    [InlineData("""{ "values": { "testedOn": ["../x"] } }""", "invalidValue")]
    public async Task 宣言に無い欄と型の合わない値は断り_何も書かない(string body, string code)
    {
        var (status, problem, _) = await SendAsync(HttpMethod.Put, Path0, body);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(code, Code(problem));
        Assert.False(File.Exists(ValueFile));
    }

    [Fact]
    public async Task PATCH_は名指しした欄だけ書き_null_で消す_欄が無くなればファイルも消す()
    {
        await SendAsync(HttpMethod.Put, Path0, """{ "values": { "polyCount": 1, "note": "前" } }""");

        var (_, patched, _) = await SendAsync(HttpMethod.Patch, Path0, """{ "values": { "note": null } }""");
        Assert.Equal(1, patched.GetProperty("values").GetProperty("polyCount").GetInt32());
        Assert.False(patched.GetProperty("values").TryGetProperty("note", out _));

        await SendAsync(HttpMethod.Patch, Path0, """{ "values": { "polyCount": null } }""");
        Assert.False(File.Exists(ValueFile));
    }

    [Fact]
    public async Task PUT_は宣言から外した欄の値を残し_持ち主だけが_include_hidden_で読める()
    {
        await SendAsync(HttpMethod.Put, Path0, """{ "values": { "polyCount": 1, "note": "残す" } }""");
        await _store.Extensions.ChangeSchemaAsync("outfit-checker", schema => schema! with
        {
            Fields = [.. schema.Fields.Where(field => field.Key != "note")],
            Hidden = [.. schema.Fields.Where(field => field.Key == "note")],
        });

        var (status, _, _) = await SendAsync(HttpMethod.Put, Path0, """{ "values": { "polyCount": 2 } }""");
        Assert.Equal(HttpStatusCode.OK, status);

        var (_, other, _) = await SendAsync(HttpMethod.Get, Path0 + "?include=hidden", token: TokenB);
        Assert.False(other.GetProperty("values").TryGetProperty("note", out _));
        var (_, mine, _) = await SendAsync(HttpMethod.Get, Path0 + "?include=hidden");
        Assert.Equal("残す", mine.GetProperty("values").GetProperty("note").GetString());

        // 隠した欄へは書けない（宣言に無い欄と同じ）
        var (refused, problem, _) = await SendAsync(HttpMethod.Patch, Path0, """{ "values": { "note": "新" } }""");
        Assert.Equal(HttpStatusCode.BadRequest, refused);
        Assert.Equal("unknownField", Code(problem));
    }

    [Fact]
    public async Task 読んだ時の印で書け_古い印なら_412_で断り値を残す()
    {
        var (_, _, first) = await SendAsync(HttpMethod.Put, Path0, """{ "values": { "polyCount": 1 } }""");
        var (_, _, read) = await SendAsync(HttpMethod.Get, Path0);
        var (_, _, readHidden) = await SendAsync(HttpMethod.Get, Path0 + "?include=hidden");
        Assert.Equal(first, read);
        Assert.NotEqual(read, readHidden);

        // 隠した欄を入れた答えの印でも同じ値に当たる
        var (okStatus, _, second) = await SendAsync(HttpMethod.Patch, Path0, """{ "values": { "polyCount": 2 } }""", ifMatch: readHidden);
        Assert.Equal(HttpStatusCode.OK, okStatus);

        var (stale, problem, _) = await SendAsync(HttpMethod.Patch, Path0, """{ "values": { "polyCount": 3 } }""", ifMatch: first);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale);
        Assert.Equal("preconditionFailed", Code(problem));
        var (staleDelete, _, _) = await SendAsync(HttpMethod.Delete, Path0, ifMatch: first);
        Assert.Equal(HttpStatusCode.PreconditionFailed, staleDelete);

        var (_, now, _) = await SendAsync(HttpMethod.Get, Path0);
        Assert.Equal(2, now.GetProperty("values").GetProperty("polyCount").GetInt32());

        var (deleted, _, _) = await SendAsync(HttpMethod.Delete, Path0, ifMatch: second);
        Assert.Equal(HttpStatusCode.NoContent, deleted);
        Assert.False(File.Exists(ValueFile));
    }

    [Fact]
    public async Task 上限を超える値とメモは_413_で断る()
    {
        var big = new string('あ', ExtensionLimits.ValueBytes / 2);
        var (status, problem, _) = await SendAsync(HttpMethod.Put, Path0, $$"""{ "values": { "note": "{{big}}" } }""");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, status);
        Assert.Equal("extensionTooLarge", Code(problem));

        var memo = new string('a', ExtensionLimits.MemoBytes + 1);
        var (memoStatus, _, _) = await SendAsync(HttpMethod.Put, Path0 + "/memo", $$"""{ "memo": { "state": "{{memo}}" } }""");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, memoStatus);
    }

    [Fact]
    public async Task 書けるのは持ち主だけ_メモ欄は読むのも持ち主だけ()
    {
        var (other, problem, _) = await SendAsync(HttpMethod.Put, Path0, """{ "values": { "polyCount": 1 } }""", token: TokenB);
        Assert.Equal(HttpStatusCode.Forbidden, other);
        Assert.Equal("notOwner", Code(problem));
        var (legacy, _, _) = await SendAsync(HttpMethod.Put, Path0, """{ "values": { "polyCount": 1 } }""", token: Legacy);
        Assert.Equal(HttpStatusCode.Forbidden, legacy);

        var (memoStatus, _, _) = await SendAsync(HttpMethod.Put, Path0 + "/memo", """{ "memo": { "cursor": 5 } }""");
        Assert.Equal(HttpStatusCode.OK, memoStatus);
        Assert.True(File.Exists(Path.Combine(_root, "extensions", "outfit-checker", "items", ItemId + ".memo.json")));
        var (mine, memo, _) = await SendAsync(HttpMethod.Get, Path0 + "/memo");
        Assert.Equal(HttpStatusCode.OK, mine);
        Assert.Equal(5, memo.GetProperty("memo").GetProperty("cursor").GetInt32());
        foreach (var token in new[] { TokenB, Legacy })
        {
            var (refused, _, _) = await SendAsync(HttpMethod.Get, Path0 + "/memo", token: token);
            Assert.Equal(HttpStatusCode.Forbidden, refused);
        }
    }

    [Theory]
    [InlineData("/v1/items/9600999/extensions/outfit-checker")]
    [InlineData("/v1/items/..%2F..%2Fx/extensions/outfit-checker")]
    [InlineData("/v1/modifications/mod-0001/extensions/outfit-checker")]
    [InlineData("/v1/files/ABCDEF/extensions/outfit-checker")]
    [InlineData("/v1/shops/nothing-here/extensions/outfit-checker")]
    public async Task 知らない付け先は_404_で何も置かない(string path)
    {
        var (status, _, _) = await SendAsync(HttpMethod.Put, path, """{ "values": { "polyCount": 1 } }""");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.False(Directory.Exists(Path.Combine(_root, "extensions", "outfit-checker", "items")));
    }

    [Fact]
    public async Task 全体の値は名前空間の直下に置く()
    {
        var (status, body, _) = await SendAsync(HttpMethod.Put, "/v1/extensions/outfit-checker/data", """{ "values": { "polyCount": 3 } }""");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("global", body.GetProperty("target").GetString());
        Assert.True(File.Exists(Path.Combine(_root, "extensions", "outfit-checker", "global.json")));
    }

    [Fact]
    public async Task 商品を外した後の書き込みは錠の中で断り_値を残さない()
    {
        // 写しにはまだ商品があるが、記録はもう消えている（外す処理と書き込みが重なった時）
        File.Delete(_store.Paths.ItemFile(ItemId));

        var (status, problem, _) = await SendAsync(HttpMethod.Put, Path0, """{ "values": { "polyCount": 1 } }""");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("targetNotFound", Code(problem));
        Assert.False(File.Exists(ValueFile));
    }
}
