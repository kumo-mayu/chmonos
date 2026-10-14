using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Chmonos.Query.Search;

namespace Chmonos.Query.Tests;

/// <summary>検索の式（AND・OR・NOT）・並べ替えと続きの印・選択肢の件数（docs/spec/local-api-shapes.md）。</summary>
public sealed class SearchEngineTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.FromHours(9));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-query-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static ItemRecord Item(string id, string name, Func<BoothBlock, BoothBlock>? booth = null, Func<LocalBlock, LocalBlock>? local = null)
    {
        var record = new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = name },
            Local = new LocalBlock { LocalFiles = [new LocalFileRecord { Hash = id.PadLeft(64, '0'), Paths = [$@"D:\files\{id}.zip"], SizeBytes = 1 }] },
        };
        return record with { Booth = booth?.Invoke(record.Booth) ?? record.Booth, Local = local?.Invoke(record.Local) ?? record.Local };
    }

    /// <summary>作り物の5件（名前・ID は作り物）。</summary>
    private async Task<QuerySnapshot> SnapshotAsync()
    {
        var store = new DataStore(new AppPaths(_root));
        ItemRecord[] items =
        [
            Item("9700001", "夏の水着", booth => booth with { Category = new BoothCategory { Id = 1, Name = "3D衣装" }, Tags = ["夏"], WishListsCount = 300 },
                local => local with { Purchases = [new Purchase { Kind = PurchaseKind.ForSelf, Price = 1500 }], IsFavorite = true }),
            Item("9700002", "冬のコート", booth => booth with { Category = new BoothCategory { Id = 1, Name = "3D衣装" }, Tags = ["冬"], WishListsCount = 100 },
                local => local with { Purchases = [new Purchase { Kind = PurchaseKind.ForSelf, Price = 3000 }] }),
            Item("9700003", "夏の帽子", booth => booth with { Category = new BoothCategory { Id = 2, Name = "3D小物" }, Tags = ["夏"], WishListsCount = 300, IsAdult = true }),
            Item("9700004", "素材集", booth => booth with
            {
                WishListsCount = 50,
                Description = "作り物の説明。秘密の手順が書いてある",
                H2Sections = [new H2Section { Heading = "使い方", Text = "見出しの下の作り物の文" }],
            }, local => local with { IsHidden = true }),
            Item("9700005", "対応つきの髪", booth => booth with { Category = new BoothCategory { Id = 3, Name = "3D装飾品" }, WishListsCount = 10 },
                local => local with { Avatars = [new AvatarLink { AvatarItemId = "7000001", Source = AvatarLinkSource.SupportSection }] }),
        ];

        foreach (var item in items)
        {
            await store.Items.SaveAsync(item);
        }

        return await QuerySnapshot.LoadAsync(store);
    }

    private static string[] Ids(QuerySnapshot snapshot, SearchExpression? where, bool includeHidden = true, bool includeAdult = true)
        => SearchEngine.Filter(snapshot, where, includeHidden, includeAdult, null, Now).Select(item => item.Id).ToArray();

    private static SearchExpression Condition(ConditionRequest request) => new() { Condition = request };

    /// <summary>
    /// 要求で省いた「非表示・R-18 も返す」は既定の true になる（2026-10-14・使い手の試作で見つかった：省くと非表示の商品が外れ、条件「非表示」が0件だった）。
    /// API が読むのと同じ、コードから作った読み方で読む
    /// </summary>
    [Fact]
    public void 省いた非表示とR18の指定は_既定のtrueで読む()
    {
        var search = JsonSerializer.Deserialize("""{"where":{"text":"x"}}""", QueryJson.Context.SearchRequest)!;
        Assert.True(search.IncludeHidden);
        Assert.True(search.IncludeAdult);

        var facets = JsonSerializer.Deserialize("{}", QueryJson.Context.FacetsRequest)!;
        Assert.True(facets.IncludeHidden);
        Assert.True(facets.IncludeAdult);

        Assert.False(JsonSerializer.Deserialize("""{"includeHidden":false}""", QueryJson.Context.SearchRequest)!.IncludeHidden);
    }

    /// <summary>属性の範囲は、省いた側を端まで（0・100）で探す（2026-10-14・前は省くと 0 と読み、黙って0件だった）。</summary>
    [Fact]
    public async Task 属性の範囲は_省いた側を端までとして探す()
    {
        var store = new DataStore(new AppPaths(_root));
        await store.Items.SaveAsync(Item("9700011", "作り物の高い", local: local => local with { Attributes = new Dictionary<string, int> { ["かわいさ"] = 80 } }));
        await store.Items.SaveAsync(Item("9700012", "作り物の低い", local: local => local with { Attributes = new Dictionary<string, int> { ["かわいさ"] = 30 } }));
        var snapshot = await QuerySnapshot.LoadAsync(store);

        SearchExpression? Where(string range) => JsonSerializer.Deserialize(
            """{"where":{"condition":{"kind":"attribute","ranges":[{"name":"かわいさ" """ + range + "}]}}}", QueryJson.Context.SearchRequest)!.Where;

        Assert.Equal(["9700011"], Ids(snapshot, Where(""","min":50""")));
        Assert.Equal(["9700012"], Ids(snapshot, Where(""","max":50""")));
        Assert.Equal(["9700011", "9700012"], Ids(snapshot, Where("")));
    }

    [Fact]
    public async Task 文字列と条件を_ANDとORとNOTで組める()
    {
        var snapshot = await SnapshotAsync();

        Assert.Equal(["9700001", "9700003"], Ids(snapshot, new() { Text = "夏" }));
        Assert.Equal(["9700001"], Ids(snapshot, new()
        {
            And = [new() { Text = "夏" }, Condition(new() { Kind = "category", Values = ["3D衣装"] })],
        }));
        Assert.Equal(["9700001", "9700002", "9700003"], Ids(snapshot, new()
        {
            Or = [new() { Text = "夏" }, Condition(new() { Kind = "category", Values = ["3D衣装"] })],
        }));
        Assert.Equal(["9700002", "9700004", "9700005"], Ids(snapshot, new() { Not = new() { Text = "夏" } }));

        // NOT (夏 AND 3D衣装) = 夏でない OR 3D衣装でない
        Assert.Equal(["9700002", "9700003", "9700004", "9700005"], Ids(snapshot, new()
        {
            Not = new() { And = [new() { Text = "夏" }, Condition(new() { Kind = "category", Values = ["3D衣装"] })] },
        }));
    }

    [Fact]
    public async Task 説明文は写しに持たず_本文を探すときだけファイルから読む()
    {
        var snapshot = await SnapshotAsync();

        Assert.All(snapshot.Items, item => Assert.Null(item.Booth.Description));
        Assert.All(snapshot.Items, item => Assert.Empty(item.Booth.H2Sections));

        // 既定の対象（名前・ショップ・メモ）に本文は入らないので、前置き main: で探す
        Assert.Empty(Ids(snapshot, new() { Text = "秘密" }));
        Assert.Equal(["9700004"], Ids(snapshot, new() { Text = "main:秘密" }));
        Assert.Equal(["9700004"], Ids(snapshot, new() { Text = "main:見出しの下" }));
    }

    [Fact]
    public async Task NOTの範囲は_画面の除くと同じく_値の分からない商品も外す()
    {
        var snapshot = await SnapshotAsync();
        var paid = Condition(new() { Kind = "price", Min = 1000, Max = 2000 });

        Assert.Equal(["9700001"], Ids(snapshot, paid));

        // 払った額を入れていない3件は「範囲の外」とは言えないので、NOT でも出さない
        Assert.Equal(["9700002"], Ids(snapshot, new() { Not = paid }));

        // NOT を2回重ねると元に戻る
        Assert.Equal(["9700001"], Ids(snapshot, new() { Not = new() { Not = paid } }));
    }

    [Fact]
    public async Task 絞らない値の条件は_すべてに当たり_NOTで何にも当たらない()
    {
        var snapshot = await SnapshotAsync();
        var both = Condition(new() { Kind = "favorite", Choice = "both" });
        var noBounds = Condition(new() { Kind = "wishCount" });

        Assert.Equal(5, Ids(snapshot, both).Length);
        Assert.Empty(Ids(snapshot, new() { Not = both }));
        Assert.Equal(5, Ids(snapshot, noBounds).Length);
        Assert.Empty(Ids(snapshot, new() { Not = noBounds }));
    }

    [Fact]
    public async Task 非表示とR18は既定で返し_外すこともできる()
    {
        var snapshot = await SnapshotAsync();

        Assert.Equal(5, Ids(snapshot, null).Length);
        Assert.Equal(["9700001", "9700002", "9700005"], Ids(snapshot, null, includeHidden: false, includeAdult: false));
    }

    [Fact]
    public async Task 対応アバターは画面と同じ数え方で当てる()
    {
        var snapshot = await SnapshotAsync();

        Assert.Equal(["9700005"], Ids(snapshot, Condition(new() { Kind = "avatar", Avatars = ["7000001"] })));

        // 対応の指定が無い商品だけ
        Assert.Equal(["9700001", "9700002", "9700003", "9700004"], Ids(snapshot, Condition(new() { Kind = "avatar", Matched = false, Unspecified = true })));
    }

    [Theory]
    [InlineData("""{"condition":{"kind":"無い種類"}}""", "unknownCondition")]
    [InlineData("""{"condition":{"kind":"favorite","choice":"maybe"}}""", "invalidChoice")]
    [InlineData("""{"condition":{"kind":"gift","options":{"includeEverything":true}}}""", "invalidOption")]
    [InlineData("""{"condition":{"kind":"boothTag","values":["夏"],"match":"most"}}""", "invalidCondition")]
    [InlineData("""{"condition":{"kind":"avatar","matched":false,"unspecified":false}}""", "invalidCondition")]
    [InlineData("""{"text":"夏","condition":{"kind":"favorite"}}""", "invalidExpression")]
    [InlineData("""{}""", "invalidExpression")]
    public async Task 読めない式は_決まった名前の誤りで返す(string json, string code)
    {
        var snapshot = await SnapshotAsync();
        var where = JsonSerializer.Deserialize("{\"where\":" + json + "}", QueryJson.Context.SearchRequest)!.Where;

        var error = Assert.Throws<QueryException>(() => SearchEngine.Filter(snapshot, where, true, true, null, Now));
        Assert.Equal(400, error.Status);
        Assert.Equal(code, error.Code);
    }

    [Fact]
    public async Task 深すぎる式は断る()
    {
        var snapshot = await SnapshotAsync();
        var where = new SearchExpression { Text = "夏" };
        for (var depth = 0; depth < SearchEngine.MaxDepth; depth++)
        {
            where = new SearchExpression { Not = where };
        }

        Assert.Equal("expressionTooDeep", Assert.Throws<QueryException>(() => Ids(snapshot, where)).Code);
    }

    [Fact]
    public async Task 空の枝を大量に並べても_段の数の上限で断る()
    {
        var snapshot = await SnapshotAsync();

        // 葉が1つも無いので、葉の数の上限には触れない（点検30）
        var wide = new SearchExpression { And = Enumerable.Range(0, SearchEngine.MaxNodes).Select(_ => new SearchExpression { And = [] }).ToList() };
        Assert.Equal("expressionTooLarge", Assert.Throws<QueryException>(() => Ids(snapshot, wide)).Code);

        // 上限の中なら、空の and はすべてに当たる
        Assert.Equal(5, Ids(snapshot, new() { And = [new() { And = [] }, new() { Or = [new() { And = [] }] }] }).Length);
    }

    [Fact]
    public async Task 並べ替えて続きの印で分けると_全部を1回ずつ返す()
    {
        var snapshot = await SnapshotAsync();
        var sort = new[] { new SortRequest("wishCount", "desc") };
        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = SearchEngine.Search(snapshot, new SearchRequest { Sort = sort, Limit = 2, Cursor = cursor }, Now);
            Assert.Equal(5, page.Total);
            seen.AddRange(page.Items.Select(item => item.Id));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.Equal(3, pages);

        // スキ数の多い順。同じ数（300）は ID の順
        Assert.Equal(["9700001", "9700003", "9700002", "9700004", "9700005"], seen);
    }

    /// <summary>
    /// 要約に添えた並べる欄の値は、並べ替えの鍵と同じ（Web の API-GAPS G8）。入手日は購入1件の日付のうち最も早い物で、
    /// 商品の入手日の欄とは違い得る。払った額は自分用だけで、額の無い購入しか無ければ null
    /// </summary>
    [Fact]
    public async Task 要約に添えた並べる欄の値は_並べ替えの鍵と同じ()
    {
        var store = new DataStore(new AppPaths(_root));
        ItemRecord[] items =
        [
            // 購入の日付が商品の入手日より早い・自分用と贈った
            Item("9600001", "作り物A", booth => booth with { PublishedAt = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.FromHours(9)), WishListsCount = 7, PriceText = "¥ 500" },
                local => local with
                {
                    AcquiredAt = new DateOnly(2025, 6, 1),
                    Purchases =
                    [
                        new Purchase { Kind = PurchaseKind.ForSelf, Price = 500, PurchasedAt = new DateOnly(2025, 3, 1) },
                        new Purchase { Kind = PurchaseKind.Given, Price = 800 },
                    ],
                }),

            // 購入記録が無く、商品の入手日だけ
            Item("9600002", "作り物B", local: local => local with { AcquiredAt = new DateOnly(2024, 12, 31) }),

            // 額の無い購入だけ（払った額は分からない）・無料
            Item("9600003", "作り物C", local: local => local with { Purchases = [new Purchase { Kind = PurchaseKind.ForSelf }] }),
            Item("9600004", "作り物D", local: local => local with { Purchases = [new Purchase { Kind = PurchaseKind.ForSelf, Price = 0 }] }),
        ];
        foreach (var item in items)
        {
            await store.Items.SaveAsync(item);
        }

        var snapshot = await QuerySnapshot.LoadAsync(store);
        var summaries = SearchEngine.Search(snapshot, new SearchRequest(), Now).Items.ToDictionary(summary => summary.Id);
        var keys = SearchEngine.SortOf([new("publishedAt"), new("acquiredAt"), new("wishCount"), new("paid")]);

        foreach (var item in snapshot.Items)
        {
            var summary = summaries[item.Id];
            Assert.Equal(keys[0].Value(item, snapshot).Number, summary.PublishedAt?.UtcTicks);
            Assert.Equal(keys[1].Value(item, snapshot).Number, summary.AcquiredAt?.DayNumber);
            Assert.Equal(keys[2].Value(item, snapshot).Number, summary.WishCount);
            Assert.Equal(keys[3].Value(item, snapshot).Number, summary.Paid);
        }

        Assert.Equal(new DateOnly(2025, 3, 1), summaries["9600001"].AcquiredAt);
        Assert.Equal(new DateOnly(2025, 6, 1), ItemMapper.Detail(snapshot.ItemsById["9600001"], snapshot).AcquiredAt);
        Assert.Equal(500, summaries["9600001"].Paid);
        Assert.Equal("¥ 500", summaries["9600001"].PriceText);
        Assert.Equal(new DateOnly(2024, 12, 31), summaries["9600002"].AcquiredAt);
        Assert.Null(summaries["9600003"].Paid);
        Assert.Equal(0, summaries["9600004"].Paid);
    }

    /// <summary>
    /// 送れる unitypackage を持つかで絞る（Unity の API-GAPS 3-9）。判断は <c>/v1/unitypackages</c> と同じ：
    /// 見つからないファイル・外したファイル・見つからないフォルダの中の物は送れないので数えない
    /// </summary>
    [Fact]
    public async Task 送れるunitypackageを持つかで絞ると_unitypackagesの一覧と同じ商品に当たる()
    {
        var store = new DataStore(new AppPaths(_root));
        UnityPackageSummary[] package = [new UnityPackageSummary { Entry = "a.unitypackage", Roots = ["Assets/A"] }];
        ItemRecord[] items =
        [
            Item("9600011", "zipの中にある", local: local => local with { LocalFiles = [local.LocalFiles[0] with { UnityPackages = package }] }),
            Item("9600012", "zipの中に無い"),
            Item("9600013", "フォルダの中にある", local: local => local with
            {
                LocalFiles = [],
                LocalFolders = [new LocalFolderRecord { Path = @"D:\folders\c", UnityPackages = ["c.unitypackage"] }],
            }),
            Item("9600014", "見つからないzip", local: local => local with
            {
                LocalFiles = [local.LocalFiles[0] with { UnityPackages = package, MissingSince = Now }],
            }),
            Item("9600015", "外したzip", local: local => local with { LocalFiles = [local.LocalFiles[0] with { UnityPackages = package, Detached = true }] }),
            Item("9600016", "見つからないフォルダ", local: local => local with
            {
                LocalFiles = [],
                LocalFolders = [new LocalFolderRecord { Path = @"D:\folders\f", UnityPackages = ["f.unitypackage"], MissingSince = Now }],
            }),
        ];
        foreach (var item in items)
        {
            await store.Items.SaveAsync(item);
        }

        var snapshot = await QuerySnapshot.LoadAsync(store);
        var listed = Catalogs.UnityPackages(snapshot, null).Select(entry => entry.ItemId).Distinct().Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["9600011", "9600013"], listed);

        string[] Search(bool? has) => SearchEngine.Search(snapshot, new SearchRequest { HasUnityPackage = has, Sort = [new("id")] }, Now)
            .Items.Select(item => item.Id).ToArray();
        Assert.Equal(listed, Search(true));
        Assert.Equal(["9600012", "9600014", "9600015", "9600016"], Search(false));
        Assert.Equal(6, Search(null).Length);

        var facets = SearchEngine.Facets(snapshot, new FacetsRequest { HasUnityPackage = true, Fields = ["category"] }, Now);
        Assert.Equal(2, facets.Total);

        var request = JsonSerializer.Deserialize("""{"hasUnityPackage":true}""", QueryJson.Context.SearchRequest)!;
        Assert.True(request.HasUnityPackage);
    }

    [Fact]
    public async Task 続きの印は_同じ並べ方にだけ使える()
    {
        var snapshot = await SnapshotAsync();
        var first = SearchEngine.Search(snapshot, new SearchRequest { Sort = [new SortRequest("wishCount")], Limit = 2 }, Now);

        Assert.Equal("invalidCursor", Assert.Throws<QueryException>(() =>
            SearchEngine.Search(snapshot, new SearchRequest { Sort = [new SortRequest("name")], Cursor = first.NextCursor }, Now)).Code);
        Assert.Equal("invalidCursor", Assert.Throws<QueryException>(() =>
            SearchEngine.Search(snapshot, new SearchRequest { Cursor = "読めない印" }, Now)).Code);
    }

    [Fact]
    public async Task 選択肢の件数は_式に当たった商品の中で数える()
    {
        var snapshot = await SnapshotAsync();
        var answer = SearchEngine.Facets(snapshot, new FacetsRequest { Where = new() { Text = "夏" }, Fields = ["category", "boothTag", "avatar"] }, Now);

        Assert.Equal(2, answer.Total);
        Assert.Equal([("3D小物", 1), ("3D衣装", 1)], answer.Fields["category"].Select(value => (value.Value, value.Count)));
        Assert.Equal([("夏", 2)], answer.Fields["boothTag"].Select(value => (value.Value, value.Count)));
        Assert.Equal(new AvatarGroupCounts(0, 2), answer.AvatarGroups);

        Assert.Equal("unknownFacet", Assert.Throws<QueryException>(() =>
            SearchEngine.Facets(snapshot, new FacetsRequest { Fields = ["price"] }, Now)).Code);
    }

    [Fact]
    public async Task 要求のJSONは_camelCaseで読み_答えは日本語のまま書く()
    {
        var snapshot = await SnapshotAsync();
        var request = JsonSerializer.Deserialize(
            """{"where":{"and":[{"text":"夏"},{"not":{"condition":{"kind":"adult","choice":"adult"}}}]},"sort":[{"field":"id"}],"limit":10}""",
            QueryJson.Context.SearchRequest)!;

        var page = SearchEngine.Search(snapshot, request, Now);
        var json = JsonSerializer.Serialize(page, QueryJson.Context.SearchPage);

        Assert.Equal(["9700001"], page.Items.Select(item => item.Id));
        Assert.Contains("\"name\":\"夏の水着\"", json);
        Assert.Contains("\"nextCursor\":null", json);
    }
}
