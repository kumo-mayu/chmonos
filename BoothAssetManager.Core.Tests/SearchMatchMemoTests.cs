using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 照合の答えを商品ごとに覚えて使い回す形（2026-09-24）。覚えた答えで結果が変わらないこと——
/// 毎回まっさらな材料で照らした答えと、同じ材料を何度も・順を変えて・別のスレッドから照らした答えが揃うことを確かめる。
/// </summary>
public class SearchMatchMemoTests
{
    private static readonly string[][] Items =
    [
        ["指輪モデル", "Ring Model の説明", @"D:\BOOTH\ring_v1.zip", "しぐにゃも"],
        ["ＶＲＣｈａｔ用 リボン", "ribbon とりぼん", @"D:\BOOTH\Ribbon.unitypackage", "ショップ"],
        ["しっぽセット", "Tail set／テール", @"E:\素材\tail.zip", "tail shop"],
        ["カナのなまえ", "かなのほんぶん", @"D:\カナ\file.txt", "カナ屋"],
        ["Black or White", "無料 セット", @"D:\bw.zip", "BW"],
    ];

    private static SearchHaystack Hay(string[] values) => SearchHaystack.FromValues(
        new Dictionary<SearchField, string[]>
        {
            [SearchField.Name] = [values[0]],
            [SearchField.Main] = [values[1]],
            [SearchField.Path] = [values[2]],
            [SearchField.File] = [Path.GetFileName(values[2])],
            [SearchField.Shop] = [values[3]],
        },
        "りんぐもでる");

    private static readonly string[] Queries =
    [
        "指輪", "ring", "RING", "リボン", "りぼん", "ｒｉｂｂｏｎ", "しっぽ OR tail", "-無料", "セット -無料", "\"Ring Model\"",
        "name:カナ", "path:D:", "file:zip", "(かな OR カナ) -屋", "りんぐ", "リング",
    ];

    private static readonly SearchOptions[] Options =
    [
        SearchOptions.Default,
        SearchOptions.Default with { KanaSensitive = false },
        SearchOptions.Default with { CaseSensitive = true },
        SearchOptions.Default with { WidthSensitive = true },
        SearchOptions.Default with { CaseSensitive = true, WidthSensitive = true, KanaSensitive = false },
        SearchOptions.Default with { Targets = new HashSet<SearchField> { SearchField.Main, SearchField.File } },
        SearchOptions.Default with { IncludeReadings = true },
        SearchOptions.Default with { IncludeReadings = true, KanaSensitive = false },
    ];

    /// <summary>毎回まっさらな材料で照らした答え（覚えた答えを使えない）。</summary>
    private static bool Fresh(string query, string[] item, SearchOptions options)
        => SearchQuery.Matches(SearchQuery.Parse(query), Hay(item), options);

    [Fact]
    public void RememberedAnswersEqualFreshOnes()
    {
        var hays = Items.Select(Hay).ToList();

        // 1回の絞り込みのように、同じ式と切り替えで全商品を何度も照らす。式・切り替えを変えた後に戻っても揃う
        for (var round = 0; round < 2; round++)
        {
            foreach (var options in Options)
            {
                foreach (var query in Queries)
                {
                    var node = SearchQuery.Parse(query);
                    for (var pass = 0; pass < 4; pass++)
                    {
                        for (var i = 0; i < Items.Length; i++)
                        {
                            Assert.True(
                                Fresh(query, Items[i], options) == SearchQuery.Matches(node, hays[i], options),
                                $"{query} / {options} / {Items[i][0]}");
                        }
                    }
                }
            }
        }
    }

    /// <summary>同じ式でも、切り替えを変えれば（新しい切り替えの物なら）照らし直す。前の答えを返さない。</summary>
    [Fact]
    public void ChangingOptionsWithTheSameNodeRecomputes()
    {
        var hay = Hay(Items[0]);
        var node = SearchQuery.Parse("説明");

        Assert.False(SearchQuery.Matches(node, hay, SearchOptions.Default));
        var withBody = SearchOptions.Default with { Targets = new HashSet<SearchField> { SearchField.Name, SearchField.Main } };
        Assert.True(SearchQuery.Matches(node, hay, withBody));
        Assert.False(SearchQuery.Matches(node, hay, SearchOptions.Default));
    }

    /// <summary>別のスレッドから別の式で同じ材料を照らしても、答えが混ざらない。</summary>
    [Fact]
    public void ConcurrentMatchingDoesNotMixAnswers()
    {
        var hays = Items.Select(Hay).ToList();
        var expected = Queries.Select(query => Items.Select(item => Fresh(query, item, SearchOptions.Default)).ToArray()).ToArray();

        Parallel.For(0, 400, n =>
        {
            var q = n % Queries.Length;
            var node = SearchQuery.Parse(Queries[q]);
            for (var i = 0; i < hays.Count; i++)
            {
                Assert.Equal(expected[q][i], SearchQuery.Matches(node, hays[i], SearchOptions.Default));
            }
        });
    }

    /// <summary>覚えた物（ひらがなに寄せた語・対象の配列）は、式や切り替えの「等しいか」に加わらない。</summary>
    [Fact]
    public void CachesDoNotChangeEquality()
    {
        // 語1つの式で比べる（And・Or は並びを List で持つので、前から参照でしか等しくならない）
        var first = SearchQuery.Parse("カナ");
        var second = SearchQuery.Parse("カナ");
        _ = SearchQuery.Matches(first, Hay(Items[3]), SearchOptions.Default with { KanaSensitive = false });

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());

        var options = SearchOptions.Default with { KanaSensitive = false };
        _ = SearchQuery.Matches(first, Hay(Items[3]), options);
        Assert.Equal(SearchOptions.Default with { KanaSensitive = false }, options);

        // with で対象を差し替えた写しは、元の対象の配列を使わない
        var bodyOnly = options with { Targets = new HashSet<SearchField> { SearchField.Main } };
        Assert.True(SearchQuery.Matches(SearchQuery.Parse("ほんぶん"), Hay(Items[3]), bodyOnly));
        Assert.False(SearchQuery.Matches(SearchQuery.Parse("ほんぶん"), Hay(Items[3]), options));
    }

    /// <summary>元の文字列を控えても、前と同じ並びを返す。</summary>
    [Fact]
    public void RawValuesAreTheSameAfterCaching()
    {
        var calls = 0;
        var hay = new SearchHaystack(field =>
        {
            calls++;
            return field == SearchField.Path ? [@"D:\a.zip", @"E:\b.zip"] : [];
        });

        Assert.Equal([@"D:\a.zip", @"E:\b.zip"], hay.Raw(SearchField.Path));
        Assert.Equal([@"D:\a.zip", @"E:\b.zip"], hay.Raw(SearchField.Path));
        Assert.Equal(1, calls);
    }
}
