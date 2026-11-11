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
    private static readonly string HashUnresolved = new('1', 64);
    private static readonly string HashOther = new('2', 64);

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

        var scans = new UnityScanStore(_store.Paths);
        scans.Save(@"D:\Unity\Avatar", new UnityProjectScan(
            [new UnityProjectMatch("9600002", 3, 4), new UnityProjectMatch("9600001", 1, 4), new UnityProjectMatch("9699999", 1, 1)],
            new Dictionary<string, IReadOnlyList<UnityImageMatch>>
            {
                ["9600001"] = [new UnityImageMatch("Assets/Tex/a.png", "a.png", UnityImageMatchKind.SameImage), new UnityImageMatch("Assets/Tex/b.png", "b.png", UnityImageMatchKind.SameName)],
            }));
        scans.Save(@"C:\Unity\Other", UnityProjectScan.Empty);

        // 別の場所の名前で写された控え（中の場所とファイルの名前が合わない）は読まない
        File.Copy(_store.Paths.UnityScanFile(Directory.GetFiles(Path.Combine(_root, AppPaths.UnityScansDirName)).Select(Path.GetFileNameWithoutExtension).First()!),
            _store.Paths.UnityScanFile("0000000000000000"));

        // ハッシュを数えた控え：商品のファイル（2か所）・商品が決まっていないファイル・どこにも属さない物（外した物など）
        var at = new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero);
        await _store.ScanCache.SaveAsync(
        [
            new ScanCacheEntry { Path = @"E:\copy\summer.zip", SizeBytes = 1, ModifiedAtUtc = at, Hash = HashA.ToLowerInvariant() },
            new ScanCacheEntry { Path = @"D:\files\summer.zip", SizeBytes = 1, ModifiedAtUtc = at, Hash = HashA },
            new ScanCacheEntry { Path = @"D:\inbox\unknown.zip", SizeBytes = 5, ModifiedAtUtc = at, Hash = HashUnresolved },
            new ScanCacheEntry { Path = @"D:\inbox\excluded.txt", SizeBytes = 7, ModifiedAtUtc = at, Hash = HashOther },
        ]);
        await _store.Unresolved.SaveAsync(
        [
            new UnresolvedFile { Hash = HashUnresolved, Paths = [@"D:\inbox\unknown.zip"], SizeBytes = 5, ModifiedAtUtc = at, FirstSeenAt = at },
        ]);

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
    public async Task プロジェクトを調べた結果は_控えごとに商品と割合と手で置いた画像を返す()
    {
        var all = await JsonOf(await SendAsync(HttpMethod.Get, "/v1/unity-scans"));
        Assert.Equal([@"C:\Unity\Other", @"D:\Unity\Avatar"], all.EnumerateArray().Select(scan => scan.GetProperty("project").GetString()));

        var scan = all[1];
        Assert.True(scan.GetProperty("savedAt").GetDateTimeOffset() > DateTimeOffset.Now.AddMinutes(-5));
        var items = scan.GetProperty("items").EnumerateArray()
            .Select(item => (item.GetProperty("itemId").GetString(), item.GetProperty("present").GetInt32(), item.GetProperty("total").GetInt32(), item.GetProperty("ratio").GetDouble()))
            .ToList();
        Assert.Equal([("9600002", 3, 4, 0.75), ("9600001", 1, 4, 0.25), ("9699999", 1, 1, 1.0)], items);

        // もう無い商品は名前が null（控えの行は潰さない）
        Assert.Equal("作り物の冬服", scan.GetProperty("items")[0].GetProperty("itemName").GetString());
        Assert.Equal(JsonValueKind.Null, scan.GetProperty("items")[2].GetProperty("itemName").ValueKind);
        Assert.Equal(["sameImage", "sameName"], scan.GetProperty("images").EnumerateArray().Select(image => image.GetProperty("kind").GetString()));

        // 場所は条件 unityProject と同じ揃え方で照らす（/ 区切り・末尾の区切り・大文字小文字）
        var one = await JsonOf(await SendAsync(HttpMethod.Get, "/v1/unity-scans?project=d:/unity/avatar/"));
        Assert.Equal(@"D:\Unity\Avatar", one.EnumerateArray().Single().GetProperty("project").GetString());
    }

    [Fact]
    public async Task ファイルのまとめての逆引きは_1件ずつの答えと同じ物を送った順に返す()
    {
        var body = $$"""{"hashes": ["{{HashB.ToLowerInvariant()}}", "{{HashOther}}"], "paths": ["d:/FILES/summer.zip"]}""";
        var answer = await JsonOf(await SendAsync(HttpMethod.Post, "/v1/files/lookup", body));

        var hashes = answer.GetProperty("hashes");
        Assert.Equal(HashB.ToLowerInvariant(), hashes[0].GetProperty("key").GetString());
        Assert.Equal("9600002", hashes[0].GetProperty("owners").EnumerateArray().Single().GetProperty("itemId").GetString());
        Assert.Equal(0, hashes[1].GetProperty("owners").GetArrayLength());

        // 1件ずつの GET と同じ答え（同じ関数を当てる）
        var single = await JsonOf(await SendAsync(HttpMethod.Get, "/v1/files/lookup?path=d:/FILES/summer.zip"));
        Assert.Equal(single.GetRawText(), answer.GetProperty("paths")[0].GetProperty("owners").GetRawText());
        Assert.Equal("present", single[0].GetProperty("state").GetString());

        await AssertProblem(await SendAsync(HttpMethod.Post, "/v1/files/lookup", "{}"), HttpStatusCode.BadRequest, "invalidLookup");
        await AssertProblem(await SendAsync(HttpMethod.Post, "/v1/files/lookup", """{"hashes": [null]}"""), HttpStatusCode.BadRequest, "invalidIds");
        var many = JsonSerializer.Serialize(new { hashes = Enumerable.Repeat(HashA, 150), paths = Enumerable.Repeat("D:\\a.zip", 51) });
        await AssertProblem(await SendAsync(HttpMethod.Post, "/v1/files/lookup", many), HttpStatusCode.BadRequest, "invalidLimit");
    }

    [Fact]
    public async Task ハッシュから場所を引く道は_商品のファイルと未確定の物だけを返す()
    {
        var body = $$"""{"hashes": ["{{HashA}}", "{{HashUnresolved}}", "{{HashOther}}", "{{HashOrphan}}"]}""";
        var answer = await JsonOf(await SendAsync(HttpMethod.Post, "/v1/files/locations", body));

        var found = answer.GetProperty("found").EnumerateArray()
            .Select(row => (row.GetProperty("path").GetString(), row.GetProperty("owner").GetString()))
            .ToList();
        Assert.Equal([(@"D:\files\summer.zip", "item"), (@"E:\copy\summer.zip", "item"), (@"D:\inbox\unknown.zip", "unresolved")], found);
        Assert.Equal(5, answer.GetProperty("found")[2].GetProperty("sizeBytes").GetInt64());

        // どこにも属さない物（外した物など）の場所は出さない。控えに無い物と同じく見つからないと答える
        Assert.Equal([HashOther, HashOrphan], answer.GetProperty("missing").EnumerateArray().Select(hash => hash.GetString()));

        await AssertProblem(await SendAsync(HttpMethod.Post, "/v1/files/locations", """{"hashes": []}"""), HttpStatusCode.BadRequest, "invalidHashes");
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
