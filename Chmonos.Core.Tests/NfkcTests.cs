using Chmonos.Core.Search;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 絵文字・片割れのサロゲート・結合文字を、畳む道のどこに通しても落ちないこと。
/// 検索欄に絵文字を打っただけで検索が落ちていた（1字ずつ NFKC に通して片割れを作っていた・点検 2026-09-23）。
/// </summary>
public class NfkcTests
{
    // 片割れ：文字列を途中で切ると絵文字の前半だけが残る
    private const string LoneHigh = "猫\uD83D";
    private const string LoneLow = "\uDC31猫";
    private const string Cat = "🐱";

    [Fact]
    public void FoldKeepsEmojiAndDropsOnlyLoneSurrogates()
    {
        Assert.Equal("🐱ab", Nfkc.Fold("🐱ＡＢ".ToLowerInvariant()).ToLowerInvariant());
        Assert.Equal("猫", Nfkc.Fold(LoneHigh));
        Assert.Equal("猫", Nfkc.Fold(LoneLow));
        Assert.Equal(string.Empty, Nfkc.Fold(null));
    }

    [Fact]
    public void FoldComposesCombiningMarks()
    {
        // 濁点を別の字で持つ名前（Mac で作ったファイル名に多い）も、打った「が」に当たるように
        Assert.Equal("が", Nfkc.Fold("か\u3099"));
        Assert.Equal("é", Nfkc.Fold("e\u0301"));
    }

    [Fact]
    public void FoldCharLeavesSurrogatesAlone()
    {
        Assert.Equal('\uD83D', Nfkc.FoldChar('\uD83D'));
        Assert.Equal('(', Nfkc.FoldChar('（'));
    }

    public static TheoryData<string> BrokenQueries => new()
    {
        Cat, "🐱 猫", "（🐱）", "\"🐱\"", "-🐱", "name:🐱", "🐱 OR 猫", LoneHigh, LoneLow, "👨‍👩‍👧", "か\u3099",
    };

    [Theory]
    [MemberData(nameof(BrokenQueries))]
    public void SearchParsesAndMatchesUnderEveryOption(string query)
    {
        var hay = SearchHaystack.FromValues(
            new Dictionary<SearchField, string[]> { [SearchField.Name] = ["🐱猫の服" + LoneHigh], [SearchField.Shop] = [LoneLow] },
            readings: "ねこ" + LoneHigh);
        var node = SearchQuery.Parse(query);

        foreach (var caseSensitive in new[] { false, true })
        foreach (var widthSensitive in new[] { false, true })
        foreach (var kanaSensitive in new[] { false, true })
        {
            var options = new SearchOptions
            {
                CaseSensitive = caseSensitive,
                WidthSensitive = widthSensitive,
                KanaSensitive = kanaSensitive,
                IncludeReadings = true,
            };
            _ = SearchQuery.Matches(node, hay, options);
        }
    }

    [Fact]
    public void EmojiQueryFindsEmojiInName()
    {
        var hay = SearchHaystack.FromValues(new Dictionary<SearchField, string[]> { [SearchField.Name] = ["🐱猫の服"] });

        Assert.True(SearchQuery.Matches(SearchQuery.Parse("🐱"), hay, SearchOptions.Default));
        Assert.True(SearchQuery.Matches(SearchQuery.Parse("（🐱 猫）"), hay, SearchOptions.Default));
        Assert.False(SearchQuery.Matches(SearchQuery.Parse("🐶"), hay, SearchOptions.Default));
    }

    [Fact]
    public void CombiningMarksMatchComposedQuery()
    {
        var hay = SearchHaystack.FromValues(new Dictionary<SearchField, string[]> { [SearchField.Name] = ["か\u3099くらん"] });

        Assert.True(SearchQuery.Matches(SearchQuery.Parse("がくらん"), hay, SearchOptions.Default));
    }

    [Fact]
    public void ItemTextFilterAcceptsEmoji()
    {
        // タグと属性の管理の絞り込みも同じ読み方を通る
        var filter = ItemTextFilter.Create("🐱 " + LoneHigh);

        Assert.NotNull(filter);
        Assert.True(filter!.MatchesName("🐱猫"));
    }

    [Theory]
    [MemberData(nameof(BrokenQueries))]
    public void AvatarTextHelpersDoNotThrow(string text)
    {
        _ = AvatarText.Normalize(text);
        _ = AvatarText.StripForMatch(text);
        _ = AvatarText.DisplayNameFrom(text);
        _ = AvatarText.ShortenName(text);
        _ = AvatarText.InitialOf(text);
        _ = AvatarText.IsNotAName(text);
        _ = AvatarBaseKeys.Key(text);
        _ = AvatarBaseKeys.MentionsIn(text).ToList();
        _ = AvatarNameIndex.NamesOf(new Models.AvatarRegistryEntry { ItemId = "1", BoothName = text });
        _ = DateText.Parse(text, isEnd: false, new DateOnly(2026, 9, 23));
        _ = MoneyText.Parse(text);
        _ = RomajiReading.Readings(text);
    }
}
