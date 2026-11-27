using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Microsoft.AspNetCore.Builder;

namespace Chmonos.Api.Tests;

/// <summary>
/// 内部の情報を読む道（2026-11-27 に出すと決めた物：docs/spec/local-api-internal.md「出す物を決めた」）を HTTP で叩く。
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

        // 外したファイル：同じ日時が2件（記録の後ろが先）・場所が2つ・出どころの文が無い物・知らない文の物
        await _store.Excluded.SaveAsync(
        [
            new ExcludedEntry { Hash = new string('1', 64), Paths = [@"D:\inbox\a.zip", @"E:\copy\a.zip"], ExcludedAt = Day1, Reason = "作り物の理由" },
            new ExcludedEntry { Hash = new string('2', 64), Paths = [@"D:\inbox\b.zip"], ExcludedAt = Day2 },
            new ExcludedEntry { Hash = new string('3', 64), Paths = [@"D:\inbox\c.zip"], ExcludedAt = Day2 },
        ]);

        // 商品が決まっていないファイル：場所の順を試す2件（後ろの場所を先に書く）・BOOTH に無かった記録と壊れた zip・場所の無い物
        await _store.Unresolved.SaveAsync(
        [
            new UnresolvedFile
            {
                Hash = new string('6', 64), Paths = [@"D:\inbox\z.zip"], SizeBytes = 20, ModifiedAtUtc = Day1, FirstSeenAt = Day2,
                Contents = ["z/readme.txt"], CandidateItemIds = ["9700001", "9700002"], ZoneHostUrl = "https://example.invalid/z.zip",
            },
            new UnresolvedFile
            {
                Hash = new string('5', 64), Paths = [@"D:\inbox\a.zip", @"E:\copy\a.zip"], SizeBytes = 10, ModifiedAtUtc = Day1, FirstSeenAt = Day1,
                ArchiveBroken = true, SamePathItemIds = ["9700001"], NotOnBooth = new BoothNotFoundNote { ItemId = "9799998", CheckedAt = Day3 },
            },
            new UnresolvedFile { Hash = new string('7', 64), Paths = [], SizeBytes = 1, ModifiedAtUtc = Day1, FirstSeenAt = Day1 },
        ]);

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

    [Fact]
    public async Task 外したファイルは_画面と同じ新しい順に場所の全部と出どころの値を返し_外したら読み直す()
    {
        var first = await JsonOf("/v1/excluded?limit=2");
        Assert.Equal(3, first.GetProperty("total").GetInt32());

        // 外した日時の新しい順・同じ日時は記録の後ろ（後から外した物）が先（設定の「除外したファイル」と同じ）
        Assert.Equal([new string('3', 64), new string('2', 64)], Ids(first, "hash"));
        Assert.Equal(JsonValueKind.Null, first.GetProperty("items")[1].GetProperty("source").ValueKind);

        var cursor = Uri.EscapeDataString(first.GetProperty("nextCursor").GetString()!);
        var second = await JsonOf($"/v1/excluded?limit=2&cursor={cursor}");
        var oldest = second.GetProperty("items").EnumerateArray().Single();

        // 場所は潰さず全部（画面は1つ目だけを見せる）
        Assert.Equal([@"D:\inbox\a.zip", @"E:\copy\a.zip"], oldest.GetProperty("paths").EnumerateArray().Select(path => path.GetString()));
        // 表に無い文は other（文そのものは返さない）
        Assert.Equal(ExcludeSources.Other, oldest.GetProperty("source").GetString());
        Assert.Equal(Day1, oldest.GetProperty("excludedAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);

        // 画面が外した（見張りの知らせと同じ道：excluded.json の場所から部分を見分けて読み直す）
        await _store.Excluded.SaveAsync([new ExcludedEntry { Hash = new string('4', 64), Paths = [@"D:\inbox\d.zip"], ExcludedAt = Day3, Reason = ExcludeSources.ResolveArchiveText }]);
        _sync.Note(_store.Paths.ExcludedFile);
        await _sync.ApplyPendingAsync();
        var after = await JsonOf("/v1/excluded");
        Assert.Equal([new string('4', 64)], Ids(after, "hash"));
        Assert.Equal("resolveArchive", after.GetProperty("items")[0].GetProperty("source").GetString());

        await AssertProblem("/v1/excluded?cursor=abc", "invalidCursor");
    }

    /// <summary>
    /// 外したファイルの記録が壊れても、ほかの答えは止めず、前の写しの記録で答え続ける（読み終えていない部分として次の見比べで読み直す）
    /// </summary>
    [Fact]
    public async Task 外したファイルの記録が壊れても_前の記録で答え_ほかの道も止めない()
    {
        await File.WriteAllTextAsync(_store.Paths.ExcludedFile, "{ 壊れた");
        _sync.Note(_store.Paths.ExcludedFile);
        await _sync.ApplyPendingAsync();

        Assert.Equal(3, (await JsonOf("/v1/excluded")).GetProperty("total").GetInt32());
        Assert.Equal(3, (await JsonOf("/v1/recent")).GetProperty("total").GetInt32());

        // 読み終えていないので2分ごとの見比べのたびに読み直すが、中身は変わらないので知らせない（改変の点検32と同じ）
        var sequence = (await JsonOf("/v1/changes")).GetProperty("sequence").GetInt64();
        for (var round = 0; round < 3; round++)
        {
            var everything = new Chmonos.Query.StoreChangeSet();
            everything.Add(Chmonos.Query.StorePart.Excluded);
            _sync.Enqueue(everything);
            await _sync.ApplyPendingAsync();
        }

        Assert.Equal(sequence, (await JsonOf("/v1/changes")).GetProperty("sequence").GetInt64());
    }

    [Fact]
    public async Task 商品が決まっていないファイルは_場所の順に推定の候補と記録を状態を潰さずに返す()
    {
        var first = await JsonOf("/v1/unresolved?limit=2");
        Assert.Equal(3, first.GetProperty("total").GetInt32());

        // 最初の場所の順（場所の無い物は先頭）
        Assert.Equal([new string('7', 64), new string('5', 64)], Ids(first, "hash"));
        var broken = first.GetProperty("items")[1];
        Assert.Equal([@"D:\inbox\a.zip", @"E:\copy\a.zip"], broken.GetProperty("paths").EnumerateArray().Select(path => path.GetString()));
        Assert.True(broken.GetProperty("archiveBroken").GetBoolean());
        Assert.Equal(["9700001"], broken.GetProperty("samePathItemIds").EnumerateArray().Select(id => id.GetString()));
        Assert.Equal("9799998", broken.GetProperty("notOnBooth").GetProperty("itemId").GetString());
        Assert.Equal(Day3, broken.GetProperty("notOnBooth").GetProperty("checkedAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, broken.GetProperty("zoneHostUrl").ValueKind);

        // zip の中の一覧は既定の答えに入れない（記録の大半で、長くなる）
        Assert.False(broken.TryGetProperty("contents", out _));

        var cursor = Uri.EscapeDataString(first.GetProperty("nextCursor").GetString()!);
        var second = await JsonOf($"/v1/unresolved?limit=2&cursor={cursor}");
        var last = second.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(["9700001", "9700002"], last.GetProperty("candidateItemIds").EnumerateArray().Select(id => id.GetString()));
        Assert.Equal("https://example.invalid/z.zip", last.GetProperty("zoneHostUrl").GetString());
        Assert.Equal(JsonValueKind.Null, last.GetProperty("notOnBooth").ValueKind);
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);

        await AssertProblem("/v1/unresolved?cursor=abc", "invalidCursor");
    }

    [Fact]
    public async Task 商品が決まっていないファイルは_記録が変わったら読み直して知らせ_壊れたら前の記録で答える()
    {
        Assert.Equal(3, (await JsonOf("/v1/unresolved")).GetProperty("total").GetInt32());
        var sequence = (await JsonOf("/v1/changes")).GetProperty("sequence").GetInt64();

        // 画面が確定した（見張りの知らせと同じ道で、変わった部分 unresolved を知らせる）
        await _store.Unresolved.SaveAsync([new UnresolvedFile { Hash = new string('8', 64), Paths = [@"D:\inbox\new.zip"], SizeBytes = 1, ModifiedAtUtc = Day1, FirstSeenAt = Day3 }]);
        _sync.Note(_store.Paths.UnresolvedFile);
        await _sync.ApplyPendingAsync();

        var changes = await JsonOf($"/v1/changes?since={sequence}");
        Assert.Contains(changes.GetProperty("changes").EnumerateArray(), change => change.GetProperty("parts").EnumerateArray().Any(part => part.GetString() == "unresolved"));
        Assert.Equal([new string('8', 64)], Ids(await JsonOf("/v1/unresolved"), "hash"));

        // 壊れた記録は前に読めた物で答える（ほかの道も止めない）
        await File.WriteAllTextAsync(_store.Paths.UnresolvedFile, "{ 壊れた");
        Assert.Equal([new string('8', 64)], Ids(await JsonOf("/v1/unresolved"), "hash"));
    }

    /// <summary>
    /// 人が足した変換の組は足した順に、端を商品の conversion.targets と同じ名前（avatarId・baseName）で返す。
    /// 記録の行は潰さない（端が空の行も、計算に使わないかは外の道具が端を見て分かる）。書いたら見張りの部分 conversionPairs で知らせて読み直す
    /// </summary>
    [Fact]
    public async Task 変換の組は_足した順に端とツールとメモを返し_書いたら読み直して知らせる()
    {
        var empty = await JsonOf("/v1/conversion-pairs");
        Assert.Equal(0, empty.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("nextCursor").ValueKind);
        var sequence = (await JsonOf("/v1/changes")).GetProperty("sequence").GetInt64();

        await _store.ConversionPairs.SaveAsync(new ConversionPairFile
        {
            Pairs =
            [
                new ConversionPair { From = ConversionEnd.Avatar("9700002"), To = ConversionEnd.Avatar("9700001"), Tool = "作り物の変換ツール", Memo = "袖を直す" },
                new ConversionPair { From = ConversionEnd.Base("作り物の素体"), To = ConversionEnd.Avatar("9700001") },
                new ConversionPair { From = ConversionEnd.Avatar("9700001"), To = new ConversionEnd() },
            ],
        });
        _sync.Note(_store.Paths.ConversionPairsFile);
        await _sync.ApplyPendingAsync();

        var changes = await JsonOf($"/v1/changes?since={sequence}");
        Assert.Contains(changes.GetProperty("changes").EnumerateArray(), change => change.GetProperty("parts").EnumerateArray().Any(part => part.GetString() == "conversionPairs"));

        var first = await JsonOf("/v1/conversion-pairs?limit=2");
        Assert.Equal(3, first.GetProperty("total").GetInt32());
        var pair = first.GetProperty("items")[0];
        Assert.Equal("9700002", pair.GetProperty("from").GetProperty("avatarId").GetString());
        Assert.Equal(JsonValueKind.Null, pair.GetProperty("from").GetProperty("baseName").ValueKind);
        Assert.Equal("9700001", pair.GetProperty("to").GetProperty("avatarId").GetString());
        Assert.Equal("作り物の変換ツール", pair.GetProperty("tool").GetString());
        Assert.Equal("袖を直す", pair.GetProperty("memo").GetString());

        var fromBase = first.GetProperty("items")[1];
        Assert.Equal("作り物の素体", fromBase.GetProperty("from").GetProperty("baseName").GetString());
        Assert.Equal(JsonValueKind.Null, fromBase.GetProperty("from").GetProperty("avatarId").ValueKind);
        Assert.Equal(JsonValueKind.Null, fromBase.GetProperty("tool").ValueKind);
        Assert.Equal(JsonValueKind.Null, fromBase.GetProperty("memo").ValueKind);

        var cursor = Uri.EscapeDataString(first.GetProperty("nextCursor").GetString()!);
        var second = await JsonOf($"/v1/conversion-pairs?limit=2&cursor={cursor}");
        var blank = second.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("9700001", blank.GetProperty("from").GetProperty("avatarId").GetString());
        Assert.Equal(JsonValueKind.Null, blank.GetProperty("to").GetProperty("avatarId").ValueKind);
        Assert.Equal(JsonValueKind.Null, blank.GetProperty("to").GetProperty("baseName").ValueKind);
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);

        // ほかの一覧の印は断る
        var foldersCursor = Uri.EscapeDataString((await JsonOf("/v1/folders?limit=1")).GetProperty("nextCursor").GetString()!);
        await AssertProblem($"/v1/conversion-pairs?cursor={foldersCursor}", "invalidCursor");
    }
}
