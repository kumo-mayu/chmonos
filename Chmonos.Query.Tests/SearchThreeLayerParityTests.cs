using System.Text.Json;
using Chmonos.App.Tests.Support;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Chmonos.Query.Search;

namespace Chmonos.Query.Tests;

/// <summary>
/// Core と API が、同じ保存した検索で、手で数えた同じ商品を返すか（試験の点検 2026-11-28 の高1・高2）。
/// Core の道は条件 <c>savedSearch</c>（SavedSearchMatcher）、API の道は保存した検索の一覧が戻した式（SavedSearchExport）を
/// 外のツールと同じく <c>POST /v1/search</c> の本文として読んで探したとき（textOptions の対象と区別もここを通る）。
/// 画面の道は App.Tests の同じ名前の試験が、同じ作り物（<see cref="SearchParityFixture"/>）と同じ期待で見る。
/// </summary>
public sealed class SearchThreeLayerParityTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 12, 0, 0, TimeSpan.FromHours(9));

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

    private static string[] Ids(QuerySnapshot snapshot, SearchExpression? where, TextOptionsRequest? options = null)
        => SearchEngine.Filter(snapshot, where, true, true, options, Now).Select(item => item.Id).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task 条件savedSearchと_戻した式で探した結果は_手で数えた商品と同じ()
    {
        var store = new DataStore(new AppPaths(_root));
        await store.Avatars.SaveAsync(SearchParityFixture.Registry());
        foreach (var item in SearchParityFixture.Items())
        {
            await store.Items.SaveAsync(item);
        }

        await store.SavedSearches.SaveAsync(SearchParityFixture.SavedSearches());
        var snapshot = await QuerySnapshot.LoadAsync(store);
        Assert.Equal(6, Ids(snapshot, null).Length);

        var page = SavedSearchApiList.Page(snapshot, null, null, Now);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(page, QueryJson.Context.SavedSearchPage));
        var exported = json.RootElement.GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("name").GetString()!);

        foreach (var @case in SearchParityFixture.Cases())
        {
            var core = Ids(snapshot, new SearchExpression { Condition = new ConditionRequest { Kind = "savedSearch", Values = [@case.Name] } });
            Assert.True(@case.Expected.SequenceEqual(core), $"Core {@case.Name}: 期待 [{string.Join(",", @case.Expected)}] 実際 [{string.Join(",", core)}]");

            var item = exported[@case.Name];
            Assert.Equal(0, item.GetProperty("unconverted").GetArrayLength());
            var request = JsonSerializer.Deserialize(item.GetProperty("search").GetRawText(), QueryJson.Context.SearchRequest)!;
            var api = Ids(snapshot, request.Where, request.TextOptions);
            Assert.True(@case.Expected.SequenceEqual(api), $"API {@case.Name}: 期待 [{string.Join(",", @case.Expected)}] 実際 [{string.Join(",", api)}]");
        }
    }
}
