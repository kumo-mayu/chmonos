using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Microsoft.AspNetCore.Builder;

namespace Chmonos.Api.Tests;

/// <summary>
/// 内部の情報を読む道（2026-11-27 に出すと決めた物：docs/spec/local-api-shapes.md「出す物を決めた」）を HTTP で叩く。
/// 保存先は作り物で、名前・ID・場所は作り物。読む道はどれも、書き込みの種類を1つも持たないソフトのトークンで読める
/// </summary>
public sealed class ApiInternalListTests : IAsyncLifetime
{
    private const string Token = "test-token-0123456789abcdef";

    /// <summary>書き込みを1つも許していないソフトのトークン。</summary>
    private const string ReaderToken = "reader-token-0123456789abcdef";

    private static readonly DateTimeOffset Day1 = new(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(9));
    private static readonly DateTimeOffset Day2 = new(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(9));
    private static readonly DateTimeOffset Day3 = new(2026, 11, 3, 9, 0, 0, TimeSpan.FromHours(9));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-api-tests", Guid.NewGuid().ToString("N"));
    private WebApplication? _app;
    private HttpClient _client = null!;
    private DataStore _store = null!;
    private StoreSync _sync = null!;

    public async Task InitializeAsync()
    {
        _store = new DataStore(new AppPaths(_root));
        // 登録したフォルダ：見つからない物・2つの商品が同じ場所を登録・大文字小文字だけ違う場所（並びを一意にする試し）
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "9700001",
            Booth = new BoothBlock { Name = "作り物の上着" },
            Local = new LocalBlock
            {
                LocalFolders =
                [
                    new LocalFolderRecord { Path = @"D:\assets\Coat", FileCount = 3, TotalBytes = 300, RegisteredAt = Day1, UnityPackages = ["coat.unitypackage"] },
                    new LocalFolderRecord { Path = @"c:\old\Coat", FileCount = 1, TotalBytes = 10, MissingSince = Day2 },
                ],
            },
        });
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "9700002",
            Booth = new BoothBlock { Name = "作り物の靴" },
            Local = new LocalBlock
            {
                LocalFolders =
                [
                    new LocalFolderRecord { Path = @"D:\assets\coat", RegisteredAt = Day2 },
                    new LocalFolderRecord { Path = @"D:\assets\Coat", RegisteredAt = Day2 },
                ],
            },
        });

        // 足跡：同じ商品の行が2つ（新しい方を取る）・写しに無い商品の行（消した直後。行は残して返す）
        await _store.Recent.SaveAsync(new RecentLog
        {
            Entries =
            [
                new RecentEntry { ItemId = "9700002", ViewedAt = Day1 },
                new RecentEntry { ItemId = "9700001", AddedAt = Day1, UsedAt = Day2 },
                new RecentEntry { ItemId = "9700002", ViewedAt = Day3, AddedAt = Day1 },
                new RecentEntry { ItemId = "9799999", ViewedAt = Day2 },
            ],
        });

        await _store.ApiClients.SaveAsync(new ApiClientList
        {
            Clients = [new ApiClientRecord { Id = "reader", Name = "読むだけの道具", Scopes = [], TokenHash = ApiClientBook.HashOf(ReaderToken) }],
        });

        (_app, _, _sync) = await ApiHost.BuildAsync(new ApiHostOptions(_root, 0, Token, [], Watch: false));
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

    private Task<HttpResponseMessage> GetAsync(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ReaderToken);
        return _client.SendAsync(request);
    }

    private async Task<JsonElement> JsonOf(string path)
    {
        var response = await GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private async Task AssertProblem(string path, string code)
    {
        var response = await GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
    }

    private static IReadOnlyList<string?> Ids(JsonElement page, string name = "itemId")
        => page.GetProperty("items").EnumerateArray().Select(row => row.GetProperty(name).GetString()).ToList();

    [Fact]
    public async Task 最近の足跡は_商品ごとに3種類の日時を返し_続きの印で分けて取れる()
    {
        var first = await JsonOf("/v1/recent?limit=2");
        Assert.Equal(3, first.GetProperty("total").GetInt32());
        Assert.Equal(["9700001", "9700002"], Ids(first));

        var jacket = first.GetProperty("items")[0];
        Assert.Equal("作り物の上着", jacket.GetProperty("itemName").GetString());
        Assert.Equal(Day1, jacket.GetProperty("addedAt").GetDateTimeOffset());
        Assert.Equal(Day2, jacket.GetProperty("usedAt").GetDateTimeOffset());

        // 付いていない種類は項目を省かず null
        Assert.Equal(JsonValueKind.Null, jacket.GetProperty("viewedAt").ValueKind);

        // 同じ商品の行が2つあれば、種類ごとに新しい方（検索の条件「最近」と同じ RecentActivity.Times）
        Assert.Equal(Day3, first.GetProperty("items")[1].GetProperty("viewedAt").GetDateTimeOffset());

        var cursor = Uri.EscapeDataString(first.GetProperty("nextCursor").GetString()!);
        var second = await JsonOf($"/v1/recent?limit=2&cursor={cursor}");
        Assert.Equal(["9799999"], Ids(second));

        // 写しに無い商品の行も消さずに返し、名前を null にする
        Assert.Equal(JsonValueKind.Null, second.GetProperty("items")[0].GetProperty("itemName").ValueKind);
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);

        await AssertProblem("/v1/recent?cursor=abc", "invalidCursor");
        await AssertProblem("/v1/recent?limit=201", "invalidLimit");
    }

    [Fact]
    public async Task 最近の足跡は_書いたら読み直した答えを返す()
    {
        var before = await JsonOf("/v1/recent");
        Assert.Equal(3, before.GetProperty("total").GetInt32());

        // 画面が足跡を書いた（見張りの知らせと同じ道：ファイルの場所から部分を見分けて読み直す）
        await _store.Recent.SaveAsync(new RecentLog { Entries = [new RecentEntry { ItemId = "9700002", UsedAt = Day3 }] });
        _sync.Note(_store.Paths.RecentFile);
        await _sync.ApplyPendingAsync();

        var after = await JsonOf("/v1/recent");
        Assert.Equal(["9700002"], Ids(after));
        Assert.Equal(Day3, after.GetProperty("items")[0].GetProperty("usedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task 登録したフォルダは_全部の商品から場所の順に並べ_見つからない物も状態つきで返す()
    {
        var first = await JsonOf("/v1/folders?limit=3");
        Assert.Equal(4, first.GetProperty("total").GetInt32());
        var rows = first.GetProperty("items");
        Assert.Equal([@"c:\old\Coat", @"D:\assets\Coat", @"D:\assets\Coat"], rows.EnumerateArray().Select(row => row.GetProperty("path").GetString()));
        Assert.Equal(["9700001", "9700001", "9700002"], Ids(first));

        // 見つからない物は外さずに状態で返す（ファイルの逆引きのフォルダと同じ present／missing）
        Assert.Equal("missing", rows[0].GetProperty("state").GetString());
        Assert.Equal(Day2, rows[0].GetProperty("missingSince").GetDateTimeOffset());
        Assert.Equal("present", rows[1].GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("missingSince").ValueKind);
        Assert.Equal("作り物の上着", rows[1].GetProperty("itemName").GetString());
        Assert.Equal(["coat.unitypackage"], rows[1].GetProperty("unityPackages").EnumerateArray().Select(entry => entry.GetString()));

        // 記録した日時が無い物は null（西暦1年を返さない）
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("registeredAt").ValueKind);

        var cursor = Uri.EscapeDataString(first.GetProperty("nextCursor").GetString()!);
        var second = await JsonOf($"/v1/folders?limit=3&cursor={cursor}");
        Assert.Equal([@"D:\assets\coat"], second.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("path").GetString()));
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);

        // 商品の詳細の folders と同じ値（同じ ItemMapper.Folder から作る）
        var detail = await JsonOf("/v1/items/9700001");
        var coat = detail.GetProperty("folders").EnumerateArray().Single(row => row.GetProperty("path").GetString() == @"D:\assets\Coat");
        Assert.Equal(rows[1].GetProperty("totalBytes").GetInt64(), coat.GetProperty("totalBytes").GetInt64());
        Assert.Equal(rows[1].GetProperty("registeredAt").GetDateTimeOffset(), coat.GetProperty("registeredAt").GetDateTimeOffset());

        // 別の URL の続きの印は断る
        var recentCursor = (await JsonOf("/v1/recent?limit=1")).GetProperty("nextCursor").GetString()!;
        await AssertProblem($"/v1/folders?cursor={Uri.EscapeDataString(recentCursor)}", "invalidCursor");
        await AssertProblem("/v1/folders?limit=0", "invalidLimit");
    }
}
