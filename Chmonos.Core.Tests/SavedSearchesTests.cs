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

    // ---- フォルダ（1階層・ユーザ判断 2026-11-27） ----

    private static SearchHistoryEntry InFolder(string name, string? folder) => new() { Name = name, Folder = folder };

    private static string[] Names(IEnumerable<SearchHistoryEntry> entries) => entries.Select(entry => entry.Name ?? "").ToArray();

    [Fact]
    public void 一覧はフォルダの外を先に_フォルダは初めて出てきた順でまとめる()
    {
        IReadOnlyList<SearchHistoryEntry> list = [InFolder("a", "衣装"), InFolder("b", null), InFolder("c", "髪"), InFolder("d", " 衣装 "), InFolder("e", "  ")];

        Assert.Equal(["b", "e", "a", "d", "c"], Names(SavedSearches.Grouped(list)));
        Assert.Equal(["衣装", "髪"], SavedSearches.Folders(list));
    }

    [Fact]
    public void フォルダへ移すと_移した先の最後の行の後ろに置き_綴りは今あるフォルダに寄せる()
    {
        IReadOnlyList<SearchHistoryEntry> list = [InFolder("a", null), InFolder("b", "衣装"), InFolder("c", "髪"), InFolder("d", "衣装")];

        var moved = SavedSearches.MoveToFolder(list, "a", "髪");
        Assert.Equal(["b", "c", "a", "d"], Names(moved));
        Assert.Equal("髪", moved[2].Folder);

        var cased = SavedSearches.MoveToFolder([InFolder("x", "Summer"), InFolder("y", null)], "y", " summer ");
        Assert.Equal(["Summer", "Summer"], cased.Select(entry => entry.Folder));

        var outside = SavedSearches.MoveToFolder(list, "c", null);
        Assert.Equal(["a", "c", "b", "d"], Names(outside));
        Assert.Null(outside[1].Folder);
    }

    [Fact]
    public void 新しいフォルダへ移すと末尾に置く_見つからない名前は何もしない()
    {
        IReadOnlyList<SearchHistoryEntry> list = [InFolder("a", null), InFolder("b", null)];

        Assert.Equal(["b", "a"], Names(SavedSearches.MoveToFolder(list, "a", "新しい")));
        Assert.Equal(["a", "b"], Names(SavedSearches.MoveToFolder(list, "消えた", "新しい")));
    }

    [Fact]
    public void 上へ下へは同じフォルダの中の隣と入れ替え_フォルダの中の端では動かさない()
    {
        IReadOnlyList<SearchHistoryEntry> list = [InFolder("a", "衣装"), InFolder("b", null), InFolder("c", "衣装"), InFolder("d", null)];

        Assert.Equal(["c", "b", "a", "d"], Names(SavedSearches.Move(list, "c", -1)));
        Assert.Equal(["a", "b", "c", "d"], Names(SavedSearches.Move(list, "a", -1)));
        Assert.Equal(["a", "b", "c", "d"], Names(SavedSearches.Move(list, "c", +1)));
        Assert.Equal(["a", "d", "c", "b"], Names(SavedSearches.Move(list, "b", +1)));
    }

    [Fact]
    public void フォルダの名前の変更は中身をまとめて移し_ほかのフォルダと同じ名前ならまとめる()
    {
        IReadOnlyList<SearchHistoryEntry> list = [InFolder("a", "衣装"), InFolder("b", "髪"), InFolder("c", "衣装")];

        Assert.Equal(["服", "髪", "服"], SavedSearches.RenameFolder(list, "衣装", " 服 ").Select(entry => entry.Folder));
        Assert.Equal(["髪", "髪", "髪"], SavedSearches.RenameFolder(list, "衣装", "髪").Select(entry => entry.Folder));
        Assert.Equal(["衣装", "髪", "衣装"], SavedSearches.RenameFolder(list, "衣装", "  ").Select(entry => entry.Folder));
    }

    [Fact]
    public void フォルダを消すと中身はフォルダの外へ出て_保存した検索は残る()
    {
        IReadOnlyList<SearchHistoryEntry> list = [InFolder("a", "衣装"), InFolder("b", "髪")];

        var removed = SavedSearches.RemoveFolder(list, "衣装");

        Assert.Equal(["a", "b"], Names(removed));
        Assert.Equal([null, "髪"], removed.Select(entry => entry.Folder));
    }

    [Fact]
    public void 上書きと同じ名前で足したときは_フォルダを残す()
    {
        IReadOnlyList<SearchHistoryEntry> list = [InFolder("a", "衣装")];

        Assert.Equal("衣装", SavedSearches.Overwrite(list, "a", Saved("x", "冬")).Single().Folder);
        Assert.Equal("衣装", SavedSearches.Add(list, Saved("A", "冬")).Single().Folder);
    }

    [Fact]
    public void フォルダは指紋に入らず_フォルダの外ならJSONに書かない()
    {
        var entry = Saved("a", "夏");
        var options = Chmonos.Core.Storage.JsonStore.Options;

        Assert.Equal(entry.Fingerprint, (entry with { Folder = "衣装" }).Fingerprint);
        Assert.DoesNotContain("folder", System.Text.Json.JsonSerializer.Serialize(entry, options));
        Assert.Contains("\"folder\": \"衣装\"", System.Text.Json.JsonSerializer.Serialize(entry with { Folder = "衣装" }, options));
    }
}
