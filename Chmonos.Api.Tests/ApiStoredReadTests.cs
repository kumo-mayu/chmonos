using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Microsoft.AspNetCore.Builder;

namespace Chmonos.Api.Tests;

/// <summary>
/// 手元に控えた情報を読む道（plan 32・docs/spec/local-api-stored.md）を HTTP で叩く。保存先は作り物で、名前・ID・GUID・場所は作り物。
/// 読む道はどれも、書き込みの種類を1つも持たないソフトのトークンで読める（読むのはいつも許す：api-clients.md）
/// </summary>
public sealed class ApiStoredReadTests : IAsyncLifetime
{
    private const string Token = "test-token-0123456789abcdef";

    /// <summary>書き込みを1つも許していないソフトのトークン。</summary>
    private const string ReaderToken = "reader-token-0123456789abcdef";

    private static readonly string HashA = new('A', 64);
    private static readonly string HashB = new('B', 64);
    private static readonly string HashC = new('C', 64);
    private static readonly string HashOrphan = new('E', 64);

    private const string GuidShared = "0123456789abcdef0123456789abcdef";
    private const string GuidOnlyA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1";
    private const string GuidOrphan = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeee1";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-api-tests", Guid.NewGuid().ToString("N"));
    private WebApplication? _app;
    private HttpClient _client = null!;
    private DataStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = new DataStore(new AppPaths(_root));
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "9600001",
            Booth = new BoothBlock { Name = "作り物の夏服" },
            Local = new LocalBlock
            {
                LocalFiles =
                [
                    new LocalFileRecord { Hash = HashA, Paths = [@"D:\files\summer.zip"], SizeBytes = 1, Contents = ["summer/summer.unitypackage", "summer/readme.txt"] },

                    // 外したファイル：状態を潰さずに返す
                    new LocalFileRecord { Hash = HashC, Paths = [@"D:\files\old.zip"], SizeBytes = 1, Contents = ["old.unitypackage"], Detached = true },
                ],
            },
        });
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "9600002",
            Booth = new BoothBlock { Name = "作り物の冬服" },
            Local = new LocalBlock
            {
                LocalFiles =
                [
                    new LocalFileRecord { Hash = HashB, Paths = [@"D:\files\winter.zip"], SizeBytes = 1, Contents = ["winter.unitypackage", "extra.unitypackage"] },
                ],
            },
        });

        var packages = new UnityPackagePathStore(_store.Paths);
        await packages.SaveAsync(HashA, new Dictionary<string, IReadOnlyList<UnityPackageAsset>>
        {
            ["summer/summer.unitypackage"] = [new(GuidOnlyA, "Assets/Summer/summer.fbx"), new(GuidShared, "Assets/Shader/shared.shader")],
        });
        await packages.SaveAsync(HashC, new Dictionary<string, IReadOnlyList<UnityPackageAsset>>
        {
            ["old.unitypackage"] = [new(GuidShared, "Assets/Shader/shared.shader")],
        });

        // winter.zip は1つ目の包みだけ読んである（extra はまだ読んでいない）
        await packages.SaveAsync(HashB, new Dictionary<string, IReadOnlyList<UnityPackageAsset>>
        {
            ["winter.unitypackage"] = [new(GuidShared.ToUpperInvariant(), "Assets/Shader/shared.shader")],
        });

        // どの商品のファイルでもない zip の控え（未確定のまま）
        await packages.SaveAsync(HashOrphan, new Dictionary<string, IReadOnlyList<UnityPackageAsset>>
        {
            ["orphan.unitypackage"] = [new(GuidOrphan, "Assets/Orphan/a.prefab")],
        });

        await _store.ApiClients.SaveAsync(new ApiClientList
        {
            Clients = [new ApiClientRecord { Id = "reader", Name = "読むだけの道具", Scopes = [], TokenHash = ApiClientBook.HashOf(ReaderToken) }],
        });

        (_app, _, _) = await ApiHost.BuildAsync(new ApiHostOptions(_root, 0, Token, []));
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

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body = null, string token = ReaderToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return _client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task 商品の包みの中身は_GUIDとパスを返し_まだ読んでいない包みはnull()
    {
        var winter = await JsonOf(await SendAsync(HttpMethod.Get, "/v1/items/9600002/unitypackage-assets"));
        Assert.Equal(2, winter.GetArrayLength());
        Assert.Equal("winter.unitypackage", winter[0].GetProperty("entry").GetString());
        Assert.Equal(HashB, winter[0].GetProperty("zipHash").GetString());

        // GUID は小文字に揃える（控えの書き手によって大文字が混じっても、逆引きと同じ形で返す）
        Assert.Equal(GuidShared, winter[0].GetProperty("assets")[0].GetProperty("guid").GetString());
        Assert.Equal("Assets/Shader/shared.shader", winter[0].GetProperty("assets")[0].GetProperty("path").GetString());
        Assert.Equal("extra.unitypackage", winter[1].GetProperty("entry").GetString());
        Assert.Equal(JsonValueKind.Null, winter[1].GetProperty("assets").ValueKind);

        var summer = await JsonOf(await SendAsync(HttpMethod.Get, "/v1/items/9600001/unitypackage-assets"));
        Assert.Equal(["present", "detached"], summer.EnumerateArray().Select(row => row.GetProperty("fileState").GetString()));

        await AssertProblem(await SendAsync(HttpMethod.Get, "/v1/items/9999999/unitypackage-assets"), HttpStatusCode.NotFound, "itemNotFound");
    }

    [Fact]
    public async Task GUIDの逆引きは_全部の包みと商品を返し_商品へ戻せない物は見つからないと答える()
    {
        var body = $$"""{"guids": ["{{GuidShared.ToUpperInvariant()}}", " {{GuidOnlyA}} ", "{{GuidOrphan}}", "ffffffffffffffffffffffffffffffff"]}""";
        var answer = await JsonOf(await SendAsync(HttpMethod.Post, "/v1/unitypackages/lookup", body));

        var found = answer.GetProperty("found").EnumerateArray()
            .Select(row => (row.GetProperty("guid").GetString(), row.GetProperty("itemId").GetString(), row.GetProperty("zipHash").GetString(),
                row.GetProperty("entry").GetString(), row.GetProperty("fileState").GetString()))
            .ToList();
        Assert.Equal(
            [
                (GuidShared, "9600001", HashA, "summer/summer.unitypackage", "present"),
                (GuidShared, "9600001", HashC, "old.unitypackage", "detached"),
                (GuidShared, "9600002", HashB, "winter.unitypackage", "present"),
                (GuidOnlyA, "9600001", HashA, "summer/summer.unitypackage", "present"),
            ],
            found);
        Assert.Equal("Assets/Summer/summer.fbx", answer.GetProperty("found")[3].GetProperty("path").GetString());
        Assert.Equal("作り物の夏服", answer.GetProperty("found")[3].GetProperty("itemName").GetString());
        Assert.Equal([GuidOrphan, "ffffffffffffffffffffffffffffffff"], answer.GetProperty("missing").EnumerateArray().Select(guid => guid.GetString()));
    }

    [Fact]
    public async Task GUIDの逆引きは_控えが増えたら作り直した表で答える()
    {
        const string guid = "cccccccccccccccccccccccccccccc01";
        var body = $$"""{"guids": ["{{guid}}"]}""";
        Assert.Equal(0, (await JsonOf(await SendAsync(HttpMethod.Post, "/v1/unitypackages/lookup", body))).GetProperty("found").GetArrayLength());

        // 取り込みの裏が、外した zip の控えを後から足した（同じ控えのファイルを書き直す）
        await new UnityPackagePathStore(_store.Paths).SaveAsync(HashC, new Dictionary<string, IReadOnlyList<UnityPackageAsset>>
        {
            ["old.unitypackage"] = [new(GuidShared, "Assets/Shader/shared.shader"), new(guid, "Assets/Old/new.mat")],
        });

        var answer = await JsonOf(await SendAsync(HttpMethod.Post, "/v1/unitypackages/lookup", body));
        Assert.Equal("Assets/Old/new.mat", answer.GetProperty("found").EnumerateArray().Single().GetProperty("path").GetString());
    }

    [Fact]
    public async Task GUIDの逆引きは_形の違う値と空と多すぎる数を断る()
    {
        await AssertProblem(await SendAsync(HttpMethod.Post, "/v1/unitypackages/lookup", """{"guids": ["xyz"]}"""), HttpStatusCode.BadRequest, "invalidGuid");
        await AssertProblem(await SendAsync(HttpMethod.Post, "/v1/unitypackages/lookup", """{"guids": [null]}"""), HttpStatusCode.BadRequest, "invalidGuid");
        await AssertProblem(await SendAsync(HttpMethod.Post, "/v1/unitypackages/lookup", """{"guids": []}"""), HttpStatusCode.BadRequest, "invalidGuids");
        var many = JsonSerializer.Serialize(new { guids = Enumerable.Repeat(GuidShared, 1001) });
        await AssertProblem(await SendAsync(HttpMethod.Post, "/v1/unitypackages/lookup", many), HttpStatusCode.BadRequest, "invalidLimit");
    }
}
