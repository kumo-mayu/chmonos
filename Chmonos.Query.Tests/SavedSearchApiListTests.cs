using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Chmonos.Query.Search;

namespace Chmonos.Query.Tests;

/// <summary>
/// 保存した検索の一覧（<c>/v1/saved-searches</c>・docs/spec/local-api-internal.md ⑤）。
/// 戻した式を <c>POST /v1/search</c> の本文として読んで探した答えが、条件 <c>savedSearch</c> の答えと同じかを、条件の種類を混ぜて確かめる。
/// </summary>
public sealed class SavedSearchApiListTests : IDisposable
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

    private static SearchModuleState Module(string kind, Func<SearchModuleState, SearchModuleState>? change = null)
        => change?.Invoke(new SearchModuleState { Kind = kind }) ?? new SearchModuleState { Kind = kind };

    /// <summary>作り物の5件と、種類を混ぜた保存した検索（名前・ID は作り物）。</summary>
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
            Item("9700004", "素材集", booth => booth with { WishListsCount = 50 }, local => local with { IsHidden = true }),
            Item("9700005", "対応つきの髪", booth => booth with { Category = new BoothCategory { Id = 3, Name = "3D装飾品" }, WishListsCount = 10 },
                local => local with { Avatars = [new AvatarLink { AvatarItemId = "7000001", Source = AvatarLinkSource.SupportSection }] }),
        ];

        foreach (var item in items)
        {
            await store.Items.SaveAsync(item);
        }

        await store.SavedSearches.SaveAsync(new Chmonos.Core.Services.SavedSearchList
        {
            Entries =
            [
                new SearchHistoryEntry
                {
                    Name = "文字と対象", Text = "夏", Targets = ["name", "知らない対象"], KanaInsensitive = true, Folder = "季節",
                    Modules = [Module("Category", state => state with { Items = ["3D衣装", "3D小物"] }), Module("Price", state => state with { Min = "", Max = "2000" })],
                },

                // 範囲の同じ種類は「どれかに当たる」、除くは全部通る
                new SearchHistoryEntry
                {
                    Name = "範囲", Sort = "名前順", View = ResultView.List,
                    Modules =
                    [
                        Module("WishList", state => state with { Min = "0", Max = "50" }),
                        Module("WishList", state => state with { Min = "250", Max = "400" }),
                        Module("WishList", state => state with { Min = "290", MaxEnabled = false, Exclude = true }),
                    ],
                },
                new SearchHistoryEntry
                {
                    Name = "選ぶと除く", KeepSort = true, KeepView = true,
                    Modules =
                    [
                        Module("Adult", state => state with { Choice = "general" }),
                        Module("Avatar", state => state with { Items = ["avatar:7000001"], Exclude = true }),
                        Module("BoothTag", state => state with { Items = ["冬"], Enabled = false }),
                        Module("Shop", state => state with { Items = [] }),
                    ],
                },
                new SearchHistoryEntry
                {
                    Name = "入れ子",
                    Modules =
                    [
                        Module("SavedSearch", state => state with { Items = ["文字と対象"], IgnoreText = true }),
                        Module("PublishedAt", state => state with { Min = "", Max = "", Exclude = true }),
                    ],
                },
                new SearchHistoryEntry
                {
                    Name = "書けない物", Text = "夏", SearchAlternates = true,
                    Modules =
                    [
                        Module("UnityProjectContents", state => state with { Items = ["D:/proj"] }),
                        Module("Recent", state => state with { Min = "0", Max = "7", ShowUnspecified = true }),
                        Module("FutureKind", state => state with { Items = ["x"] }),
                    ],
                },
                new SearchHistoryEntry { Name = "輪A", Modules = [Module("SavedSearch", state => state with { Items = ["輪B"] })] },
                new SearchHistoryEntry { Name = "輪B", Modules = [Module("SavedSearch", state => state with { Items = ["輪A"] })] },
            ],
        });

        return await QuerySnapshot.LoadAsync(store);
    }

    private static string[] Ids(QuerySnapshot snapshot, SearchExpression? where, TextOptionsRequest? options = null)
        => SearchEngine.Filter(snapshot, where, true, true, options, Now).Select(item => item.Id).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task 戻した式を検索の本文として読むと_条件savedSearchと同じ商品に当たる()
    {
        var snapshot = await SnapshotAsync();
        var page = SavedSearchApiList.Page(snapshot, null, null, Now);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(page, QueryJson.Context.SavedSearchPage));

        foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()!;
            if (item.GetProperty("unconverted").GetArrayLength() > 0 || item.GetProperty("inCycle").GetBoolean())
            {
                continue;
            }

            // 外のツールと同じ道：答えの search をそのまま検索の本文として読む
            var request = JsonSerializer.Deserialize(item.GetProperty("search").GetRawText(), QueryJson.Context.SearchRequest)!;
            var expected = Ids(snapshot, new SearchExpression { Condition = new ConditionRequest { Kind = "savedSearch", Values = [name] } });
            Assert.Equal(expected, Ids(snapshot, request.Where, request.TextOptions));
        }

        // search の中は値のある欄だけ（null を書かない）。外側の欄は答えの決まりどおり null も書く
        static IEnumerable<JsonElement> Walk(JsonElement element)
            => element.ValueKind switch
            {
                JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Walk(property.Value).Prepend(property.Value)),
                JsonValueKind.Array => element.EnumerateArray().SelectMany(Walk),
                _ => [],
            };
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.All(items, item => Assert.DoesNotContain(Walk(item.GetProperty("search")), value => value.ValueKind == JsonValueKind.Null));
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("folder").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[2].GetProperty("sort").ValueKind);
        Assert.False(items[2].GetProperty("search").TryGetProperty("textOptions", out _));

        // 確かめた物が空振りでない（当たる物も外れる物もある）
        Assert.Equal(["9700001"], Ids(snapshot, new SearchExpression { Condition = new ConditionRequest { Kind = "savedSearch", Values = ["文字と対象"] } }));
        Assert.Equal(["9700004", "9700005"], Ids(snapshot, new SearchExpression { Condition = new ConditionRequest { Kind = "savedSearch", Values = ["範囲"] } }));
        Assert.Equal(["9700001", "9700002", "9700004"], Ids(snapshot, new SearchExpression { Condition = new ConditionRequest { Kind = "savedSearch", Values = ["選ぶと除く"] } }));
    }

    [Fact]
    public async Task 一覧は人が決めた並びで_名前とフォルダと含める物と要約を出す()
    {
        var snapshot = await SnapshotAsync();
        var page = SavedSearchApiList.Page(snapshot, null, null, Now);

        Assert.Equal(["文字と対象", "範囲", "選ぶと除く", "入れ子", "書けない物", "輪A", "輪B"], page.Items.Select(item => item.Name).ToArray());
        Assert.Equal(7, page.Total);
        Assert.Null(page.NextCursor);

        var text = page.Items[0];
        Assert.Equal("季節", text.Folder);
        Assert.Equal("夏", text.Text);
        Assert.StartsWith("夏", text.Summary);
        Assert.Equal("入手日が新しい順", text.Sort);
        Assert.Equal("card", text.View);
        Assert.False(text.KeepSort || text.KeepView);
        Assert.Equal(["name"], text.Search.TextOptions!.Targets);
        Assert.False(text.Search.TextOptions.KanaSensitive);

        Assert.Equal(("名前順", "list"), (page.Items[1].Sort, page.Items[1].View));
        var kept = page.Items[2];
        Assert.Equal((null, null, true, true), (kept.Sort, kept.View, kept.KeepSort, kept.KeepView));
        Assert.Null(kept.Search.TextOptions);

        // 効かせていない・絞らない条件は入れない（選ぶ条件と、除く対応アバターの2つだけ）
        var and = kept.Search.Where!.And!;
        Assert.Equal(2, and.Count);
        Assert.Equal("adult", and[0].Condition!.Kind);
        Assert.Equal("avatar", and[1].Not!.Condition!.Kind);

        // 入れ子は名前のまま。日付の無い作り物では、空欄の境の日付の条件は絞らないので入らない
        var nested = page.Items[3].Search.Where!.Condition!;
        Assert.Equal(("savedSearch", false), (nested.Kind, nested.UseText));

        Assert.Null(page.Items[5].Search.Where!.Condition!.UseText);
        Assert.True(page.Items[5].InCycle && page.Items[6].InCycle);
        Assert.False(page.Items[0].InCycle);
    }

    [Fact]
    public async Task 書けない条件と別表記は_式から落としてunconvertedで言う()
    {
        var snapshot = await SnapshotAsync();
        var item = SavedSearchApiList.Page(snapshot, null, null, Now).Items.Single(entry => entry.Name == "書けない物");

        Assert.Equal(
            [("searchAlternates", "searchAlternates"), ("UnityProjectContents", "unsupportedKind"), ("recent", "recordlessRecent"), ("FutureKind", "unsupportedKind")],
            item.Unconverted.Select(part => (part.Kind, part.Reason)).ToArray());
        Assert.Equal("夏", item.Search.Where!.Text);
    }

    [Fact]
    public async Task 続きの印で分けて返し_別の一覧の印は断る()
    {
        var snapshot = await SnapshotAsync();
        var first = SavedSearchApiList.Page(snapshot, null, 3, Now);
        Assert.Equal(["文字と対象", "範囲", "選ぶと除く"], first.Items.Select(item => item.Name).ToArray());
        Assert.NotNull(first.NextCursor);

        var second = SavedSearchApiList.Page(snapshot, first.NextCursor, 200, Now);
        Assert.Equal(["入れ子", "書けない物", "輪A", "輪B"], second.Items.Select(item => item.Name).ToArray());
        Assert.Null(second.NextCursor);

        Assert.Equal("invalidCursor", Assert.Throws<QueryException>(() => SavedSearchApiList.Page(snapshot, "abc", 10, Now)).Code);
        var recentCursor = ListPages.Take(new[] { 1, 2 }, value => [ListPages.Text(value)], (left, right) => string.CompareOrdinal(left[0], right[0]), "recent", 1, null, 1).NextCursor;
        Assert.Equal("invalidCursor", Assert.Throws<QueryException>(() => SavedSearchApiList.Page(snapshot, recentCursor, 10, Now)).Code);
    }
}
