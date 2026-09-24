using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Search;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 商品名の読みを造語変換で照らすときに初めて作る形と、字の表の控え（2026-09-24）。
/// 前（全商品で先に作る・毎回 XML から組む）と、当たる商品が同じであることを確かめる。
/// </summary>
public sealed class LazyReadingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bam-kanji-" + Guid.NewGuid().ToString("N"));

    public LazyReadingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string KanjiPath => Path.Combine(AppContext.BaseDirectory, "assets", "kanjidic2.xml.gz");

    /// <summary>造語変換を入れていない照らし方では、読みを作らない。</summary>
    [Fact]
    public void DoesNotMakeReadingsUnlessTheyAreSearched()
    {
        var calls = 0;
        var hay = new SearchHaystack(
            field => field == SearchField.Name ? ["撫で音ギミック"] : [],
            () =>
            {
                calls++;
                return "なでおと";
            });

        Assert.False(SearchQuery.Matches(SearchQuery.Parse("なでおと"), hay, SearchOptions.Default));
        Assert.True(SearchQuery.Matches(SearchQuery.Parse("ギミック"), hay, SearchOptions.Default));
        Assert.Equal(0, calls);

        var coined = SearchOptions.Default with { IncludeReadings = true };
        Assert.True(SearchQuery.Matches(SearchQuery.Parse("なでおと"), hay, coined));
        Assert.True(SearchQuery.Matches(SearchQuery.Parse("ナデオト"), hay, coined));
        Assert.Equal(1, calls);
    }

    /// <summary>後で作った読みが、前に Build の中で作っていた読みと同じ文字列になる。</summary>
    [Fact]
    public void LazyReadingsEqualTheOldEagerOnes()
    {
        var readings = new KanjiReadings(KanjiPath);
        if (!readings.IsAvailable)
        {
            return;
        }

        var items = new[]
        {
            Item("1", "【VRChat想定】撫で音ギミック", null),
            Item("2", "指輪モデル", "手作りの指輪"),
            Item("3", "Ring Model", null),
            Item("4", "", "心音"),
        };

        foreach (var item in items)
        {
            // 前の Build の中身（2026-09-24 まで）
            var eager = new System.Text.StringBuilder();
            foreach (var name in new[] { item.Local.DisplayName, item.Booth.Name })
            {
                if (name is { Length: > 0 })
                {
                    foreach (var text in readings.Of(name))
                    {
                        eager.Append(text).Append('\n');
                    }
                }
            }

            Assert.Equal(SearchQuery.Normalize(eager.ToString()), SearchText.Build(item, readings).Readings);
        }

        Assert.Equal(string.Empty, SearchText.Build(items[0]).Readings);
    }

    /// <summary>控えから読んだ字の表が、XML から組んだ表と全部の字で同じ読みを返す。</summary>
    [Fact]
    public void CachedTableReadsLikeTheXml()
    {
        var fromXml = new KanjiReadings(KanjiPath);
        if (!fromXml.IsAvailable)
        {
            return;
        }

        var cache = Path.Combine(_dir, "kanji-readings.cache");
        var first = new KanjiReadings(KanjiPath, cache);
        first.Prepare();
        Assert.True(File.Exists(cache));
        Assert.Null(first.CacheSaveError);

        var second = new KanjiReadings(KanjiPath, cache);
        var samples = new[] { "撫で音", "指輪", "心音", "鳥", "お砂糖指輪", "々" };
        for (var c = '一'; c <= '鿿'; c++)
        {
            var text = c.ToString();
            Assert.Equal(fromXml.Of(text), second.Of(text));
        }

        foreach (var text in samples)
        {
            Assert.Equal(fromXml.Of(text), second.Of(text));
        }
    }

    /// <summary>崩れた控え・見出しの違う控えは信じずに XML から組み直す（控えは消してよい物）。</summary>
    [Fact]
    public void RebuildsFromXmlWhenTheCacheIsBrokenOrStale()
    {
        var fromXml = new KanjiReadings(KanjiPath);
        if (!fromXml.IsAvailable)
        {
            return;
        }

        var cache = Path.Combine(_dir, "kanji-readings.cache");
        new KanjiReadings(KanjiPath, cache).Prepare();
        var header = File.ReadLines(cache).First();

        File.WriteAllText(cache, header + "\n撫\n");
        Assert.Equal(fromXml.Of("撫で音"), new KanjiReadings(KanjiPath, cache).Of("撫で音"));

        File.WriteAllText(cache, "kanjidic\t0\t0-0\n撫\tぶ\n");
        Assert.Equal(fromXml.Of("撫で音"), new KanjiReadings(KanjiPath, cache).Of("撫で音"));

        // 組み直した控えは今の見出しで書き直されている
        Assert.Equal(header, File.ReadLines(cache).First());
    }

    private static ItemRecord Item(string id, string boothName, string? displayName) => new()
    {
        Id = id,
        Booth = new BoothBlock { Name = boothName },
        Local = new LocalBlock { DisplayName = displayName },
    };
}
