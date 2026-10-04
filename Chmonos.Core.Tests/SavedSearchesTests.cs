using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

/// <summary>保存した検索の並びの規則（<see cref="SavedSearches"/>）。画面を通した確かめは App の SavedSearchTests。</summary>
public class SavedSearchesTests
{
    private static SearchHistoryEntry Saved(string name, string text) => new() { Name = name, Text = text };

    [Fact]
    public void 同じ名前で足すと_2行にせずその場所で置き換える()
    {
        IReadOnlyList<SearchHistoryEntry> list = [Saved("A", "夏"), Saved("B", "冬")];

        var added = SavedSearches.Add(list, Saved(" a ", "秋"));

        Assert.Equal(["a", "B"], added.Select(entry => entry.Name));
        Assert.Equal("秋", added[0].Text);
    }

    [Fact]
    public void 見つからない名前の上書きと名前の変更は何もしない()
    {
        IReadOnlyList<SearchHistoryEntry> list = [Saved("A", "夏")];

        Assert.Equal("夏", SavedSearches.Overwrite(list, "消えた", Saved("x", "冬")).Single().Text);
        Assert.Equal("A", SavedSearches.Rename(list, "消えた", "C").Single().Name);
    }

    [Fact]
    public void 名前の大文字小文字だけを直すのは同じ行なので通す_空の名前には変えない()
    {
        IReadOnlyList<SearchHistoryEntry> list = [Saved("summer", "夏")];

        Assert.Equal("Summer", SavedSearches.Rename(list, "summer", "Summer").Single().Name);
        Assert.Equal("summer", SavedSearches.Rename(list, "summer", "  ").Single().Name);
    }
}
