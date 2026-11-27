using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

/// <summary>
/// 条件「保存した検索」の Core の判定（<see cref="SavedSearchCondition"/>・<see cref="SavedSearchReferences"/>）。
/// 画面の「呼び出した結果と同じ」は App の SavedSearchConditionTests が見る。ここは手で直した JSON の輪・入れ子・同じ種類のまとまり・
/// 組むのが数え直し1回に名前ごと1回か・名前の変更に付いていくか、を見る。
/// </summary>
public class SavedSearchConditionTests
{
    private static ItemRecord Item(string id, string name, int wish = 0)
        => new()
        {
            Id = id,
            Booth = new BoothBlock { Name = name, WishListsCount = wish },
            Local = new LocalBlock(),
        };

    private static readonly ItemRecord[] Items =
    [
        Item("1", "夏のワンピース", wish: 5),
        Item("2", "夏の髪", wish: 50),
        Item("3", "冬のコート", wish: 500),
    ];

    private static SearchHistoryEntry Saved(string name, string text = "", params SearchModuleState[] modules)
        => new() { Name = name, Text = text, Modules = modules };

    private static SearchModuleState Use(string name, bool exclude = false, bool ignoreText = false)
        => new() { Kind = "SavedSearch", Items = [name], Exclude = exclude, IgnoreText = ignoreText };

    private static SearchModuleContext Context(IReadOnlyList<SearchHistoryEntry> saved, Func<SearchNode, SearchNode>? widen = null) => new(
        () => AvatarCompatibilityIndex.Build(new AvatarRegistry()),
        ModificationUsage.Empty,
        RecentTimes.Empty,
        null,
        new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.FromHours(9)))
    {
        Items = Items,
        SavedSearches = saved,
        Haystack = item => SearchText.Build(item),
        WidenText = widen,
    };

    private static string[] Hits(SearchModuleState state, SearchModuleContext context)
    {
        var condition = SearchModuleConditions.Build(state, context)!;
        return Items.Where(item => state.Exclude ? condition.MatchesExcluded(item, context) : condition.Matches(item, context))
            .Select(item => item.Id).ToArray();
    }

    [Fact]
    public void 文字と条件で当て_同じ種類の範囲はどれかに当たれば通す()
    {
        var saved = new[]
        {
            Saved("少ないか多い", "",
                new() { Kind = "WishList", Min = "0", Max = "10" },
                new() { Kind = "WishList", Min = "100", Max = string.Empty }),
            Saved("夏で少ないか多い", "夏", Use("少ないか多い")),
        };
        var context = Context(saved);

        Assert.Equal(["1", "3"], Hits(Use("少ないか多い"), context));
        Assert.Equal(["1"], Hits(Use("夏で少ないか多い"), context));
        Assert.Equal(["1", "3"], Hits(Use("夏で少ないか多い", ignoreText: true), context));
        Assert.Equal(["2", "3"], Hits(Use("夏で少ないか多い", exclude: true), context));
    }

    [Fact]
    public void 効かせていない条件と_除くを持たない種類に書かれた除くは読まない()
    {
        var saved = new[]
        {
            Saved("S", "",
                new() { Kind = "WishList", Min = "0", Max = "10", Enabled = false },
                new() { Kind = "Favorite", Choice = "other", Exclude = true }),
        };

        // お気に入りでない物（全部）。除くを読むと0件になる
        Assert.Equal(["1", "2", "3"], Hits(Use("S"), Context(saved)));
    }

    [Fact]
    public void 手で直したJSONの輪は_当たらず_除くと全部が通る()
    {
        var saved = new[]
        {
            Saved("A", "夏", Use("B")),
            Saved("B", "", Use("A")),
            Saved("C", "", Use("A")),
            Saved("自分", "", Use("自分")),
        };
        var context = Context(saved);

        Assert.Equal(["A", "B", "自分"], SavedSearchReferences.CycleNames(saved).Order(StringComparer.Ordinal).ToArray());
        Assert.Empty(Hits(Use("A"), context));
        Assert.Empty(Hits(Use("自分"), context));
        Assert.Equal(["1", "2", "3"], Hits(Use("A", exclude: true), context));

        // C は輪の中ではないが、中で指す A が輪なので当たらない。欄に出す理由は A の側
        Assert.Equal(SavedSearchProblem.None, SavedSearchReferences.ProblemOf(saved, "C"));
        Assert.Equal(SavedSearchProblem.Cycle, SavedSearchReferences.ProblemOf(saved, "A"));
        Assert.Empty(Hits(Use("C"), context));
    }

    [Fact]
    public void 元が無いと当たらず_除くと全部が通る()
    {
        var context = Context([Saved("A", "夏")]);

        Assert.Empty(Hits(Use("無い"), context));
        Assert.Equal(["1", "2", "3"], Hits(Use("無い", exclude: true), context));
        Assert.Equal(SavedSearchProblem.Missing, SavedSearchReferences.ProblemOf(context.SavedSearches, "無い"));
        Assert.True(SearchModuleConditions.From(Use("無い"))!.IsActive);
    }

    [Fact]
    public void 組むのは数え直し1回に名前ごと1回で_入れ子と複数の条件で使い回す()
    {
        var widened = 0;
        var saved = new[]
        {
            Saved("A", "夏") with { SearchAlternates = true },
            Saved("B", "", Use("A")),
        };
        var context = Context(saved, node =>
        {
            widened++;
            return node;
        });

        Hits(Use("A"), context);
        Hits(Use("B"), context);
        Hits(Use("A", exclude: true), context);

        Assert.Equal(1, widened);
    }

    [Fact]
    public void 別表記は_保存した検索で入れたときだけ広げる()
    {
        var calls = 0;
        SearchNode Widen(SearchNode node)
        {
            calls++;
            return node;
        }

        Hits(Use("A"), Context([Saved("A", "夏") with { SearchAlternates = false }], Widen));
        Assert.Equal(0, calls);
    }

    /// <summary>打つたびの数え直しの中で索引を組むと数秒止まる。組めていない節は引かずに、そのまま返す（ユーザ判断 2026-11-27 ①a）</summary>
    [Fact]
    public void 組めている索引だけで広げるときは_索引を組まず控えも書かない()
    {
        if (!SharedDictionaries.JapaneseAvailable)
        {
            return;
        }

        var cache = Path.Combine(Path.GetTempPath(), $"chmonos-loadedonly-{Guid.NewGuid():N}", "search-bridge.cache");
        var bridge = new Search.SearchBridge(new Search.JapaneseDictionary(SharedDictionaries.JapanesePath, cache));
        var node = SearchQuery.Parse("bird");

        var widened = bridge.WidenLoaded(node);

        Assert.Same(node, widened);
        Assert.False(File.Exists(cache));
    }

    [Fact]
    public void 名前の変更に付いていき_消したときは書き換えない()
    {
        var saved = new[] { Saved("A", "夏"), Saved("B", "", Use("a")) };

        var renamed = SavedSearches.Rename(saved, "A", "夏の物");
        Assert.Equal(["夏の物"], renamed[1].Modules[0].Items);

        var removed = SavedSearches.Remove(saved, "A");
        Assert.Equal(["a"], removed.Single().Modules[0].Items);
    }

    [Fact]
    public void 保存と上書きで断る輪は_並び全体で判定する()
    {
        var saved = new[] { Saved("A", "", Use("B")), Saved("B", "") };

        Assert.False(SavedSearchReferences.InCycle(saved, "A"));
        Assert.True(SavedSearchReferences.InCycle(SavedSearches.Overwrite(saved, "B", Saved("B", "", Use("A"))), "B"));
    }

    [Fact]
    public void 検索文字列も使うの既定は書かず_外したときだけ書いて読み戻す()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Use("A"), Storage.JsonStore.Options);
        Assert.DoesNotContain("ignoreText", json);

        var off = System.Text.Json.JsonSerializer.Serialize(Use("A", ignoreText: true), Storage.JsonStore.Options);
        Assert.Contains("\"ignoreText\": true", off);
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<SearchModuleState>(off, Storage.JsonStore.Options)!.IgnoreText);
        Assert.NotEqual(Use("A").Fingerprint, Use("A", ignoreText: true).Fingerprint);
    }
}
