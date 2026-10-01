using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 取り込み元と監視対象の足し引き。画面の写しで丸ごと書くと、別の所が足した物を消していた（技術的負債 1-1 の再発）。
/// </summary>
public class FolderListChangeTests
{
    /// <summary>別の画面が先に足していた物を、こちらの足し算で消さない。</summary>
    [Fact]
    public void AddingKeepsWhatOthersAdded()
    {
        var current = new AppSettings { ImportFolders = [@"D:\a", @"D:\b"] };

        var changed = FolderListChange.AddImportFolders(current, [@"D:\c"]);

        Assert.Equal([@"D:\a", @"D:\b", @"D:\c"], changed.ImportFolders);
    }

    /// <summary>大文字小文字と末尾の区切りだけ違う物は同じ場所なので、2つにしない。</summary>
    [Fact]
    public void AddingSkipsTheSamePlace()
    {
        var current = new AppSettings { ImportFolders = [@"D:\Assets"] };

        var changed = FolderListChange.AddImportFolders(current, [@"d:\assets\", @"D:\New"]);

        Assert.Equal([@"D:\Assets", @"D:\New"], changed.ImportFolders);
    }

    [Fact]
    public void RemovingTakesOnlyThatOne()
    {
        var current = new AppSettings { ImportFolders = [@"D:\a", @"D:\B", @"D:\c"] };

        var changed = FolderListChange.RemoveImportFolder(current, @"d:\b");

        Assert.Equal([@"D:\a", @"D:\c"], changed.ImportFolders);
    }

    /// <summary>取り込み元の足し引きは監視対象に触らない（逆も）。</summary>
    [Fact]
    public void ListsDoNotTouchEachOther()
    {
        var current = new AppSettings { ImportFolders = [@"D:\a"], WatchedFolders = [@"D:\w"] };

        var added = FolderListChange.AddImportFolders(current, [@"D:\b"]);
        var watched = FolderListChange.SetWatched(current, @"D:\x", watch: true);

        Assert.Equal([@"D:\w"], added.WatchedFolders);
        Assert.Equal([@"D:\a"], watched.ImportFolders);
        Assert.Equal([@"D:\w", @"D:\x"], watched.WatchedFolders);
    }

    [Fact]
    public void UnwatchingTakesOnlyThatOne()
    {
        var current = new AppSettings { WatchedFolders = [@"D:\w", @"D:\x"] };

        var changed = FolderListChange.SetWatched(current, @"D:\W\", watch: false);

        Assert.Equal([@"D:\x"], changed.WatchedFolders);
    }

    /// <summary>手で直した JSON で配列が欠けていても落ちない。</summary>
    [Fact]
    public void MissingListIsEmpty()
    {
        var current = new AppSettings { ImportFolders = null!, WatchedFolders = null! };

        Assert.Equal([@"D:\a"], FolderListChange.AddImportFolders(current, [@"D:\a"]).ImportFolders);
        Assert.Empty(FolderListChange.SetWatched(current, @"D:\a", watch: false).WatchedFolders);
    }
}
